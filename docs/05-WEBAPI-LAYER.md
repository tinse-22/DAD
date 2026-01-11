# 🌐 WebAPI Layer

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [Cấu trúc thư mục](#cấu-trúc-thư-mục)
3. [Program.cs Setup](#programcs-setup)
4. [Controllers](#controllers)
5. [Models & DTOs](#models--dtos)
6. [Authorization](#authorization)
7. [Rate Limiting](#rate-limiting)
8. [Exception Handling](#exception-handling)
9. [API Documentation](#api-documentation)
10. [Quy tắc Implementation](#quy-tắc-implementation)

---

## Tổng quan

**WebAPI Layer** là presentation layer, chứa:
- REST API controllers
- Request/Response models
- Authorization policies
- Rate limiting
- Exception handling
- API documentation (Scalar/OpenAPI)

**Đặc điểm:**
- ✅ Thin layer - chỉ chứa presentation logic
- ✅ Delegate business logic cho Application Layer
- ✅ Handle HTTP concerns (status codes, headers, etc.)
- ❌ **KHÔNG** chứa business logic

---

## Cấu trúc thư mục

```
ClassifiedAds.WebAPI/
├── Program.cs                    # Application entry point
├── appsettings.json             # Configuration
├── appsettings.Development.json # Dev configuration
├── Authorization/               # Authorization policies
│   ├── Permissions.cs
│   ├── PermissionRequirement.cs
│   └── AuthorizationExtensions.cs
├── ConfigurationOptions/        # Strongly typed options
│   └── AppSettings.cs
├── Controllers/                 # API Controllers
│   ├── ProductsController.cs
│   ├── UsersController.cs
│   ├── FilesController.cs
│   └── ...
├── Hubs/                       # SignalR hubs
│   └── NotificationHub.cs
├── Models/                     # Request/Response models
│   ├── Products/
│   │   ├── ProductModel.cs
│   │   └── CreateProductRequest.cs
│   └── ...
├── RateLimiterPolicies/        # Rate limiting
│   └── DefaultRateLimiterPolicy.cs
├── Tenants/                    # Multi-tenancy
│   └── TenantResolver.cs
├── Templates/                  # Email/notification templates
├── Certs/                     # Certificates
├── Properties/
│   └── launchSettings.json
└── Dockerfile
```

---

## Program.cs Setup

### Service Registration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;
var configuration = builder.Configuration;

var appSettings = new AppSettings();
configuration.Bind(appSettings);

// 1. Monitoring & Logging
builder.WebHost.UseClassifiedAdsLogger(config => appSettings.Logging);
services.AddMonitoringServices(appSettings.Monitoring);

// 2. Exception Handling
services.AddExceptionHandler<GlobalExceptionHandler>();

// 3. Controllers
services.AddControllers()
    .ConfigureApiBehaviorOptions(options => { })
    .AddJsonOptions(options => { });

// 4. SignalR
services.AddSignalR();

// 5. CORS
services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", builder => builder
        .WithOrigins(appSettings.CORS.AllowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader());
});

// 6. DateTime Provider
services.AddDateTimeProvider();

// 7. Persistence & Domain Services
services.AddMultiTenantPersistence(
        typeof(AdsDbContextMultiTenantConnectionStringResolver),
        typeof(TenantResolver))
    .AddDomainServices()
    .AddApplicationServices((serviceType, implementationType, lifetime) =>
    {
        services.AddInterceptors(serviceType, implementationType, lifetime, appSettings.Interceptors);
    })
    .AddMessageHandlers()
    .ConfigureInterceptors()
    .AddIdentityCore();

// 8. AI Services
services.AddScoped<EmbeddingService>();
services.AddScoped<ImageAnalysisService>();

// 9. Data Protection
services.AddDataProtection()
    .PersistKeysToDbContext<AdsDbContext>()
    .SetApplicationName("ClassifiedAds");

// 10. Authentication
services.AddAuthentication(options =>
{
    options.DefaultScheme = appSettings.Authentication.Provider switch
    {
        "Jwt" => "Jwt",
        _ => JwtBearerDefaults.AuthenticationScheme
    };
})
.AddJwtBearer(options =>
{
    options.Authority = appSettings.Authentication.IdentityServer.Authority;
    options.Audience = appSettings.Authentication.IdentityServer.Audience;
})
.AddJwtBearer("Jwt", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = appSettings.Authentication.Jwt.IssuerUri,
        ValidAudience = appSettings.Authentication.Jwt.Audience,
        // ...
    };
});

// 11. Authorization Policies
services.AddAuthorizationPolicies(Assembly.GetExecutingAssembly());

// 12. Rate Limiting
services.AddRateLimiter(options =>
{
    options.AddPolicy<string, DefaultRateLimiterPolicy>(RateLimiterPolicyNames.DefaultPolicy);
});

// 13. API Documentation (Scalar)
services.AddOpenApi("ClassifiedAds", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "ClassifiedAds API",
            Version = "v1"
        };
        return Task.CompletedTask;
    });
});
```

### Middleware Pipeline

```csharp
var app = builder.Build();

// 1. Exception Handler (first)
app.UseExceptionHandler(options => { });

// 2. CORS
app.UseCors("AllowedOrigins");

// 3. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 4. Rate Limiting
app.UseRateLimiter();

// 5. API Documentation
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("ClassifiedAds API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// 6. Endpoints
app.MapControllers();
app.MapHub<NotificationHub>("/notification");

// 7. Health Checks
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
```

---

## Controllers

### Base Controller Pattern

```csharp
// ProductsController.cs
[EnableRateLimiting(RateLimiterPolicyNames.DefaultPolicy)]
[Authorize]
[Produces("application/json")]
[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;

    public ProductsController(
        Dispatcher dispatcher,
        ILogger<ProductsController> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }
}
```

### GET - List Items

```csharp
[Authorize(Permissions.GetProducts)]
[HttpGet]
public async Task<ActionResult<IEnumerable<ProductModel>>> Get()
{
    _logger.LogInformation("Getting all products");

    // 1. Dispatch query
    var products = await _dispatcher.DispatchAsync(new GetProductsQuery());

    // 2. Map to model
    var model = products.ToModels();

    return Ok(model);
}
```

### GET - Single Item

```csharp
[Authorize(Permissions.GetProduct)]
[HttpGet("{id}")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<ProductModel>> Get(Guid id)
{
    // Dispatch query với NotFound handling
    var product = await _dispatcher.DispatchAsync(new GetProductQuery
    {
        Id = id,
        ThrowNotFoundIfNull = true  // Throw NotFoundException if not found
    });

    var model = product.ToModel();
    return Ok(model);
}
```

### POST - Create Item

```csharp
[Authorize(Permissions.AddProduct)]
[HttpPost]
[Consumes("application/json")]
[ProducesResponseType(StatusCodes.Status201Created)]
public async Task<ActionResult<ProductModel>> Post([FromBody] ProductModel model)
{
    // 1. Map model to entity
    var product = model.ToEntity();

    // 2. Dispatch command
    await _dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });

    // 3. Return 201 Created với location header
    return Created($"/api/products/{product.Id}", product.ToModel());
}
```

### PUT - Update Item

```csharp
[Authorize(Permissions.UpdateProduct)]
[HttpPut("{id}")]
[Consumes("application/json")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<ProductModel>> Put(Guid id, [FromBody] ProductModel model)
{
    // 1. Get existing entity
    var product = await _dispatcher.DispatchAsync(new GetProductQuery
    {
        Id = id,
        ThrowNotFoundIfNull = true
    });

    // 2. Update properties
    product.Code = model.Code;
    product.Name = model.Name;
    product.Description = model.Description;

    // 3. Dispatch command
    await _dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });

    return Ok(product.ToModel());
}
```

### DELETE - Remove Item

```csharp
[Authorize(Permissions.DeleteProduct)]
[HttpDelete("{id}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult> Delete(Guid id)
{
    // 1. Get existing entity
    var product = await _dispatcher.DispatchAsync(new GetProductQuery
    {
        Id = id,
        ThrowNotFoundIfNull = true
    });

    // 2. Dispatch delete command
    await _dispatcher.DispatchAsync(new DeleteProductCommand { Product = product });

    // 3. Return 204 No Content
    return NoContent();
}
```

### Vector Search (AI Feature)

```csharp
[Authorize(Permissions.GetProducts)]
[HttpGet("vectorsearch")]
public async Task<ActionResult<IEnumerable<ProductModel>>> VectorSearch(string searchText)
{
    // 1. Generate embedding for search text
    var embeddingRs = await _embeddingService.GenerateAsync(searchText);
    var embedding = new Vector(embeddingRs.Vector);

    // 2. Query với vector similarity
    var products = _productEmbeddingRepository.GetQueryableSet()
        .OrderBy(x => x.Embedding.CosineDistance(embedding))  // Cosine similarity
        .Take(5)
        .Select(x => new ProductModel
        {
            Id = x.Product.Id,
            Code = x.Product.Code,
            Name = x.Product.Name,
            Description = x.Product.Description,
            SimilarityScore = x.Embedding.CosineDistance(embedding)
        }).ToList();

    return Ok(products);
}
```

---

## Models & DTOs

### Request Model

```csharp
// ProductModel.cs
public class ProductModel
{
    public Guid Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public double? SimilarityScore { get; set; }
    public ProductEmbeddingModel ProductEmbedding { get; set; }
    public List<SimilarProductModel> SimilarProducts { get; set; }
}

// Extension methods for mapping
public static class ProductModelExtensions
{
    public static ProductModel ToModel(this Product entity)
    {
        return new ProductModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Description = entity.Description
        };
    }

    public static Product ToEntity(this ProductModel model)
    {
        return new Product
        {
            Id = model.Id,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description
        };
    }

    public static List<ProductModel> ToModels(this IEnumerable<Product> entities)
    {
        return entities.Select(x => x.ToModel()).ToList();
    }
}
```

### Validation với Data Annotations

```csharp
public class CreateProductRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; }

    [StringLength(2000)]
    public string Description { get; set; }
}
```

---

## Authorization

### Permission-Based Authorization

```csharp
// Permissions.cs
public static class Permissions
{
    // Products
    public const string GetProducts = "GetProducts";
    public const string GetProduct = "GetProduct";
    public const string AddProduct = "AddProduct";
    public const string UpdateProduct = "UpdateProduct";
    public const string DeleteProduct = "DeleteProduct";

    // Users
    public const string GetUsers = "GetUsers";
    public const string GetUser = "GetUser";
    // ...
}

// AuthorizationExtensions.cs
public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(
        this IServiceCollection services,
        Assembly assembly)
    {
        services.AddAuthorization(options =>
        {
            // Get all permission constants
            var permissions = typeof(Permissions)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null));

            foreach (var permission in permissions)
            {
                options.AddPolicy(permission, policy =>
                {
                    policy.Requirements.Add(new PermissionRequirement(permission));
                });
            }
        });

        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        return services;
    }
}
```

### Sử dụng Authorization

```csharp
[Authorize(Permissions.GetProducts)]   // Require GetProducts permission
[HttpGet]
public async Task<ActionResult<IEnumerable<ProductModel>>> Get()
{
    // ...
}

[Authorize]  // Just require authentication
[HttpGet("me")]
public async Task<ActionResult<UserModel>> GetCurrentUser()
{
    // ...
}
```

---

## Rate Limiting

### Rate Limiter Policy

```csharp
// DefaultRateLimiterPolicy.cs
public class DefaultRateLimiterPolicy : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } =
        (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return ValueTask.CompletedTask;
        };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Rate limit per user or IP
        var key = httpContext.User.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,           // 100 requests
                Window = TimeSpan.FromMinutes(1), // per minute
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    }
}
```

### Sử dụng Rate Limiting

```csharp
[EnableRateLimiting(RateLimiterPolicyNames.DefaultPolicy)]
[ApiController]
public class ProductsController : ControllerBase
{
    // All actions use DefaultPolicy
}

// Or per action
[EnableRateLimiting(RateLimiterPolicyNames.GetAuditLogsPolicy)]
[HttpGet]
public async Task<ActionResult> GetAuditLogs()
{
    // This action has its own rate limit
}
```

---

## Exception Handling

### Global Exception Handler

```csharp
// GlobalExceptionHandler.cs
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception occurred");

        var (statusCode, message) = exception switch
        {
            ValidationException validationEx => (StatusCodes.Status400BadRequest, validationEx.Message),
            NotFoundException notFoundEx => (StatusCodes.Status404NotFound, notFoundEx.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            _ => (StatusCodes.Status500InternalServerError, "An error occurred")
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = message,
            Instance = httpContext.Request.Path
        }, cancellationToken);

        return true;
    }
}
```

---

## API Documentation

### Scalar Configuration

```csharp
// OpenAPI registration
services.AddOpenApi("ClassifiedAds", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "ClassifiedAds API",
            Version = "v1",
            Description = "Clean Architecture API"
        };
        return Task.CompletedTask;
    });
});

// Scalar UI
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("ClassifiedAds API")
        .WithTheme(ScalarTheme.Default)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});
```

---

## Quy tắc Implementation

### ✅ PHẢI LÀM

```csharp
// 1. Controller chỉ làm nhiệm vụ orchestration
[HttpPost]
public async Task<ActionResult> Post([FromBody] ProductModel model)
{
    var product = model.ToEntity();
    await _dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });
    return Created(...);  // ✅
}

// 2. Proper HTTP status codes
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]

// 3. Authorization trên mỗi action
[Authorize(Permissions.GetProducts)]
[HttpGet]

// 4. Rate limiting
[EnableRateLimiting(RateLimiterPolicyNames.DefaultPolicy)]

// 5. Logging
_logger.LogInformation("Getting product {ProductId}", id);
```

### ❌ KHÔNG ĐƯỢC LÀM

```csharp
// 1. KHÔNG có business logic trong Controller
[HttpPost]
public async Task<ActionResult> Post([FromBody] ProductModel model)
{
    // ❌ Sai! Business logic phải ở Application Layer
    if (await _productRepository.GetQueryableSet().AnyAsync(x => x.Code == model.Code))
    {
        return BadRequest("Code already exists");
    }
    
    _dbContext.Products.Add(model);  // ❌ Sai!
    await _dbContext.SaveChangesAsync();
}

// 2. KHÔNG inject DbContext vào Controller
private readonly AdsDbContext _dbContext;  // ❌ Sai!

// 3. KHÔNG return Entity trực tiếp (nên dùng Model/DTO)
public async Task<ActionResult<Product>> Get()  // ⚠️ Cân nhắc dùng ProductModel

// 4. KHÔNG catch exception trong Controller (dùng GlobalExceptionHandler)
try
{
    // ...
}
catch (Exception ex)  // ❌ Sai!
{
    return StatusCode(500);
}
```

---

## Checklist khi tạo Controller mới

- [ ] Controller kế thừa từ `ControllerBase`
- [ ] Attributes: `[ApiController]`, `[Route("api/[controller]")]`
- [ ] `[Authorize]` hoặc `[AllowAnonymous]` trên controller/action
- [ ] `[EnableRateLimiting]` nếu cần
- [ ] `[ProducesResponseType]` cho tất cả status codes
- [ ] Inject `Dispatcher` thay vì repositories
- [ ] Proper HTTP verbs (GET, POST, PUT, DELETE)
- [ ] Proper status codes (200, 201, 204, 400, 404)
- [ ] Logging cho operations quan trọng
- [ ] Model validation với Data Annotations
- [ ] API documentation comments nếu cần
