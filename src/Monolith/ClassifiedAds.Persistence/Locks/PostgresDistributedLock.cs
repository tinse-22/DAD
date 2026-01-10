using ClassifiedAds.CrossCuttingConcerns.Locks;
using Npgsql;
using System;
using System.Data;

namespace ClassifiedAds.Persistence.Locks;

public class PostgresDistributedLock : IDistributedLock
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly string _connectionString;

    public bool HasTransaction => _transaction != null;

    public PostgresDistributedLock(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public PostgresDistributedLock(NpgsqlTransaction transaction)
    {
        _transaction = transaction;
        _connection = _transaction.Connection;
    }

    public PostgresDistributedLock(string connectionString)
    {
        _connectionString = connectionString;
        _connection = new NpgsqlConnection(connectionString);
    }

    public IDistributedLockScope Acquire(string lockName)
    {
        EnsureOpen();

        var lockId = GetLockId(lockName);
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "SELECT pg_advisory_lock(@lockId)";
        command.Parameters.AddWithValue("lockId", lockId);
        command.ExecuteNonQuery();

        return new PostgresDistributedLockScope(_connection, _transaction, lockName, lockId);
    }

    public IDistributedLockScope TryAcquire(string lockName)
    {
        EnsureOpen();

        var lockId = GetLockId(lockName);
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "SELECT pg_try_advisory_lock(@lockId)";
        command.Parameters.AddWithValue("lockId", lockId);
        var result = (bool)command.ExecuteScalar();

        if (result)
        {
            return new PostgresDistributedLockScope(_connection, _transaction, lockName, lockId);
        }

        return null;
    }

    private void EnsureOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    private static long GetLockId(string lockName)
    {
        // Use a hash of the lock name as the lock ID
        return (long)lockName.GetHashCode();
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_connectionString))
        {
            _connection.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
