using CryptographyHelper;
using CryptographyHelper.AsymmetricAlgorithms;
using CryptographyHelper.Certificates;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Collections.Generic;
using System.Linq;

namespace ClassifiedAds.Infrastructure.Configuration;

public class SqlConfigurationProvider : ConfigurationProvider
{
    private readonly PostgreSqlConfigurationOptions _options;

    public SqlConfigurationProvider(PostgreSqlConfigurationOptions options)
    {
        _options = options;
    }

    public override void Load()
    {
        using var conn = new NpgsqlConnection(_options.ConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(_options.SqlQuery, conn);
        using var reader = cmd.ExecuteReader();

        var data = new List<ConfigurationEntry>();
        while (reader.Read())
        {
            data.Add(new ConfigurationEntry
            {
                Key = reader.GetString(reader.GetOrdinal("Key")),
                Value = reader.GetString(reader.GetOrdinal("Value")),
                IsSensitive = reader.GetBoolean(reader.GetOrdinal("IsSensitive"))
            });
        }

        var cert = data.Any(x => x.IsSensitive)
            ? _options.Certificate.FindCertificate()
            : null;

        foreach (var entry in data)
        {
            if (entry.IsSensitive)
            {
                var decrypted = entry.Value.FromBase64String().UseRSA(cert).Decrypt();
                entry.Value = decrypted.GetString();
            }
        }

        Data = data.ToDictionary(c => c.Key, c => c.Value);
    }
}

public class ConfigurationEntry
{
    public string Key { get; set; }

    public string Value { get; set; }

    public bool IsSensitive { get; set; }
}

public class PostgreSqlConfigurationOptions
{
    public bool IsEnabled { get; set; }

    public string ConnectionString { get; set; }

    public string SqlQuery { get; set; }

    public CertificateOption Certificate { get; set; }
}

public class SqlConfigurationSource : IConfigurationSource
{
    private readonly PostgreSqlConfigurationOptions _options;

    public SqlConfigurationSource(PostgreSqlConfigurationOptions options)
    {
        _options = options;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SqlConfigurationProvider(_options);
    }
}

public static class SqlConfigurationExtensions
{
    public static IConfigurationBuilder AddPostgreSql(this IConfigurationBuilder builder, PostgreSqlConfigurationOptions options)
    {
        return builder.Add(new SqlConfigurationSource(options));
    }
}
