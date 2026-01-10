using ClassifiedAds.CrossCuttingConcerns.Locks;
using Npgsql;

namespace ClassifiedAds.Persistence.Locks;

public class PostgresDistributedLockScope : IDistributedLockScope
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly string _lockName;
    private readonly long _lockId;

    public bool HasTransaction => _transaction != null;

    public PostgresDistributedLockScope(NpgsqlConnection connection, NpgsqlTransaction transaction, string lockName, long lockId)
    {
        _connection = connection;
        _transaction = transaction;
        _lockName = lockName;
        _lockId = lockId;
    }

    public void Dispose()
    {
        if (HasTransaction)
        {
            return;
        }

        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "SELECT pg_advisory_unlock(@lockId)";
        command.Parameters.AddWithValue("lockId", _lockId);
        command.ExecuteNonQuery();
    }

    public bool StillHoldingLock()
    {
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND objid = @lockId AND pid = pg_backend_pid())";
        command.Parameters.AddWithValue("lockId", (int)_lockId);
        return (bool)command.ExecuteScalar();
    }
}
