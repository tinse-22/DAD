# 🤖 AI Agent Rules - Strict Implementation Guidelines

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [ABSOLUTE RULES - Không được vi phạm](#absolute-rules---không-được-vi-phạm)
3. [Layer Dependencies](#layer-dependencies)
4. [Entity Creation Rules](#entity-creation-rules)
5. [Command/Query Rules](#commandquery-rules)
6. [Repository Rules](#repository-rules)
7. [Controller Rules](#controller-rules)
8. [Configuration Rules](#configuration-rules)
9. [Naming Conventions](#naming-conventions)
10. [Code Structure Templates](#code-structure-templates)

---

## Tổng quan

Tài liệu này định nghĩa **STRICT RULES** mà AI Agents **PHẢI TUÂN THỦ** khi implement code trong codebase này. Vi phạm bất kỳ rule nào sẽ dẫn đến code không được chấp nhận.

---

## ABSOLUTE RULES - Không được vi phạm

### 🚫 RULE 1: KHÔNG VI PHẠM DEPENDENCY RULE

```
Domain ← Application ← Infrastructure/Persistence ← WebAPI/Background
```

| Layer | CÓ THỂ Reference | KHÔNG ĐƯỢC Reference |
|-------|------------------|---------------------|
| Domain | Không reference layer nào | Application, Infrastructure, Persistence, WebAPI |
| Application | Domain | Infrastructure, Persistence, WebAPI |
| Infrastructure | Domain, Application, CrossCuttingConcerns | WebAPI, Background |
| Persistence | Domain, Application, CrossCuttingConcerns | WebAPI, Background, Infrastructure |
| WebAPI | Tất cả | - |

### 🚫 RULE 2: KHÔNG SỬ DỤNG EF CORE TRONG DOMAIN LAYER

```csharp
// ❌ TUYỆT ĐỐI CẤM trong ClassifiedAds.Domain
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Table("Products")]  // ❌ CẤM
[Column("name")]     // ❌ CẤM
public class Product { }
```

### 🚫 RULE 3: KHÔNG EXPOSE DbContext RA NGOÀI PERSISTENCE LAYER

```csharp
// ❌ CẤM - trong Application, WebAPI, hoặc bất kỳ layer nào khác
private readonly AdsDbContext _dbContext;

// ✅ ĐÚNG - Sử dụng Repository
private readonly IRepository<Product, Guid> _productRepository;
```

### 🚫 RULE 4: KHÔNG BUSINESS LOGIC TRONG CONTROLLER

```csharp
// ❌ CẤM
[HttpPost]
public async Task<ActionResult> Create(ProductModel model)
{
    if (await _context.Products.AnyAsync(x => x.Code == model.Code))  // ❌ Business logic
    {
        return BadRequest("Code exists");
    }
    _context.Products.Add(entity);  // ❌ Direct DB access
}

// ✅ ĐÚNG
[HttpPost]
public async Task<ActionResult> Create(ProductModel model)
{
    var product = model.ToEntity();
    await _dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });
    return Created(...);
}
```

### 🚫 RULE 5: MỌI ENTITY PHẢI KẾ THỪA Entity<TKey>

```csharp
// ❌ CẤM
public class Product
{
    public Guid Id { get; set; }
}

// ✅ ĐÚNG
public class Product : Entity<Guid>, IAggregateRoot
{
    public string Name { get; set; }
}
```

### 🚫 RULE 6: CHỈ AGGREGATE ROOT MỚI CÓ REPOSITORY

```csharp
// ✅ Product là Aggregate Root → có Repository
public class Product : Entity<Guid>, IAggregateRoot { }
IRepository<Product, Guid> _productRepository;

// ⚠️ ProductEmbedding cũng là Aggregate Root riêng trong codebase này
public class ProductEmbedding : Entity<Guid>, IAggregateRoot { }
```

### 🚫 RULE 7: LUÔN SỬ DỤNG CANCELLATIONTOKEN

```csharp
// ❌ CẤM
public async Task<List<Product>> GetProductsAsync()

// ✅ ĐÚNG
public async Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
```

### 🚫 RULE 8: SỬ DỤNG gen_random_uuid() CHO POSTGRESQL

```csharp
// ❌ CẤM - SQL Server syntax
builder.Property(x => x.Id).HasDefaultValueSql("newsequentialid()");
builder.Property(x => x.Id).HasDefaultValueSql("NEWID()");

// ✅ ĐÚNG - PostgreSQL
builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
```

---

## Layer Dependencies

### Domain Layer (.csproj)

```xml
<!-- ClassifiedAds.Domain.csproj -->
<ItemGroup>
    <!-- KHÔNG có reference đến project khác -->
    <!-- CHỈ có basic .NET packages -->
</ItemGroup>
```

### Application Layer (.csproj)

```xml
<!-- ClassifiedAds.Application.csproj -->
<ItemGroup>
    <ProjectReference Include="..\ClassifiedAds.Domain\ClassifiedAds.Domain.csproj" />
    <ProjectReference Include="..\ClassifiedAds.CrossCuttingConcerns\ClassifiedAds.CrossCuttingConcerns.csproj" />
    <!-- KHÔNG reference Infrastructure, Persistence, WebAPI -->
</ItemGroup>
```

### Persistence Layer (.csproj)

```xml
<!-- ClassifiedAds.Persistence.csproj -->
<ItemGroup>
    <ProjectReference Include="..\ClassifiedAds.Domain\ClassifiedAds.Domain.csproj" />
    <ProjectReference Include="..\ClassifiedAds.Application\ClassifiedAds.Application.csproj" />
    <ProjectReference Include="..\ClassifiedAds.CrossCuttingConcerns\ClassifiedAds.CrossCuttingConcerns.csproj" />
    <!-- KHÔNG reference WebAPI, Background -->
</ItemGroup>
```

---

## Entity Creation Rules

### Template cho Entity mới

```csharp
// File: ClassifiedAds.Domain/Entities/{EntityName}.cs
using System;

namespace ClassifiedAds.Domain.Entities;

public class {EntityName} : Entity<Guid>, IAggregateRoot
{
    // Properties
    public string PropertyName { get; set; }
    
    // Navigation properties (nếu cần)
    // public List<ChildEntity> Children { get; set; }
}
```

### Checklist Entity

- [ ] Đặt trong `ClassifiedAds.Domain/Entities/`
- [ ] Kế thừa `Entity<Guid>` (hoặc key type phù hợp)
- [ ] Implement `IAggregateRoot` nếu là aggregate root
- [ ] KHÔNG có Data Annotations từ EF Core
- [ ] KHÔNG có business logic phức tạp
- [ ] Properties dùng PascalCase

---

## Command/Query Rules

### Template cho Command

```csharp
// File: ClassifiedAds.Application/{Feature}/Commands/{CommandName}Command.cs
using ClassifiedAds.Domain.Entities;
using ClassifiedAds.Domain.Repositories;
using System.Threading;
using System.Threading.Tasks;

namespace ClassifiedAds.Application.{Feature}.Commands;

// Command class
public class {CommandName}Command : ICommand
{
    public {EntityType} {EntityName} { get; set; }
    // Hoặc các properties cần thiết
}

// Handler class - PHẢI internal
internal class {CommandName}CommandHandler : ICommandHandler<{CommandName}Command>
{
    private readonly ICrudService<{EntityType}> _service;
    private readonly IUnitOfWork _unitOfWork;

    public {CommandName}CommandHandler(
        ICrudService<{EntityType}> service,
        IUnitOfWork unitOfWork)
    {
        _service = service;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync({CommandName}Command command, CancellationToken cancellationToken = default)
    {
        using (await _unitOfWork.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            // Business logic here
            
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
    }
}
```

### Template cho Query

```csharp
// File: ClassifiedAds.Application/{Feature}/Queries/{QueryName}Query.cs
using ClassifiedAds.Application.Decorators.AuditLog;
using ClassifiedAds.Application.Decorators.DatabaseRetry;
using ClassifiedAds.Domain.Entities;
using ClassifiedAds.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClassifiedAds.Application.{Feature}.Queries;

// Query class
public class {QueryName}Query : IQuery<{ReturnType}>
{
    public Guid Id { get; set; }  // Parameters nếu cần
    public bool ThrowNotFoundIfNull { get; set; }
}

// Handler class - PHẢI internal, với Decorators
[AuditLog]
[DatabaseRetry]
internal class {QueryName}QueryHandler : IQueryHandler<{QueryName}Query, {ReturnType}>
{
    private readonly IRepository<{EntityType}, Guid> _repository;

    public {QueryName}QueryHandler(IRepository<{EntityType}, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<{ReturnType}> HandleAsync({QueryName}Query query, CancellationToken cancellationToken = default)
    {
        // Query logic with projection
        var result = await _repository.FirstOrDefaultAsync(
            _repository.GetQueryableSet()
                .Where(x => x.Id == query.Id)
                .Select(x => new { ... }));  // ✅ Always use projection
        
        return result;
    }
}
```

### Checklist Command/Query

- [ ] Đặt trong đúng folder (`Commands/` hoặc `Queries/`)
- [ ] Handler class đánh dấu `internal`
- [ ] CancellationToken được pass through
- [ ] Query có `[AuditLog]` và `[DatabaseRetry]` decorators
- [ ] Command sử dụng Transaction
- [ ] Validation ở đầu handler

---

## Repository Rules

### KHÔNG BAO GIỜ

```csharp
// ❌ Query không có filter
await _repository.ToListAsync(_repository.GetQueryableSet());  // ⚠️ Chỉ dùng khi cần thiết

// ❌ N+1 Query - Load related data trong loop
foreach (var product in products)
{
    var embeddings = await _embeddingRepository
        .GetQueryableSet()
        .Where(x => x.ProductId == product.Id)
        .ToListAsync();  // ❌ N+1!
}
```

### LUÔN LUÔN

```csharp
// ✅ Sử dụng projection
var products = await _repository.ToListAsync(
    _repository.GetQueryableSet()
        .Where(x => x.IsActive)
        .Select(x => new ProductDto
        {
            Id = x.Id,
            Name = x.Name
        }));

// ✅ Eager loading với Include (nếu cần)
var product = await _repository.FirstOrDefaultAsync(
    _repository.GetQueryableSet()
        .Include(x => x.Category)
        .Where(x => x.Id == id));

// ✅ Batch load thay vì N+1
var productIds = products.Select(x => x.Id).ToList();
var embeddings = await _embeddingRepository
    .GetQueryableSet()
    .Where(x => productIds.Contains(x.ProductId))
    .ToListAsync();  // ✅ Single query
```

---

## Controller Rules

### Template cho Controller

```csharp
// File: ClassifiedAds.WebAPI/Controllers/{Feature}Controller.cs
using ClassifiedAds.Application;
using ClassifiedAds.Application.{Feature}.Commands;
using ClassifiedAds.Application.{Feature}.Queries;
using ClassifiedAds.WebAPI.Authorization;
using ClassifiedAds.WebAPI.Models.{Feature};
using ClassifiedAds.WebAPI.RateLimiterPolicies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClassifiedAds.WebAPI.Controllers;

[EnableRateLimiting(RateLimiterPolicyNames.DefaultPolicy)]
[Authorize]
[Produces("application/json")]
[Route("api/[controller]")]
[ApiController]
public class {Feature}Controller : ControllerBase
{
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<{Feature}Controller> _logger;

    public {Feature}Controller(
        Dispatcher dispatcher,
        ILogger<{Feature}Controller> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    [Authorize(Permissions.Get{Feature}s)]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<{Feature}Model>>> Get()
    {
        _logger.LogInformation("Getting all {Feature}s");
        var items = await _dispatcher.DispatchAsync(new Get{Feature}sQuery());
        return Ok(items.ToModels());
    }

    [Authorize(Permissions.Get{Feature})]
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<{Feature}Model>> Get(Guid id)
    {
        var item = await _dispatcher.DispatchAsync(new Get{Feature}Query 
        { 
            Id = id, 
            ThrowNotFoundIfNull = true 
        });
        return Ok(item.ToModel());
    }

    [Authorize(Permissions.Add{Feature})]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<{Feature}Model>> Post([FromBody] {Feature}Model model)
    {
        var entity = model.ToEntity();
        await _dispatcher.DispatchAsync(new AddUpdate{Feature}Command { {Feature} = entity });
        return Created($"/api/{feature}s/{entity.Id}", entity.ToModel());
    }

    [Authorize(Permissions.Update{Feature})]
    [HttpPut("{id}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<{Feature}Model>> Put(Guid id, [FromBody] {Feature}Model model)
    {
        var entity = await _dispatcher.DispatchAsync(new Get{Feature}Query 
        { 
            Id = id, 
            ThrowNotFoundIfNull = true 
        });
        
        // Update properties
        entity.Property = model.Property;
        
        await _dispatcher.DispatchAsync(new AddUpdate{Feature}Command { {Feature} = entity });
        return Ok(entity.ToModel());
    }

    [Authorize(Permissions.Delete{Feature})]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var entity = await _dispatcher.DispatchAsync(new Get{Feature}Query 
        { 
            Id = id, 
            ThrowNotFoundIfNull = true 
        });
        
        await _dispatcher.DispatchAsync(new Delete{Feature}Command { {Feature} = entity });
        return NoContent();
    }
}
```

---

## Configuration Rules

### Entity Configuration Template

```csharp
// File: ClassifiedAds.Persistence/DbConfigurations/{EntityName}Configuration.cs
using ClassifiedAds.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassifiedAds.Persistence.DbConfigurations;

public class {EntityName}Configuration : IEntityTypeConfiguration<{EntityName}>
{
    public void Configure(EntityTypeBuilder<{EntityName}> builder)
    {
        // Table name (REQUIRED)
        builder.ToTable("{EntityName}s");

        // Primary key (REQUIRED for PostgreSQL)
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        // Required properties
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Optional properties
        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        // Indexes (nếu cần)
        builder.HasIndex(x => x.Code)
            .IsUnique();

        // Relationships (nếu có)
        builder.HasMany(x => x.Children)
            .WithOne(x => x.Parent)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed data (nếu cần)
        builder.HasData(new List<{EntityName}>
        {
            new {EntityName}
            {
                Id = Guid.Parse("..."),
                Name = "..."
            }
        });
    }
}
```

---

## Naming Conventions

### Files & Classes

| Type | Naming Pattern | Example |
|------|----------------|---------|
| Entity | `{Name}` | `Product.cs` |
| Repository Interface | `I{Name}Repository` | `IProductRepository.cs` |
| Command | `{Verb}{Name}Command` | `AddUpdateProductCommand.cs` |
| Query | `Get{Name}Query` | `GetProductQuery.cs` |
| Handler | `{Command/Query}Handler` | `AddUpdateProductCommandHandler` |
| Controller | `{Feature}Controller` | `ProductsController.cs` |
| Configuration | `{Entity}Configuration` | `ProductConfiguration.cs` |
| Model/DTO | `{Name}Model` | `ProductModel.cs` |

### Namespaces

| Layer | Namespace |
|-------|-----------|
| Domain Entities | `ClassifiedAds.Domain.Entities` |
| Domain Repositories | `ClassifiedAds.Domain.Repositories` |
| Application Commands | `ClassifiedAds.Application.{Feature}.Commands` |
| Application Queries | `ClassifiedAds.Application.{Feature}.Queries` |
| Persistence Configs | `ClassifiedAds.Persistence.DbConfigurations` |
| Persistence Repos | `ClassifiedAds.Persistence.Repositories` |
| WebAPI Controllers | `ClassifiedAds.WebAPI.Controllers` |
| WebAPI Models | `ClassifiedAds.WebAPI.Models.{Feature}` |

---

## Code Structure Templates

### Complete Feature Implementation Checklist

Khi tạo một feature mới (ví dụ: `Order`), cần tạo các files sau:

1. **Domain Layer**
   - [ ] `ClassifiedAds.Domain/Entities/Order.cs`
   - [ ] `ClassifiedAds.Domain/Repositories/IOrderRepository.cs` (nếu cần custom methods)

2. **Application Layer**
   - [ ] `ClassifiedAds.Application/Orders/Commands/AddUpdateOrderCommand.cs`
   - [ ] `ClassifiedAds.Application/Orders/Commands/DeleteOrderCommand.cs`
   - [ ] `ClassifiedAds.Application/Orders/Queries/GetOrderQuery.cs`
   - [ ] `ClassifiedAds.Application/Orders/Queries/GetOrdersQuery.cs`
   - [ ] `ClassifiedAds.Application/Orders/DTOs/OrderDto.cs` (nếu cần)
   - [ ] `ClassifiedAds.Application/Orders/Services/IOrderService.cs` (nếu cần custom logic)
   - [ ] `ClassifiedAds.Application/Orders/Services/OrderService.cs`

3. **Persistence Layer**
   - [ ] `ClassifiedAds.Persistence/DbConfigurations/OrderConfiguration.cs`
   - [ ] `ClassifiedAds.Persistence/Repositories/OrderRepository.cs` (nếu có custom interface)
   - [ ] Migration: `dotnet ef migrations add AddOrderTable`

4. **WebAPI Layer**
   - [ ] `ClassifiedAds.WebAPI/Controllers/OrdersController.cs`
   - [ ] `ClassifiedAds.WebAPI/Models/Orders/OrderModel.cs`
   - [ ] `ClassifiedAds.WebAPI/Authorization/Permissions.cs` - thêm permissions

5. **Tests**
   - [ ] Unit tests cho Commands/Queries
   - [ ] Integration tests cho Controller

---

## Summary: Quick Reference

| ❌ KHÔNG | ✅ LUÔN |
|----------|---------|
| Business logic trong Controller | Dùng Dispatcher + Commands/Queries |
| DbContext trong Application Layer | Dùng Repository interfaces |
| Data Annotations trong Entity | Dùng Fluent API Configuration |
| `newsequentialid()` | `gen_random_uuid()` |
| Query không có WHERE | Projection + Filtering |
| N+1 queries | Eager loading / Batch queries |
| Public handler classes | `internal` handler classes |
| Bỏ qua CancellationToken | Pass CancellationToken |
