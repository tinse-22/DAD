using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClassifiedAds.Infrastructure.HealthChecks;

public class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    private readonly string _sql;

    public PostgresHealthCheck(string connectionString, string sql)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException("connectionString");
        _sql = sql ?? throw new ArgumentNullException("sql");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
    {
        try
        {
            using NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using (NpgsqlCommand command = connection.CreateCommand())
            {
                command.CommandText = _sql;
                await command.ExecuteScalarAsync(cancellationToken);
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, null, exception);
        }
    }
}
