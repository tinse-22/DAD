# 💾 Persistence Layer

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [Cấu trúc thư mục](#cấu-trúc-thư-mục)
3. [DbContext](#dbcontext)
4. [Entity Configurations](#entity-configurations)
5. [Repository Implementation](#repository-implementation)
6. [Unit of Work](#unit-of-work)
7. [Interceptors](#interceptors)
8. [Distributed Locks](#distributed-locks)
9. [Multi-Tenancy](#multi-tenancy)
10. [Migrations](#migrations)
11. [Quy tắc Implementation](#quy-tắc-implementation)

---

## Tổng quan

**Persistence Layer** chứa implementations liên quan đến database:
- Entity Framework Core DbContext
- Repository implementations
- Entity configurations (Fluent API)
- Database interceptors
- Distributed locks
- Multi-tenancy support

**Đặc điểm:**
- ✅ Implement repository interfaces từ Domain
- ✅ Chứa EF Core configurations
- ❌ **KHÔNG** expose DbContext ra ngoài
- ✅ Encapsulate tất cả database logic

---

## Cấu trúc thư mục

```
ClassifiedAds.Persistence/
├── AdsDbContext.cs                    # Main DbContext
├── AdsDbContextMultiTenant.cs         # Multi-tenant DbContext
├── PersistenceExtensions.cs           # DI registration
├── CircuitBreakers/                   # Circuit breaker persistence
│   └── CircuitBreakerManager.cs
├── DbConfigurations/                  # Entity configurations
│   ├── ProductConfiguration.cs
│   ├── UserConfiguration.cs
│   ├── FileEntryConfiguration.cs
│   └── ...
├── Interceptors/                      # EF Core interceptors
│   ├── SelectWithoutWhereCommandInterceptor.cs
│   └── SelectWhereInCommandInterceptor.cs
├── Locks/                            # Distributed locks
│   ├── LockManager.cs
│   └── PostgresDistributedLock.cs
└── Repositories/                      # Repository implementations
    ├── Repository.cs                  # Generic repository
    ├── UserRepository.cs
    ├── RoleRepository.cs
    └── ...
```

---

## DbContext

### Main DbContext

```csharp
// AdsDbContext.cs
public class AdsDbContext : DbContext, IUnitOfWork, IDataProtectionKeyContext
{
    private readonly ILogger<AdsDbContext> _logger;
    private IDbContextTransaction _dbContextTransaction;

    public AdsDbContext(DbContextOptions<AdsDbContext> options, ILogger<AdsDbContext> logger)
        : base(options)
    {
        _logger = logger;
    }

    // Data Protection Keys (for encryption)
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    // Transaction Management
    public async Task<IDisposable> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        _dbContextTransaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        return _dbContextTransaction;
    }

    // Transaction with Distributed Lock
    public async Task<IDisposable> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        string lockName = null,
        CancellationToken cancellationToken = default)
    {
        _dbContextTransaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);

        // Acquire distributed lock
        var postgresLock = new PostgresDistributedLock(
            _dbContextTransaction.GetDbTransaction() as NpgsqlTransaction);
        var lockScope = postgresLock.Acquire(lockName);

        if (lockScope == null)
        {
            throw new Exception($"Could not acquire lock: {lockName}");
        }

        return _dbContextTransaction;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _dbContextTransaction.CommitAsync(cancellationToken);
    }

    // Apply configurations
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    // Add interceptors
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new SelectWithoutWhereCommandInterceptor(_logger));
        optionsBuilder.AddInterceptors(new SelectWhereInCommandInterceptor(_logger));
    }

    // Override SaveChanges for audit/outbox
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetOutboxActivityId();        // Set correlation ID for outbox messages
        HandleFileEntriesDeleted();   // Handle file deletions
        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

---

## Entity Configurations

### Sử dụng Fluent API thay vì Data Annotations

```csharp
// ProductConfiguration.cs
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Table name
        builder.ToTable("Products");

        // Primary key với auto-generate
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");  // PostgreSQL

        // Required properties
        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Optional properties
        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        // Indexes
        builder.HasIndex(x => x.Code)
            .IsUnique();

        // Seed data
        builder.HasData(new List<Product>
        {
            new Product
            {
                Id = Guid.Parse("6672E891-0D94-4620-B38A-DBC5B02DA9F7"),
                Code = "TEST",
                Name = "Test",
                Description = "Description"
            }
        });
    }
}
```

### Entity với Relationships

```csharp
// UserConfiguration.cs
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // One-to-Many relationship
        builder.HasMany(x => x.UserRoles)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.UserClaims)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.UserLogins)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### Entity với Vector (pgvector)

```csharp
// ProductEmbeddingConfiguration.cs
public class ProductEmbeddingConfiguration : IEntityTypeConfiguration<ProductEmbedding>
{
    public void Configure(EntityTypeBuilder<ProductEmbedding> builder)
    {
        builder.ToTable("ProductEmbeddings");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // Vector column for AI embeddings
        builder.Property(x => x.Embedding)
            .HasColumnType("vector(1536)");  // OpenAI embedding dimension

        // Relationship
        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId);

        // Index for vector search
        builder.HasIndex(x => x.Embedding)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops");
    }
}
```

---

## Repository Implementation

### Generic Repository

```csharp
// Repository.cs
public class Repository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : Entity<TKey>, IAggregateRoot
{
    private readonly AdsDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    protected DbSet<TEntity> DbSet => _dbContext.Set<TEntity>();

    public IUnitOfWork UnitOfWork => _dbContext;

    public Repository(AdsDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    // QUERY
    public IQueryable<TEntity> GetQueryableSet()
    {
        return _dbContext.Set<TEntity>();
    }

    // CREATE
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedDateTime = _dateTimeProvider.OffsetNow;  // Audit
        await DbSet.AddAsync(entity, cancellationToken);
    }

    // UPDATE
    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedDateTime = _dateTimeProvider.OffsetNow;  // Audit
        return Task.CompletedTask;
    }

    // DELETE
    public void Delete(TEntity entity)
    {
        DbSet.Remove(entity);
    }

    // ADD OR UPDATE
    public async Task AddOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.Id.Equals(default(TKey)))
        {
            await AddAsync(entity, cancellationToken);
        }
        else
        {
            await UpdateAsync(entity, cancellationToken);
        }
    }

    // ASYNC QUERY EXECUTION
    public Task<T> FirstOrDefaultAsync<T>(IQueryable<T> query)
        => query.FirstOrDefaultAsync();

    public Task<T> SingleOrDefaultAsync<T>(IQueryable<T> query)
        => query.SingleOrDefaultAsync();

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query)
        => query.ToListAsync();

    // BULK OPERATIONS (using SimpleBulks library)
    public async Task BulkInsertAsync(IReadOnlyCollection<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dbContext.BulkInsertAsync(entities, cancellationToken: cancellationToken);
    }

    public async Task BulkUpdateAsync(IReadOnlyCollection<TEntity> entities, 
        Expression<Func<TEntity, object>> columnNamesSelector, 
        CancellationToken cancellationToken = default)
    {
        await _dbContext.BulkUpdateAsync(entities, columnNamesSelector, cancellationToken: cancellationToken);
    }

    public async Task BulkDeleteAsync(IReadOnlyCollection<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dbContext.BulkDeleteAsync(entities, cancellationToken: cancellationToken);
    }

    // CONCURRENCY HANDLING
    public void SetRowVersion(TEntity entity, byte[] version)
    {
        _dbContext.Entry(entity).OriginalValues[nameof(entity.RowVersion)] = version;
    }

    public bool IsDbUpdateConcurrencyException(Exception ex)
    {
        return ex is DbUpdateConcurrencyException;
    }
}
```

### Specialized Repository

```csharp
// UserRepository.cs
public class UserRepository : Repository<User, Guid>, IUserRepository
{
    public UserRepository(AdsDbContext dbContext, IDateTimeProvider dateTimeProvider)
        : base(dbContext, dateTimeProvider)
    {
    }

    // Custom query methods
    public async Task<User> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await FirstOrDefaultAsync(
            GetQueryableSet()
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .Where(x => x.UserName == username));
    }

    public async Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await FirstOrDefaultAsync(
            GetQueryableSet().Where(x => x.Email == email));
    }
}
```

---

## Unit of Work

```csharp
// IUnitOfWork interface (Domain Layer)
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDisposable> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    Task<IDisposable> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, string lockName = null, CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
}

// Implemented by AdsDbContext
```

### Sử dụng Unit of Work

```csharp
// Trong Command Handler
public async Task HandleAsync(AddUpdateProductCommand command, CancellationToken cancellationToken = default)
{
    // Begin transaction
    using (await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
    {
        // Multiple operations
        await _productService.AddOrUpdateAsync(command.Product, cancellationToken);
        await _auditService.LogAsync("Product updated", cancellationToken);

        // Commit all changes
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
    }
}

// Với Distributed Lock
public async Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
{
    // Begin transaction with lock (prevent double processing)
    using (await _unitOfWork.BeginTransactionAsync(
        IsolationLevel.ReadCommitted,
        lockName: $"payment:{command.PaymentId}",  // Lock key
        cancellationToken))
    {
        // Process payment
        await ProcessPaymentAsync(command.PaymentId, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);
    }
}
```

---

## Interceptors

### Select Without WHERE Interceptor (N+1 Detection)

```csharp
// SelectWithoutWhereCommandInterceptor.cs
public class SelectWithoutWhereCommandInterceptor : DbCommandInterceptor
{
    private static readonly string LOG_TEMPLATE = 
        "SELECT WITHOUT WHERE: " + Environment.NewLine + 
        " {Query} " + Environment.NewLine + 
        " {StackTrace}";

    private readonly ILogger _logger;

    public SelectWithoutWhereCommandInterceptor(ILogger logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, 
        CommandEventData eventData, 
        InterceptionResult<DbDataReader> result)
    {
        CheckCommand(command);
        return result;
    }

    private void CheckCommand(DbCommand command)
    {
        // Skip COUNT queries
        if (command.CommandText.Contains("SELECT COUNT(*)", StringComparison.OrdinalIgnoreCase))
            return;

        if (command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            // Skip if has WHERE, OFFSET, or FETCH (pagination)
            if (command.CommandText.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
                return;
            if (command.CommandText.Contains("OFFSET", StringComparison.OrdinalIgnoreCase))
                return;
            if (command.CommandText.Contains("FETCH", StringComparison.OrdinalIgnoreCase))
                return;

            // Log warning với stack trace
            var stackTrace = string.Join("\n", Environment.StackTrace.Split('\n')
                .Where(x => x.Contains("at ClassifiedAds."))
                .Select(x => x));

            _logger.LogWarning(LOG_TEMPLATE, command.CommandText, stackTrace);
        }
    }
}
```

### Select WHERE IN Interceptor (N+1 Detection)

```csharp
// SelectWhereInCommandInterceptor.cs
public class SelectWhereInCommandInterceptor : DbCommandInterceptor
{
    private static readonly string LOG_TEMPLATE = 
        "SELECT WHERE IN: " + Environment.NewLine + 
        " {Query} " + Environment.NewLine + 
        " {StackTrace}";

    private readonly ILogger _logger;

    private void CheckCommand(DbCommand command)
    {
        var query = command.CommandText;

        // Detect SELECT ... WHERE ... IN (...) pattern
        bool selectWhereIn = query.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
            && query.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
            && query.Contains(" IN (", StringComparison.OrdinalIgnoreCase);

        if (selectWhereIn)
        {
            var stackTrace = string.Join("\n", Environment.StackTrace.Split('\n')
                .Where(x => x.Contains("at ClassifiedAds."))
                .Select(x => x));

            _logger.LogWarning(LOG_TEMPLATE, command.CommandText, stackTrace);
        }
    }
}
```

---

## Distributed Locks

### PostgreSQL Advisory Locks

```csharp
// PostgresDistributedLock.cs
public class PostgresDistributedLock : IDistributedLock
{
    private readonly string _connectionString;
    private readonly NpgsqlTransaction _transaction;

    public PostgresDistributedLock(string connectionString)
    {
        _connectionString = connectionString;
    }

    public PostgresDistributedLock(NpgsqlTransaction transaction)
    {
        _transaction = transaction;
    }

    public IDisposable Acquire(string lockName)
    {
        // Convert string to hash for advisory lock
        var lockId = GetLockId(lockName);

        if (_transaction != null)
        {
            // Transaction-scoped lock (released on commit/rollback)
            using var cmd = _transaction.Connection.CreateCommand();
            cmd.Transaction = _transaction;
            cmd.CommandText = $"SELECT pg_advisory_xact_lock({lockId})";
            cmd.ExecuteNonQuery();
            return new NoOpDisposable();
        }
        else
        {
            // Session-scoped lock (must be released explicitly)
            var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_lock({lockId})";
            cmd.ExecuteNonQuery();

            return new PostgresLockScope(connection, lockId);
        }
    }

    private long GetLockId(string lockName)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(lockName));
        return BitConverter.ToInt64(hash, 0);
    }
}
```

### Lock Manager

```csharp
// LockManager.cs
public class LockManager : ILockManager
{
    private readonly AdsDbContext _dbContext;

    public async Task<Lock> AcquireLockAsync(
        string entityId, 
        string entityName, 
        TimeSpan? lockPeriod = null,
        CancellationToken cancellationToken = default)
    {
        var period = lockPeriod ?? TimeSpan.FromMinutes(5);
        var lockEntry = new Lock
        {
            EntityId = entityId,
            EntityName = entityName,
            ExpiredDateTime = DateTimeOffset.UtcNow.Add(period)
        };

        // Upsert lock (PostgreSQL)
        var sql = @"
            INSERT INTO ""Locks"" (""EntityId"", ""EntityName"", ""AcquiredDateTime"", ""ExpiredDateTime"")
            VALUES (@EntityId, @EntityName, @AcquiredDateTime, @ExpiredDateTime)
            ON CONFLICT (""EntityId"", ""EntityName"") 
            DO UPDATE SET ""AcquiredDateTime"" = @AcquiredDateTime, ""ExpiredDateTime"" = @ExpiredDateTime
            WHERE ""Locks"".""ExpiredDateTime"" < @Now";

        var result = await _dbContext.Database.ExecuteSqlRawAsync(sql, ...);

        return result > 0 ? lockEntry : null;
    }

    public async Task ReleaseLockAsync(
        string entityId, 
        string entityName,
        CancellationToken cancellationToken = default)
    {
        var sql = @"DELETE FROM ""Locks"" WHERE ""EntityId"" = @EntityId AND ""EntityName"" = @EntityName";
        await _dbContext.Database.ExecuteSqlRawAsync(sql, ...);
    }
}
```

---

## Multi-Tenancy

### Multi-Tenant DbContext

```csharp
// AdsDbContextMultiTenant.cs
public class AdsDbContextMultiTenant : AdsDbContext
{
    private readonly IConnectionStringResolver<AdsDbContextMultiTenant> _connectionStringResolver;

    public AdsDbContextMultiTenant(
        DbContextOptions<AdsDbContextMultiTenant> options,
        ILogger<AdsDbContext> logger,
        IConnectionStringResolver<AdsDbContextMultiTenant> connectionStringResolver)
        : base(options, logger)
    {
        _connectionStringResolver = connectionStringResolver;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        
        // Dynamic connection string based on tenant
        optionsBuilder.UseNpgsql(_connectionStringResolver.ConnectionString);
    }
}
```

### Connection String Resolver

```csharp
// AdsDbContextMultiTenantConnectionStringResolver.cs
public class AdsDbContextMultiTenantConnectionStringResolver 
    : IConnectionStringResolver<AdsDbContextMultiTenant>
{
    private readonly ITenantResolver _tenantResolver;
    private readonly IConfiguration _configuration;

    public string ConnectionString
    {
        get
        {
            var tenantId = _tenantResolver.TenantId;
            
            // Get tenant-specific connection string
            return _configuration.GetConnectionString($"Tenant_{tenantId}")
                ?? _configuration.GetConnectionString("Default");
        }
    }
}
```

---

## Migrations

### Tạo Migration

```bash
# Tạo migration mới
dotnet ef migrations add AddProductTable -p ClassifiedAds.Persistence -s ClassifiedAds.WebAPI

# Update database
dotnet ef database update -p ClassifiedAds.Persistence -s ClassifiedAds.WebAPI

# Generate SQL script
dotnet ef migrations script -p ClassifiedAds.Persistence -s ClassifiedAds.WebAPI
```

### Migration Best Practices

```csharp
// Migration với index
public partial class AddProductIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Products_Code",
            table: "Products",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Products_Code",
            table: "Products");
    }
}
```

---

## Quy tắc Implementation

### ✅ PHẢI LÀM

```csharp
// 1. Sử dụng Fluent API cho configuration
builder.Property(x => x.Name).HasMaxLength(200);  // ✅

// 2. Default value với gen_random_uuid() cho PostgreSQL
builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

// 3. Proper relationship configuration
builder.HasMany(x => x.Items)
    .WithOne(x => x.Order)
    .HasForeignKey(x => x.OrderId)
    .OnDelete(DeleteBehavior.Cascade);

// 4. Index cho frequently queried columns
builder.HasIndex(x => x.Email).IsUnique();

// 5. Audit fields tự động set
entity.CreatedDateTime = _dateTimeProvider.OffsetNow;
```

### ❌ KHÔNG ĐƯỢC LÀM

```csharp
// 1. KHÔNG expose DbContext ra ngoài Persistence layer
public class ProductService
{
    private readonly AdsDbContext _dbContext;  // ❌ Sai!
}

// 2. KHÔNG dùng Data Annotations trong Entity
[Table("Products")]           // ❌ Sai!
[Required]                    // ❌ Sai!
public class Product { }

// 3. KHÔNG query không qua Repository
var products = _dbContext.Products.ToList();  // ❌ Sai!

// 4. KHÔNG hardcode SQL Server syntax cho PostgreSQL
builder.Property(x => x.Id).HasDefaultValueSql("newsequentialid()");  // ❌ SQL Server only!

// 5. KHÔNG bỏ qua CancellationToken
public async Task<List<T>> ToListAsync<T>(IQueryable<T> query)  // ❌ Thiếu CancellationToken
```

---

## Checklist khi tạo Entity Configuration mới

- [ ] Configuration file đặt trong `DbConfigurations/`
- [ ] Table name được specify rõ ràng
- [ ] Primary key với `gen_random_uuid()` default
- [ ] Required properties được mark
- [ ] Max length được set cho string properties
- [ ] Relationships được configure đúng
- [ ] Indexes được tạo cho frequently queried columns
- [ ] Seed data được thêm nếu cần
- [ ] Migration được tạo và test
