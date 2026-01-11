# 📋 Application Layer

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [Cấu trúc thư mục](#cấu-trúc-thư-mục)
3. [CQRS Pattern](#cqrs-pattern)
4. [Commands](#commands)
5. [Queries](#queries)
6. [Dispatcher](#dispatcher)
7. [Decorators](#decorators)
8. [Application Services](#application-services)
9. [Quy tắc Implementation](#quy-tắc-implementation)

---

## Tổng quan

**Application Layer** chứa:
- Business logic / Use cases
- CQRS implementation (Commands & Queries)
- Application services
- DTOs (Data Transfer Objects)
- Event handlers

**Đặc điểm:**
- ✅ Phụ thuộc vào Domain Layer
- ❌ **KHÔNG** phụ thuộc vào Infrastructure
- ❌ **KHÔNG** biết về database cụ thể
- ✅ Orchestrate business workflows

---

## Cấu trúc thư mục

```
ClassifiedAds.Application/
├── Common/                           # Shared components
│   ├── Commands/                     # Command interfaces
│   │   ├── ICommand.cs
│   │   └── ICommandHandler.cs
│   ├── Queries/                      # Query interfaces
│   │   ├── IQuery.cs
│   │   └── IQueryHandler.cs
│   ├── Services/                     # Application services
│   │   ├── ICrudService.cs
│   │   └── CrudService.cs
│   ├── DTOs/                        # Shared DTOs
│   ├── Dispatcher.cs                # Command/Query dispatcher
│   ├── HandlerFactory.cs            # Handler factory với decorators
│   └── Utils.cs
├── Decorators/                       # Cross-cutting decorators
│   ├── AuditLog/                    # Audit logging
│   ├── DatabaseRetry/               # Retry logic
│   └── Mappings.cs                  # Decorator mappings
├── Products/                         # Product feature
│   ├── Commands/
│   │   ├── AddUpdateProductCommand.cs
│   │   └── DeleteProductCommand.cs
│   ├── Queries/
│   │   ├── GetProductQuery.cs
│   │   └── GetProductsQuery.cs
│   ├── DTOs/
│   ├── EventHandlers/
│   ├── MessageBusEvents/
│   └── Services/
├── Users/                            # User feature
│   ├── Commands/
│   ├── Queries/
│   ├── DTOs/
│   └── Services/
├── FileEntries/                      # File feature
├── EmailMessages/                    # Email feature
├── SmsMessages/                      # SMS feature
├── AuditLogEntries/                  # Audit log feature
├── ConfigurationEntries/             # Configuration feature
├── OutboxMessages/                   # Outbox pattern
├── FeatureToggles/                   # Feature flags
└── ApplicationServicesExtensions.cs  # DI registration
```

---

## CQRS Pattern

**CQRS (Command Query Responsibility Segregation)** tách biệt:
- **Commands**: Thay đổi state, không trả về data
- **Queries**: Đọc data, không thay đổi state

```
┌─────────────────────────────────────────────────────────────────┐
│                        Controller                               │
└───────────────────────────────┬─────────────────────────────────┘
                                │
                    ┌───────────▼───────────┐
                    │      Dispatcher       │
                    └───────────┬───────────┘
                                │
            ┌───────────────────┴───────────────────┐
            │                                       │
    ┌───────▼───────┐                     ┌────────▼────────┐
    │   Commands    │                     │     Queries     │
    │  (Write ops)  │                     │   (Read ops)    │
    └───────┬───────┘                     └────────┬────────┘
            │                                       │
    ┌───────▼───────┐                     ┌────────▼────────┐
    │ CommandHandler│                     │  QueryHandler   │
    └───────┬───────┘                     └────────┬────────┘
            │                                       │
    ┌───────▼────────────────────────────────────▼────────┐
    │                     Repository                       │
    └──────────────────────────────────────────────────────┘
```

---

## Commands

### Command Interface

```csharp
// ICommand.cs
public interface ICommand
{
}

// ICommandHandler.cs  
public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
```

### Ví dụ: AddUpdateProductCommand

```csharp
// AddUpdateProductCommand.cs

// Command - chứa data cần thiết
public class AddUpdateProductCommand : ICommand
{
    public Product Product { get; set; }
}

// Handler - chứa logic xử lý
internal class AddUpdateProductCommandHandler : ICommandHandler<AddUpdateProductCommand>
{
    private readonly ICrudService<Product> _productService;
    private readonly IUnitOfWork _unitOfWork;

    public AddUpdateProductCommandHandler(
        ICrudService<Product> productService, 
        IUnitOfWork unitOfWork)
    {
        _productService = productService;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(AddUpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Begin transaction với proper isolation
        using (await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            // 2. Execute business logic
            await _productService.AddOrUpdateAsync(command.Product, cancellationToken);

            // 3. Commit transaction
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
    }
}
```

### Ví dụ: DeleteProductCommand

```csharp
// DeleteProductCommand.cs
public class DeleteProductCommand : ICommand
{
    public Product Product { get; set; }
}

internal class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly ICrudService<Product> _productService;

    public DeleteProductCommandHandler(ICrudService<Product> productService)
    {
        _productService = productService;
    }

    public async Task HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        await _productService.DeleteAsync(command.Product, cancellationToken);
    }
}
```

---

## Queries

### Query Interface

```csharp
// IQuery.cs
public interface IQuery<TResult>
{
}

// IQueryHandler.cs
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
```

### Ví dụ: GetProductsQuery

```csharp
// GetProductsQuery.cs

// Query - có thể có filter parameters
public class GetProductsQuery : IQuery<List<Product>>
{
}

// Handler với decorators
[AuditLog]           // Decorator: Log query execution
[DatabaseRetry]      // Decorator: Retry on transient failures
internal class GetProductsQueryHandler : IQueryHandler<GetProductsQuery, List<Product>>
{
    private readonly IRepository<Product, Guid> _productRepository;

    public GetProductsQueryHandler(IRepository<Product, Guid> productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<List<Product>> HandleAsync(GetProductsQuery query, CancellationToken cancellationToken = default)
    {
        return await _productRepository.ToListAsync(_productRepository.GetQueryableSet());
    }
}
```

### Ví dụ: GetProductQuery (Single item)

```csharp
// GetProductQuery.cs
public class GetProductQuery : IQuery<Product>
{
    public Guid Id { get; set; }
    public bool ThrowNotFoundIfNull { get; set; }  // Optional: throw exception if not found
}

internal class GetProductQueryHandler : IQueryHandler<GetProductQuery, Product>
{
    private readonly IRepository<Product, Guid> _productRepository;

    public GetProductQueryHandler(IRepository<Product, Guid> productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Product> HandleAsync(GetProductQuery query, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.FirstOrDefaultAsync(
            _productRepository.GetQueryableSet().Where(x => x.Id == query.Id));

        if (product == null && query.ThrowNotFoundIfNull)
        {
            throw new NotFoundException($"Product with id {query.Id} not found.");
        }

        return product;
    }
}
```

---

## Dispatcher

**Dispatcher** là mediator pattern để dispatch commands/queries đến handlers tương ứng.

```csharp
// Dispatcher.cs
public class Dispatcher
{
    private readonly IServiceProvider _provider;

    public Dispatcher(IServiceProvider provider)
    {
        _provider = provider;
    }

    // Dispatch Command
    public async Task DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        Type type = typeof(ICommandHandler<>);
        Type[] typeArgs = { command.GetType() };
        Type handlerType = type.MakeGenericType(typeArgs);

        dynamic handler = _provider.GetService(handlerType);
        await handler.HandleAsync((dynamic)command, cancellationToken);
    }

    // Dispatch Query
    public async Task<T> DispatchAsync<T>(IQuery<T> query, CancellationToken cancellationToken = default)
    {
        Type type = typeof(IQueryHandler<,>);
        Type[] typeArgs = { query.GetType(), typeof(T) };
        Type handlerType = type.MakeGenericType(typeArgs);

        dynamic handler = _provider.GetService(handlerType);
        Task<T> result = handler.HandleAsync((dynamic)query, cancellationToken);

        return await result;
    }

    // Dispatch Domain Event
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (Type handlerType in _eventHandlers)
        {
            bool canHandleEvent = handlerType.GetInterfaces()
                .Any(x => x.IsGenericType
                    && x.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)
                    && x.GenericTypeArguments[0] == domainEvent.GetType());

            if (canHandleEvent)
            {
                dynamic handler = _provider.GetService(handlerType);
                await handler.HandleAsync((dynamic)domainEvent, cancellationToken);
            }
        }
    }
}
```

### Sử dụng Dispatcher

```csharp
// Trong Controller
public class ProductsController : ControllerBase
{
    private readonly Dispatcher _dispatcher;

    public ProductsController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // Query
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> Get()
    {
        var products = await _dispatcher.DispatchAsync(new GetProductsQuery());
        return Ok(products);
    }

    // Command
    [HttpPost]
    public async Task<ActionResult<Product>> Post([FromBody] ProductModel model)
    {
        var product = model.ToEntity();
        await _dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });
        return Created($"/api/products/{product.Id}", product);
    }
}
```

---

## Decorators

Decorators implement **cross-cutting concerns** mà không thay đổi handler logic.

### AuditLog Decorator

```csharp
// AuditLogAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class AuditLogAttribute : Attribute
{
}

// AuditLogQueryDecorator.cs
[Mapping(Type = typeof(AuditLogAttribute))]
public class AuditLogQueryDecorator<TQuery, TResult> : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _handler;

    public AuditLogQueryDecorator(IQueryHandler<TQuery, TResult> handler)
    {
        _handler = handler;
    }

    public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        // Log before execution
        string queryJson = JsonSerializer.Serialize(query);
        Console.WriteLine($"Query of type {query.GetType().Name}: {queryJson}");
        
        // Execute handler
        return _handler.HandleAsync(query, cancellationToken);
    }
}
```

### DatabaseRetry Decorator

```csharp
// DatabaseRetryAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class DatabaseRetryAttribute : Attribute
{
    public int RetryCount { get; set; } = 3;
    public int SleepDuration { get; set; } = 500; // ms
}

// DatabaseRetryQueryDecorator.cs
[Mapping(Type = typeof(DatabaseRetryAttribute))]
public class DatabaseRetryQueryDecorator<TQuery, TResult> : DatabaseRetryDecoratorBase, IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _handler;

    public DatabaseRetryQueryDecorator(IQueryHandler<TQuery, TResult> handler, DatabaseRetryAttribute options)
    {
        DatabaseRetryOptions = options;
        _handler = handler;
    }

    public async Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        Task<TResult> result = default;
        await WrapExecutionAsync(() => result = _handler.HandleAsync(query, cancellationToken));
        return await result;
    }
}
```

### Decorator Pipeline

```
Request → [AuditLog] → [DatabaseRetry] → Handler → Response
```

---

## Application Services

### Generic CRUD Service

```csharp
// ICrudService.cs
public interface ICrudService<T>
    where T : Entity<Guid>, IAggregateRoot
{
    Task<List<T>> GetAsync(CancellationToken cancellationToken = default);
    Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
}

// CrudService.cs
public class CrudService<T> : ICrudService<T>
    where T : Entity<Guid>, IAggregateRoot
{
    private readonly IUnitOfWork _unitOfWork;
    protected readonly IRepository<T, Guid> _repository;
    protected readonly Dispatcher _dispatcher;

    public CrudService(IRepository<T, Guid> repository, Dispatcher dispatcher)
    {
        _unitOfWork = repository.UnitOfWork;
        _repository = repository;
        _dispatcher = dispatcher;
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        // 1. Add entity
        await _repository.AddAsync(entity, cancellationToken);
        
        // 2. Save changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        // 3. Dispatch domain event
        await _dispatcher.DispatchAsync(new EntityCreatedEvent<T>(entity, DateTime.UtcNow), cancellationToken);
    }

    public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _dispatcher.DispatchAsync(new EntityUpdatedEvent<T>(entity, DateTime.UtcNow), cancellationToken);
    }

    public async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        _repository.Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _dispatcher.DispatchAsync(new EntityDeletedEvent<T>(entity, DateTime.UtcNow), cancellationToken);
    }
}
```

---

## Quy tắc Implementation

### ✅ PHẢI LÀM

```csharp
// 1. Mỗi Command/Query trong file riêng biệt
// Products/Commands/AddUpdateProductCommand.cs
// Products/Commands/DeleteProductCommand.cs

// 2. Handler đặt internal (không public)
internal class AddUpdateProductCommandHandler : ICommandHandler<AddUpdateProductCommand>

// 3. Inject dependencies qua constructor
public AddUpdateProductCommandHandler(
    ICrudService<Product> productService,
    IUnitOfWork unitOfWork)

// 4. Sử dụng CancellationToken
public async Task HandleAsync(TCommand command, CancellationToken cancellationToken = default)

// 5. Validation trong handler
public async Task HandleAsync(GetProductQuery query, CancellationToken cancellationToken = default)
{
    ValidationException.Requires(query.Id != Guid.Empty, "Invalid Id");
    // ...
}

// 6. Sử dụng Transaction cho write operations
using (await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
{
    // operations
    await _unitOfWork.CommitTransactionAsync(cancellationToken);
}
```

### ❌ KHÔNG ĐƯỢC LÀM

```csharp
// 1. KHÔNG reference DbContext trực tiếp
private readonly AdsDbContext _dbContext;  // ❌ Sai!

// 2. KHÔNG sử dụng EF Core trong Application Layer
using Microsoft.EntityFrameworkCore;  // ❌ Sai!

// 3. KHÔNG có business logic trong Controller
[HttpPost]
public async Task<ActionResult> Post(ProductModel model)
{
    // ❌ Sai! Logic phải ở trong Handler
    if (model.Price < 0) { }
    _dbContext.Products.Add(model);
}

// 4. KHÔNG trả về Entity từ Query nếu có thể
// Thay vào đó, sử dụng DTOs hoặc projection
public class GetProductQuery : IQuery<Product>  // ⚠️ Cân nhắc dùng ProductDTO

// 5. KHÔNG inject quá nhiều dependencies
public SomeHandler(
    IRepo1 r1, IRepo2 r2, IRepo3 r3, IRepo4 r4, IRepo5 r5)  // ❌ Quá nhiều!
```

---

## Checklist khi tạo Command/Query mới

### Command Checklist
- [ ] File đặt trong `Features/{FeatureName}/Commands/`
- [ ] Command class implement `ICommand`
- [ ] Handler class implement `ICommandHandler<TCommand>`
- [ ] Handler đánh dấu `internal`
- [ ] Transaction được sử dụng cho multi-step operations
- [ ] Validation được thực hiện ở đầu handler
- [ ] Domain events được dispatch nếu cần
- [ ] CancellationToken được pass through

### Query Checklist
- [ ] File đặt trong `Features/{FeatureName}/Queries/`
- [ ] Query class implement `IQuery<TResult>`
- [ ] Handler class implement `IQueryHandler<TQuery, TResult>`
- [ ] Handler đánh dấu `internal`
- [ ] Decorators được thêm nếu cần (`[AuditLog]`, `[DatabaseRetry]`)
- [ ] Projection/Select được sử dụng để tránh over-fetching
- [ ] Paging được implement cho list queries
- [ ] CancellationToken được pass through
