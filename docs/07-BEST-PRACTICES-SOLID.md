# 📚 Best Practices & SOLID Principles

## Mục Lục
1. [SOLID Principles](#solid-principles)
2. [Clean Code Practices](#clean-code-practices)
3. [Dependency Injection](#dependency-injection)
4. [Error Handling](#error-handling)
5. [Async/Await Best Practices](#asyncawait-best-practices)
6. [Logging Best Practices](#logging-best-practices)
7. [Security Best Practices](#security-best-practices)
8. [Testing Best Practices](#testing-best-practices)

---

## SOLID Principles

### S - Single Responsibility Principle (SRP)

**Định nghĩa:** Một class chỉ nên có một lý do để thay đổi.

```csharp
// ❌ Vi phạm SRP - Class làm quá nhiều việc
public class ProductService
{
    public void AddProduct(Product product) { }
    public void SendEmail(string to, string subject) { }  // ❌ Không liên quan
    public void GeneratePdf(Product product) { }          // ❌ Không liên quan
    public void LogToDatabase(string message) { }         // ❌ Không liên quan
}

// ✅ Tuân thủ SRP - Mỗi class một nhiệm vụ
public class ProductService
{
    public void AddProduct(Product product) { }
}

public class EmailService
{
    public void SendEmail(string to, string subject) { }
}

public class PdfService
{
    public void GeneratePdf(Product product) { }
}
```

**Trong codebase này:**
- `CrudService<T>` - Chỉ xử lý CRUD operations
- `EmbeddingService` - Chỉ xử lý vector embeddings
- `ProductsController` - Chỉ xử lý HTTP requests cho Products

---

### O - Open/Closed Principle (OCP)

**Định nghĩa:** Classes nên open for extension, nhưng closed for modification.

```csharp
// ❌ Vi phạm OCP - Phải modify class khi thêm storage type mới
public class FileStorageManager
{
    public void Save(string type, byte[] data)
    {
        if (type == "azure")
        {
            // Azure logic
        }
        else if (type == "local")  // Phải thêm else if mỗi khi có type mới
        {
            // Local logic
        }
    }
}

// ✅ Tuân thủ OCP - Extend bằng cách tạo class mới
public interface IFileStorageManager
{
    Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default);
}

public class AzureBlobStorageManager : IFileStorageManager
{
    public async Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default)
    {
        // Azure logic
    }
}

public class LocalFileStorageManager : IFileStorageManager
{
    public async Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default)
    {
        // Local logic
    }
}
```

**Trong codebase này:**
- `IFileStorageManager` interface với multiple implementations
- `IMessageSender<T>` với RabbitMQ, Azure Service Bus, Kafka implementations
- Decorator pattern cho `AuditLog`, `DatabaseRetry`

---

### L - Liskov Substitution Principle (LSP)

**Định nghĩa:** Derived classes phải có thể thay thế base class mà không làm thay đổi correctness.

```csharp
// ❌ Vi phạm LSP
public class Bird
{
    public virtual void Fly() { }
}

public class Penguin : Bird
{
    public override void Fly()
    {
        throw new NotSupportedException();  // ❌ Vi phạm LSP!
    }
}

// ✅ Tuân thủ LSP - Thiết kế lại hierarchy
public interface IFlyable
{
    void Fly();
}

public class Sparrow : IFlyable
{
    public void Fly() { /* Flies */ }
}

public class Penguin  // Không implement IFlyable
{
    public void Swim() { /* Swims */ }
}
```

**Trong codebase này:**
- Tất cả `IRepository<T, TKey>` implementations hoạt động giống nhau
- Tất cả `IQueryHandler<TQuery, TResult>` implementations có behavior nhất quán

---

### I - Interface Segregation Principle (ISP)

**Định nghĩa:** Clients không nên bị buộc phải phụ thuộc vào interfaces mà họ không sử dụng.

```csharp
// ❌ Vi phạm ISP - Interface quá lớn
public interface IWorker
{
    void Work();
    void Eat();
    void Sleep();
    void TakeVacation();
}

// ✅ Tuân thủ ISP - Tách thành nhiều interfaces nhỏ
public interface IWorkable
{
    void Work();
}

public interface IFeedable
{
    void Eat();
}

public interface IRestable
{
    void Sleep();
}
```

**Trong codebase này:**
- `ICommand` vs `IQuery<T>` - Tách biệt commands và queries
- `IHasKey<TKey>` vs `ITrackable` - Interfaces nhỏ, focused
- `IMessageSender` vs `IMessageReceiver` - Tách send và receive

---

### D - Dependency Inversion Principle (DIP)

**Định nghĩa:** High-level modules không nên phụ thuộc vào low-level modules. Cả hai nên phụ thuộc vào abstractions.

```csharp
// ❌ Vi phạm DIP - Phụ thuộc trực tiếp vào concrete class
public class ProductService
{
    private readonly SqlProductRepository _repository;  // ❌ Concrete class

    public ProductService()
    {
        _repository = new SqlProductRepository();  // ❌ Direct instantiation
    }
}

// ✅ Tuân thủ DIP - Phụ thuộc vào abstraction
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;  // ✅ Interface

    public ProductService(IRepository<Product, Guid> repository)  // ✅ DI
    {
        _repository = repository;
    }
}
```

**Trong codebase này:**
- Domain Layer định nghĩa interfaces (`IRepository<T, TKey>`, `IFileStorageManager`)
- Persistence/Infrastructure Layer implement interfaces
- Application Layer sử dụng interfaces, không biết concrete implementations

---

## Clean Code Practices

### Meaningful Names

```csharp
// ❌ Tên không rõ ràng
public class Mgr
{
    public void Proc(Obj o) { }
    public int Calc(int x, int y) { }
}

// ✅ Tên có ý nghĩa
public class ProductManager
{
    public void ProcessOrder(Order order) { }
    public int CalculateTotal(int price, int quantity) { }
}
```

### Small Functions

```csharp
// ❌ Function quá lớn
public async Task ProcessOrderAsync(Order order)
{
    // 100+ lines của logic...
    // Validate
    // Calculate
    // Save
    // Send email
    // Update inventory
}

// ✅ Tách thành các functions nhỏ
public async Task ProcessOrderAsync(Order order)
{
    ValidateOrder(order);
    CalculateTotals(order);
    await SaveOrderAsync(order);
    await SendConfirmationEmailAsync(order);
    await UpdateInventoryAsync(order);
}

private void ValidateOrder(Order order)
{
    ValidationException.Requires(order.Items.Any(), "Order must have items");
}

private void CalculateTotals(Order order)
{
    order.Total = order.Items.Sum(x => x.Price * x.Quantity);
}
```

### Avoid Magic Numbers/Strings

```csharp
// ❌ Magic numbers
if (user.Role == 1) { }
if (order.Status == "P") { }

// ✅ Sử dụng constants hoặc enums
public static class UserRoles
{
    public const int Admin = 1;
    public const int User = 2;
}

public enum OrderStatus
{
    Pending,
    Processing,
    Completed
}

if (user.Role == UserRoles.Admin) { }
if (order.Status == OrderStatus.Pending) { }
```

### Don't Repeat Yourself (DRY)

```csharp
// ❌ Code lặp lại
public async Task<User> GetUserByIdAsync(Guid id)
{
    var user = await _repository.FirstOrDefaultAsync(
        _repository.GetQueryableSet().Where(x => x.Id == id));
    if (user == null) throw new NotFoundException($"User with id {id} not found");
    return user;
}

public async Task<Product> GetProductByIdAsync(Guid id)
{
    var product = await _repository.FirstOrDefaultAsync(
        _repository.GetQueryableSet().Where(x => x.Id == id));
    if (product == null) throw new NotFoundException($"Product with id {id} not found");
    return product;
}

// ✅ Generic method
public async Task<T> GetByIdAsync<T>(Guid id) where T : Entity<Guid>
{
    var entity = await _repository.FirstOrDefaultAsync(
        _repository.GetQueryableSet().Where(x => x.Id == id));
    if (entity == null) throw new NotFoundException($"{typeof(T).Name} with id {id} not found");
    return entity;
}
```

---

## Dependency Injection

### Constructor Injection (Preferred)

```csharp
// ✅ Constructor injection
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IRepository<Product, Guid> repository,
        ILogger<ProductService> logger)
    {
        _repository = repository;
        _logger = logger;
    }
}
```

### Avoid Service Locator Pattern

```csharp
// ❌ Service Locator - Anti-pattern
public class ProductService
{
    public void DoSomething()
    {
        var repository = ServiceLocator.GetService<IRepository<Product, Guid>>();  // ❌
    }
}

// ✅ Inject qua constructor
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;

    public ProductService(IRepository<Product, Guid> repository)
    {
        _repository = repository;
    }
}
```

### Scoped vs Singleton vs Transient

```csharp
// Singleton - One instance for entire application lifetime
services.AddSingleton<ICacheService, CacheService>();  // Shared state, thread-safe

// Scoped - One instance per HTTP request
services.AddScoped<IRepository<Product, Guid>, Repository<Product, Guid>>();  // DbContext
services.AddScoped<IUnitOfWork>();  // Transaction scope

// Transient - New instance every time
services.AddTransient<IEmailService, EmailService>();  // Stateless services
```

---

## Error Handling

### Custom Exception Types

```csharp
// Defined in CrossCuttingConcerns
public class ValidationException : Exception
{
    public static void Requires(bool expected, string errorMessage)
    {
        if (!expected) throw new ValidationException(errorMessage);
    }

    public ValidationException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
```

### Proper Exception Handling

```csharp
// ❌ Catch all exceptions
try
{
    await DoSomethingAsync();
}
catch (Exception)
{
    // Swallowed - ❌ Bad!
}

// ❌ Catch and rethrow incorrectly
catch (Exception ex)
{
    throw ex;  // ❌ Loses stack trace!
}

// ✅ Proper handling
try
{
    await DoSomethingAsync();
}
catch (ValidationException)
{
    throw;  // ✅ Re-throw business exceptions
}
catch (DbException ex)
{
    _logger.LogError(ex, "Database error");
    throw new InfrastructureException("Database operation failed", ex);
}
```

### Global Exception Handler

```csharp
// In WebAPI - GlobalExceptionHandler.cs
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, message) = exception switch
        {
            ValidationException ex => (400, ex.Message),
            NotFoundException ex => (404, ex.Message),
            UnauthorizedAccessException => (401, "Unauthorized"),
            _ => (500, "An error occurred")
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = message
        });

        return true;
    }
}
```

---

## Async/Await Best Practices

### Always Use Async Suffix

```csharp
// ✅ Method names với Async suffix
public async Task<Product> GetProductByIdAsync(Guid id)
public async Task AddProductAsync(Product product)
public async Task<List<Product>> GetProductsAsync()
```

### Don't Block on Async Code

```csharp
// ❌ Blocking on async - DEADLOCK RISK!
var result = GetProductsAsync().Result;
var result = GetProductsAsync().GetAwaiter().GetResult();

// ✅ Properly await
var result = await GetProductsAsync();
```

### ConfigureAwait in Libraries

```csharp
// In library code (Infrastructure, Persistence)
public async Task<byte[]> ReadFileAsync()
{
    return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
}

// In ASP.NET Core - không cần ConfigureAwait(false)
public async Task<ActionResult> Get()
{
    var products = await _dispatcher.DispatchAsync(new GetProductsQuery());
    return Ok(products);
}
```

### Cancellation Token

```csharp
// ✅ Always pass CancellationToken
public async Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
{
    return await _repository.ToListAsync(
        _repository.GetQueryableSet(),
        cancellationToken);  // ✅ Pass through
}

// In controller
[HttpGet]
public async Task<ActionResult> Get(CancellationToken cancellationToken)
{
    var products = await _service.GetProductsAsync(cancellationToken);
    return Ok(products);
}
```

---

## Logging Best Practices

### Structured Logging

```csharp
// ❌ String concatenation
_logger.LogInformation("User " + userId + " created product " + productId);

// ✅ Structured logging với placeholders
_logger.LogInformation("User {UserId} created product {ProductId}", userId, productId);
```

### Log Levels

```csharp
_logger.LogTrace("Detailed trace message");           // Development only
_logger.LogDebug("Debug info: {@Product}", product);  // Debug builds
_logger.LogInformation("Product {ProductId} created", productId);  // Normal operations
_logger.LogWarning("Product {ProductId} not found", productId);    // Potential issues
_logger.LogError(ex, "Failed to create product {ProductId}", productId);  // Errors
_logger.LogCritical(ex, "Application crash");         // Critical failures
```

### What to Log

```csharp
// ✅ Log entry/exit points
public async Task<Product> GetProductByIdAsync(Guid id)
{
    _logger.LogInformation("Getting product {ProductId}", id);

    var product = await _repository.GetByIdAsync(id);

    if (product == null)
    {
        _logger.LogWarning("Product {ProductId} not found", id);
    }

    return product;
}

// ✅ Log exceptions với context
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process order {OrderId} for user {UserId}", orderId, userId);
    throw;
}
```

### Don't Log Sensitive Data

```csharp
// ❌ NEVER log sensitive data
_logger.LogInformation("User logged in with password {Password}", password);
_logger.LogInformation("Credit card {CardNumber}", cardNumber);

// ✅ Log only non-sensitive identifiers
_logger.LogInformation("User {UserId} logged in", userId);
_logger.LogInformation("Payment processed for order {OrderId}", orderId);
```

---

## Security Best Practices

### Input Validation

```csharp
public async Task HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
{
    // ✅ Validate all input
    ValidationException.Requires(!string.IsNullOrEmpty(command.Product.Name), "Name is required");
    ValidationException.Requires(command.Product.Name.Length <= 200, "Name too long");
    ValidationException.Requires(command.Product.Price >= 0, "Price must be positive");
}
```

### SQL Injection Prevention

```csharp
// ❌ NEVER concatenate SQL
var sql = $"SELECT * FROM Products WHERE Name = '{name}'";  // ❌ SQL Injection!

// ✅ Use parameterized queries
var products = await _context.Products
    .Where(x => x.Name == name)  // ✅ EF Core handles parameterization
    .ToListAsync();

// ✅ Or use parameters with raw SQL
var sql = "SELECT * FROM \"Products\" WHERE \"Name\" = @Name";
await _context.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("Name", name));
```

### Authentication & Authorization

```csharp
// ✅ Always require authentication
[Authorize]
[ApiController]
public class ProductsController : ControllerBase

// ✅ Fine-grained authorization
[Authorize(Permissions.DeleteProduct)]
[HttpDelete("{id}")]
public async Task<ActionResult> Delete(Guid id)
```

---

## Testing Best Practices

### Unit Test Structure (AAA Pattern)

```csharp
[Fact]
public async Task GetProductById_ReturnsProduct_WhenProductExists()
{
    // Arrange
    var productId = Guid.NewGuid();
    var expectedProduct = new Product { Id = productId, Name = "Test" };
    
    _mockRepository.Setup(x => x.GetByIdAsync(productId))
        .ReturnsAsync(expectedProduct);

    // Act
    var result = await _service.GetProductByIdAsync(productId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(expectedProduct.Name, result.Name);
}
```

### Test Naming Convention

```csharp
// Pattern: MethodName_ExpectedBehavior_WhenCondition
[Fact]
public async Task AddProduct_ThrowsValidationException_WhenNameIsEmpty()

[Fact]
public async Task GetProducts_ReturnsEmptyList_WhenNoProductsExist()

[Fact]
public async Task DeleteProduct_ReturnsNoContent_WhenProductExists()
```

### Mock Dependencies

```csharp
public class ProductServiceTests
{
    private readonly Mock<IRepository<Product, Guid>> _mockRepository;
    private readonly Mock<ILogger<ProductService>> _mockLogger;
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _mockRepository = new Mock<IRepository<Product, Guid>>();
        _mockLogger = new Mock<ILogger<ProductService>>();
        _service = new ProductService(_mockRepository.Object, _mockLogger.Object);
    }
}
```

### Integration Tests

```csharp
public class ProductsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProductsControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProducts_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

---

## Summary

| Principle | Key Takeaway |
|-----------|--------------|
| SRP | Một class, một nhiệm vụ |
| OCP | Extend, không modify |
| LSP | Subclass phải thay thế được base class |
| ISP | Interfaces nhỏ, focused |
| DIP | Depend on abstractions |
| Clean Code | Meaningful names, small functions |
| DI | Constructor injection, avoid service locator |
| Error Handling | Custom exceptions, global handler |
| Async | Use async suffix, don't block, use CancellationToken |
| Logging | Structured logging, appropriate levels |
| Security | Validate input, prevent SQL injection, authorize |
| Testing | AAA pattern, meaningful names, mock dependencies |
