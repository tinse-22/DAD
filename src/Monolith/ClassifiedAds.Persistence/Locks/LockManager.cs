using ClassifiedAds.CrossCuttingConcerns.Locks;
using ClassifiedAds.CrossCuttingConcerns.DateTimes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System;

namespace ClassifiedAds.Persistence.Locks;

public class LockManager : ILockManager
{
    private readonly AdsDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LockManager(AdsDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    private void CreateLock(string entityName, string entityId)
    {
        string sql = @"
            INSERT INTO ""Locks"" (""EntityName"", ""EntityId"")
            VALUES (@entityName, @entityId)
            ON CONFLICT (""EntityName"", ""EntityId"") DO NOTHING";

        _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("entityName", entityName),
              new NpgsqlParameter("entityId", entityId));
    }

    public bool AcquireLock(string entityName, string entityId, string ownerId, TimeSpan expirationIn)
    {
        CreateLock(entityName, entityId);

        if (ExtendLock(entityName, entityId, ownerId, expirationIn))
        {
            return true;
        }

        var now = _dateTimeProvider.OffsetNow;
        var expired = now + expirationIn;

        string sql = @"
            UPDATE ""Locks"" SET ""OwnerId"" = @OwnerId, 
            ""AcquiredDateTime"" = @AcquiredDateTime,
            ""ExpiredDateTime"" = @ExpiredDateTime
            WHERE ""EntityId"" = @EntityId 
            AND ""EntityName"" = @EntityName 
            AND (""OwnerId"" IS NULL OR ""ExpiredDateTime"" < @AcquiredDateTime)";

        var rs = _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("EntityName", entityName),
              new NpgsqlParameter("EntityId", entityId),
              new NpgsqlParameter("OwnerId", ownerId),
              new NpgsqlParameter("AcquiredDateTime", NpgsqlDbType.TimestampTz) { Value = now },
              new NpgsqlParameter("ExpiredDateTime", NpgsqlDbType.TimestampTz) { Value = expired });

        return rs > 0;
    }

    public bool ExtendLock(string entityName, string entityId, string ownerId, TimeSpan expirationIn)
    {
        var now = _dateTimeProvider.OffsetNow;
        var expired = now + expirationIn;

        string sql = @"
            UPDATE ""Locks"" SET ""ExpiredDateTime"" = @ExpiredDateTime
            WHERE ""EntityId"" = @EntityId 
            AND ""EntityName"" = @EntityName 
            AND ""OwnerId"" = @OwnerId";

        var rs = _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("EntityName", entityName),
              new NpgsqlParameter("EntityId", entityId),
              new NpgsqlParameter("OwnerId", ownerId),
              new NpgsqlParameter("ExpiredDateTime", NpgsqlDbType.TimestampTz) { Value = expired });

        return rs > 0;
    }

    public bool ReleaseLock(string entityName, string entityId, string ownerId)
    {
        string sql = @"
            UPDATE ""Locks"" SET ""OwnerId"" = NULL, 
            ""AcquiredDateTime"" = NULL,
            ""ExpiredDateTime"" = NULL
            WHERE ""EntityId"" = @EntityId 
            AND ""EntityName"" = @EntityName 
            AND ""OwnerId"" = @OwnerId";

        _ = _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("EntityName", entityName),
              new NpgsqlParameter("EntityId", entityId),
              new NpgsqlParameter("OwnerId", ownerId));

        return true;
    }

    public bool ReleaseLocks(string ownerId)
    {
        string sql = @"
            UPDATE ""Locks"" SET ""OwnerId"" = NULL, 
            ""AcquiredDateTime"" = NULL,
            ""ExpiredDateTime"" = NULL
            WHERE ""OwnerId"" = @OwnerId";

        _ = _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("OwnerId", ownerId));

        return true;
    }

    public bool ReleaseExpiredLocks()
    {
        var now = _dateTimeProvider.OffsetNow;

        string sql = @"
            UPDATE ""Locks"" SET ""OwnerId"" = NULL, 
            ""AcquiredDateTime"" = NULL,
            ""ExpiredDateTime"" = NULL
            WHERE ""ExpiredDateTime"" < @now";

        _ = _dbContext.Database.ExecuteSqlRaw(sql,
              new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });

        return true;
    }

    public void EnsureAcquiringLock(string entityName, string entityId, string ownerId, TimeSpan expirationIn)
    {
        if (!AcquireLock(entityName, entityId, ownerId, expirationIn))
        {
            throw new CouldNotAcquireLockException();
        }
    }
}
