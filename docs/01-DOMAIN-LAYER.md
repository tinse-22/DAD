# 🎯 Domain Layer

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [Cấu trúc thư mục](#cấu-trúc-thư-mục)
3. [Entities](#entities)
4. [Aggregate Root](#aggregate-root)
5. [Repository Interfaces](#repository-interfaces)
6. [Domain Events](#domain-events)
7. [Domain Services](#domain-services)
8. [Infrastructure Interfaces](#infrastructure-interfaces)
9. [Quy tắc Implementation](#quy-tắc-implementation)

---

## Tổng quan

**Domain Layer** là lõi trung tâm của ứng dụng, chứa:
- Business entities và rules
- Repository interfaces (không có implementation)
- Domain events
- Value objects
- Domain services

**Đặc điểm quan trọng:**
- ❌ **KHÔNG** phụ thuộc vào bất kỳ layer nào khác
- ❌ **KHÔNG** sử dụng framework-specific code (EF Core, ASP.NET, etc.)
- ✅ **CHỈ** chứa pure .NET code
- ✅ Có thể test 100% độc lập

---

## Cấu trúc thư mục

```
ClassifiedAds.Domain/
├── Constants/                    # Domain constants
├── Entities/                     # Business entities
│   ├── Entity.cs                # Base entity class
│   ├── IAggregateRoot.cs        # Aggregate root marker
│   ├── IHasKey.cs               # Key interface
│   ├── ITrackable.cs            # Auditing interface
│   ├── Product.cs               # Product entity
│   ├── User.cs                  # User entity
│   ├── FileEntry.cs             # File entity
│   └── ...
├── Events/                       # Domain events
│   ├── IDomainEvent.cs          # Event interface
│   ├── IDomainEventHandler.cs   # Handler interface
│   ├── EntityCreatedEvent.cs    # Generic created event
│   ├── EntityUpdatedEvent.cs    # Generic updated event
│   └── EntityDeletedEvent.cs    # Generic deleted event
├── Identity/                     # Identity interfaces
├── IdentityProviders/           # Identity provider interfaces
├── Infrastructure/              # Infrastructure interfaces
│   ├── Messaging/               # Message bus interfaces
│   └── Storages/                # File storage interfaces
├── Notification/                # Notification interfaces
├── Repositories/                # Repository interfaces
│   ├── IRepository.cs           # Generic repository
│   ├── IUnitOfWork.cs           # Unit of work
│   └── IConcurrencyHandler.cs   # Concurrency handling
├── Services/                    # Domain services
│   └── ProductService.cs
└── ValueObjects/                # Value objects
```

---

## Entities

### Base Entity Class

```csharp
// Entity.cs - Base class cho tất cả entities
public abstract class Entity<TKey> : IHasKey<TKey>, ITrackable
{
    public TKey Id { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; }          // Optimistic concurrency

    public DateTimeOffset CreatedDateTime { get; set; }   // Audit: created time

    public DateTimeOffset? UpdatedDateTime { get; set; }  // Audit: updated time
}
```

### Entity Interfaces

```csharp
// IHasKey.cs - Đảm bảo entity có primary key
public interface IHasKey<TKey>
{
    TKey Id { get; set; }
}

// ITrackable.cs - Đảm bảo entity có audit fields
public interface ITrackable
{
    DateTimeOffset CreatedDateTime { get; set; }
    DateTimeOffset? UpdatedDateTime { get; set; }
}
```

### Ví dụ Entity - Product

```csharp
// Product.cs
public class Product : Entity<Guid>, IAggregateRoot
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}
```

### Quy tắc tạo Entity

| Quy tắc | Mô tả |
|---------|-------|
| Kế thừa `Entity<TKey>` | Tất cả entity phải kế thừa từ base class |
| Implement `IAggregateRoot` | Nếu entity là aggregate root |
| Sử dụng `Guid` làm key | Preferred key type cho scalability |
| Không có logic phức tạp | Entity chỉ chứa properties, không có complex logic |
| Không reference Infrastructure | Không dùng EF Core attributes (ngoại trừ `[Timestamp]`) |

---

## Aggregate Root

**Aggregate Root** là entry point duy nhất để truy cập một nhóm entities liên quan.

```csharp
// IAggregateRoot.cs - Marker interface
public interface IAggregateRoot
{
}
```

### Quy tắc Aggregate Root

1. **Chỉ Aggregate Root mới có Repository**
   ```csharp
   // ✅ Đúng - Product là Aggregate Root
   IRepository<Product, Guid> _productRepository;
   
   // ❌ Sai - ProductEmbedding không phải Aggregate Root
   // (Tuy nhiên trong project này, ProductEmbedding được thiết kế như Aggregate Root riêng)
   ```

2. **Aggregate Root đảm bảo tính nhất quán**
   - Tất cả thay đổi đối với entities con phải thông qua Aggregate Root

3. **Aggregate Root phát ra Domain Events**
   - Khi state thay đổi, Aggregate Root phát ra event

---

## Repository Interfaces

### Generic Repository Interface

```csharp
// IRepository.cs
public interface IRepository<TEntity, TKey> : IConcurrencyHandler<TEntity>
    where TEntity : Entity<TKey>, IAggregateRoot
{
    // Unit of Work reference
    IUnitOfWork UnitOfWork { get; }

    // Query
    IQueryable<TEntity> GetQueryableSet();

    // Commands
    Task AddOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Delete(TEntity entity);

    // Async query execution
    Task<T> FirstOrDefaultAsync<T>(IQueryable<T> query);
    Task<T> SingleOrDefaultAsync<T>(IQueryable<T> query);
    Task<List<T>> ToListAsync<T>(IQueryable<T> query);

    // Bulk operations
    Task BulkInsertAsync(IReadOnlyCollection<TEntity> entities, CancellationToken cancellationToken = default);
    Task BulkUpdateAsync(IReadOnlyCollection<TEntity> entities, Expression<Func<TEntity, object>> columnNamesSelector, CancellationToken cancellationToken = default);
    Task BulkDeleteAsync(IReadOnlyCollection<TEntity> entities, CancellationToken cancellationToken = default);
    Task BulkMergeAsync(IReadOnlyCollection<TEntity> entities, Expression<Func<TEntity, object>> idSelector, Expression<Func<TEntity, object>> updateColumnNamesSelector, Expression<Func<TEntity, object>> insertColumnNamesSelector, CancellationToken cancellationToken = default);
}
```

### Unit of Work Interface

```csharp
// IUnitOfWork.cs
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDisposable> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, 
        CancellationToken cancellationToken = default);

    Task<IDisposable> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, 
        string lockName = null,                                        // Distributed lock
        CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
}
```

### Specialized Repository Interfaces

```csharp
// IUserRepository.cs - Extended repository cho User
public interface IUserRepository : IRepository<User, Guid>
{
    Task<User> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}
```

---

## Domain Events

### Event Interfaces

```csharp
// IDomainEvent.cs
public interface IDomainEvent
{
}

// IDomainEventHandler.cs
public interface IDomainEventHandler<T>
    where T : IDomainEvent
{
    Task HandleAsync(T domainEvent, CancellationToken cancellationToken = default);
}
```

### Built-in Events

```csharp
// EntityCreatedEvent.cs
public class EntityCreatedEvent<T> : IDomainEvent
{
    public T Entity { get; }
    public DateTime EventDateTime { get; }

    public EntityCreatedEvent(T entity, DateTime eventDateTime)
    {
        Entity = entity;
        EventDateTime = eventDateTime;
    }
}

// EntityUpdatedEvent.cs
public class EntityUpdatedEvent<T> : IDomainEvent
{
    public T Entity { get; }
    public DateTime EventDateTime { get; }
}

// EntityDeletedEvent.cs
public class EntityDeletedEvent<T> : IDomainEvent
{
    public T Entity { get; }
    public DateTime EventDateTime { get; }
}
```

---

## Domain Services

Domain Service chứa business logic mà không thuộc về một entity cụ thể.

```csharp
// ProductService.cs trong Domain Layer
public class ProductService
{
    // Domain-specific business logic
    // Ví dụ: ValidateProductCode, CalculateDiscount, etc.
}
```

**Lưu ý:** Trong project này, phần lớn business logic nằm trong Application Layer thông qua CQRS pattern.

---

## Infrastructure Interfaces

### Message Bus Interface

```csharp
// IMessageBus.cs
public interface IMessageBus
{
    Task SendAsync<T>(T message, MetaData metaData = null, CancellationToken cancellationToken = default)
        where T : IMessageBusMessage;

    Task ReceiveAsync<TConsumer, T>(Func<T, MetaData, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        where T : IMessageBusMessage;

    Task SendAsync(PublishingOutboxMessage outbox, CancellationToken cancellationToken = default);
}

// Message Types
public interface IMessageBusMessage { }
public interface IMessageBusEvent : IMessageBusMessage { }
public interface IMessageBusCommand : IMessageBusMessage { }
```

### File Storage Interface

```csharp
// IFileStorageManager.cs
public interface IFileStorageManager
{
    Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task DeleteAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task ArchiveAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task UnArchiveAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
}
```

---

## Quy tắc Implementation

### ✅ PHẢI LÀM

```csharp
// 1. Mọi entity phải kế thừa Entity<TKey>
public class Order : Entity<Guid>, IAggregateRoot { }

// 2. Sử dụng interfaces cho external dependencies
public interface IOrderRepository : IRepository<Order, Guid> { }

// 3. Domain Events cho state changes quan trọng
public class OrderPlacedEvent : IDomainEvent { }

// 4. Properties phải có proper encapsulation
public class Product : Entity<Guid>, IAggregateRoot
{
    public string Code { get; set; }
    public string Name { get; set; }
}
```

### ❌ KHÔNG ĐƯỢC LÀM

```csharp
// 1. KHÔNG reference Infrastructure packages
using Microsoft.EntityFrameworkCore;  // ❌ Sai!

// 2. KHÔNG có implementation của repository trong Domain
public class ProductRepository : IRepository<Product, Guid>  // ❌ Sai!
{
    // Implementation belongs in Persistence layer
}

// 3. KHÔNG call external services trực tiếp
public class ProductService
{
    public async Task SendEmail()  // ❌ Sai!
    {
        await _emailService.Send();  // Không call infrastructure trực tiếp
    }
}

// 4. KHÔNG sử dụng EF Core specific attributes
[Table("Products")]           // ❌ Sai! Dùng Fluent API thay thế
[Column("product_name")]      // ❌ Sai!
public class Product { }
```

---

## Checklist khi tạo Entity mới

- [ ] Entity kế thừa từ `Entity<Guid>` (hoặc key type phù hợp)
- [ ] Entity implement `IAggregateRoot` nếu là aggregate root
- [ ] Không có reference đến EF Core hoặc framework khác
- [ ] Properties đặt tên theo PascalCase
- [ ] Có tạo Domain Events nếu cần track changes
- [ ] Repository interface được tạo trong `Repositories/` folder
- [ ] Unit tests đã được viết cho business rules
