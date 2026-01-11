using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Npgsql;
using System;
using System.Collections.Generic;

namespace ClassifiedAds.Infrastructure.Localization;

public class PostgreSqlStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly PostgreSqlLocalizationOptions _options;
    private readonly IMemoryCache _memoryCache;

    public PostgreSqlStringLocalizerFactory(IOptions<PostgreSqlLocalizationOptions> options, IMemoryCache memoryCache)
    {
        _options = options.Value;
        _memoryCache = memoryCache;
    }

    public IStringLocalizer Create(Type resourceSource)
    {
        return new PostgreSqlStringLocalizer(LoadData());
    }

    public IStringLocalizer Create(string baseName, string location)
    {
        return new PostgreSqlStringLocalizer(LoadData());
    }

    private Dictionary<string, Dictionary<string, string>> LoadData()
    {
        var data = _memoryCache.Get<Dictionary<string, Dictionary<string, string>>>(typeof(PostgreSqlStringLocalizerFactory).FullName);

        if (data == null)
        {
            using var conn = new NpgsqlConnection(_options.ConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(_options.SqlQuery, conn);
            using var reader = cmd.ExecuteReader();

            data = new Dictionary<string, Dictionary<string, string>>();
            while (reader.Read())
            {
                var name = reader.GetString(reader.GetOrdinal("Name"));
                var culture = reader.GetString(reader.GetOrdinal("Culture"));
                var value = reader.GetString(reader.GetOrdinal("Value"));

                if (!data.ContainsKey(name))
                {
                    data[name] = new Dictionary<string, string>();
                }

                data[name][culture] = value;
            }

            _memoryCache.Set(typeof(PostgreSqlStringLocalizerFactory).FullName, data, DateTimeOffset.Now.AddMinutes(_options.CacheMinutes));
        }

        return data;
    }
}

public class LocalizationEntry
{
    public string Name { get; set; }

    public string Value { get; set; }

    public string Culture { get; set; }
}
