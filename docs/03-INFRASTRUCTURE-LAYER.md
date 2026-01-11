# 🔌 Infrastructure Layer

## Mục Lục
1. [Tổng quan](#tổng-quan)
2. [Cấu trúc thư mục](#cấu-trúc-thư-mục)
3. [Message Bus](#message-bus)
4. [File Storage](#file-storage)
5. [Email & SMS](#email--sms)
6. [AI Services](#ai-services)
7. [Caching](#caching)
8. [Health Checks](#health-checks)
9. [Logging & Monitoring](#logging--monitoring)
10. [Quy tắc Implementation](#quy-tắc-implementation)

---

## Tổng quan

**Infrastructure Layer** chứa implementations của các interfaces được định nghĩa trong Domain Layer:
- Message Bus (RabbitMQ, Azure Service Bus, Kafka)
- File Storage (Azure Blob, Local, FTP)
- Notification Services (Email, SMS)
- AI Services (Embeddings, Image Analysis)
- Caching
- External API integrations

**Đặc điểm:**
- ✅ Implement interfaces từ Domain Layer
- ✅ Chứa external service integrations
- ❌ **KHÔNG** chứa business logic
- ✅ Dễ dàng swap implementations

---

## Cấu trúc thư mục

```
ClassifiedAds.Infrastructure/
├── AI/                          # AI services
│   ├── EmbeddingService.cs      # Vector embeddings
│   └── ImageAnalysisService.cs  # Image analysis
├── Caching/                     # Caching implementations
│   └── CachingExtensions.cs
├── Configuration/               # Configuration providers
│   └── PostgreSqlConfigurationProvider.cs
├── Csv/                         # CSV utilities
├── DateTimes/                   # DateTime providers
├── Excel/                       # Excel utilities
├── FeatureToggles/             # Feature flag implementations
├── HealthChecks/               # Health check implementations
├── HostedServices/             # Background service utilities
├── Html/                       # HTML utilities
├── HttpMessageHandlers/        # HTTP handlers
├── Identity/                   # Identity implementations
├── IdentityProviders/          # External identity providers
├── Interceptors/               # DI interceptors
├── Localization/               # Localization implementations
├── Logging/                    # Logging configuration
├── Messaging/                  # Message bus implementations
│   ├── AzureServiceBus/
│   ├── RabbitMQ/
│   ├── Kafka/
│   └── Fake/
├── Monitoring/                 # Monitoring & telemetry
├── Notification/               # Email/SMS implementations
├── Pdf/                        # PDF utilities
├── Storages/                   # File storage implementations
│   ├── Azure/
│   ├── Amazon/
│   ├── Local/
│   └── Fake/
└── Web/                        # Web utilities
```

---

## Message Bus

### Interface (Domain Layer)

```csharp
// IMessageBus.cs (Domain)
public interface IMessageBus
{
    Task SendAsync<T>(T message, MetaData metaData = null, CancellationToken cancellationToken = default)
        where T : IMessageBusMessage;

    Task ReceiveAsync<TConsumer, T>(Func<T, MetaData, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        where T : IMessageBusMessage;

    Task SendAsync(PublishingOutboxMessage outbox, CancellationToken cancellationToken = default);
}
```

### RabbitMQ Implementation

```csharp
// RabbitMQSender.cs
public class RabbitMQSender : IMessageSender<RabbitMQSenderOptions>
{
    private readonly RabbitMQSenderOptions _options;

    public RabbitMQSender(RabbitMQSenderOptions options)
    {
        _options = options;
    }

    public async Task SendAsync<T>(T message, MetaData metaData = null, CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            UserName = _options.UserName,
            Password = _options.Password
        };

        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: _options.RoutingKey,
            body: body,
            cancellationToken: cancellationToken);
    }
}
```

### Azure Service Bus Implementation

```csharp
// AzureServiceBusSender.cs
public class AzureServiceBusSender : IMessageSender<AzureServiceBusSenderOptions>
{
    private readonly AzureServiceBusSenderOptions _options;

    public async Task SendAsync<T>(T message, MetaData metaData = null, CancellationToken cancellationToken = default)
    {
        await using var client = new ServiceBusClient(_options.ConnectionString);
        await using var sender = client.CreateSender(_options.QueueName);

        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message));

        if (metaData != null)
        {
            serviceBusMessage.ApplicationProperties["CorrelationId"] = metaData.CorrelationId;
        }

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}
```

### Cách đăng ký (DI)

```csharp
// MessagingCollectionExtensions.cs
public static IServiceCollection AddMessageBus(
    this IServiceCollection services,
    MessageBusOptions options)
{
    return options.Provider switch
    {
        "RabbitMQ" => services.AddRabbitMQ(options.RabbitMQ),
        "AzureServiceBus" => services.AddAzureServiceBus(options.AzureServiceBus),
        "Kafka" => services.AddKafka(options.Kafka),
        _ => services.AddFakeMessageBus()
    };
}
```

---

## File Storage

### Interface (Domain Layer)

```csharp
// IFileStorageManager.cs (Domain)
public interface IFileStorageManager
{
    Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task DeleteAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task ArchiveAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
    Task UnArchiveAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default);
}
```

### Azure Blob Storage Implementation

```csharp
// AzureBlobStorageManager.cs
public class AzureBlobStorageManager : IFileStorageManager
{
    private readonly AzureBlobOption _option;

    public AzureBlobStorageManager(AzureBlobOption option)
    {
        _option = option;
    }

    public async Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default)
    {
        var containerClient = new BlobContainerClient(_option.ConnectionString, _option.Container);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(fileEntry.FileLocation);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
    }

    public async Task<byte[]> ReadAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default)
    {
        var containerClient = new BlobContainerClient(_option.ConnectionString, _option.Container);
        var blobClient = containerClient.GetBlobClient(fileEntry.FileLocation);

        using var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    public async Task DeleteAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default)
    {
        var containerClient = new BlobContainerClient(_option.ConnectionString, _option.Container);
        var blobClient = containerClient.GetBlobClient(fileEntry.FileLocation);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
```

### Local Storage Implementation

```csharp
// LocalFileStorageManager.cs
public class LocalFileStorageManager : IFileStorageManager
{
    private readonly LocalOptions _option;

    public async Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_option.Path, fileEntry.FileLocation);
        var directory = Path.GetDirectoryName(path);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(path);
        await stream.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_option.Path, fileEntry.FileLocation);
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task DeleteAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_option.Path, fileEntry.FileLocation);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
```

---

## Email & SMS

### Email Service

```csharp
// IEmailNotification.cs (Domain)
public interface IEmailNotification
{
    Task SendAsync(EmailMessage emailMessage, CancellationToken cancellationToken = default);
}

// SmtpEmailNotification.cs (Infrastructure)
public class SmtpEmailNotification : IEmailNotification
{
    private readonly SmtpOptions _options;

    public async Task SendAsync(EmailMessage emailMessage, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            EnableSsl = _options.EnableSsl
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.From),
            Subject = emailMessage.Subject,
            Body = emailMessage.Body,
            IsBodyHtml = true
        };

        foreach (var to in emailMessage.To.Split(';'))
        {
            mailMessage.To.Add(to.Trim());
        }

        await client.SendMailAsync(mailMessage, cancellationToken);
    }
}
```

### SMS Service

```csharp
// ISmsNotification.cs (Domain)
public interface ISmsNotification
{
    Task SendAsync(SmsMessage smsMessage, CancellationToken cancellationToken = default);
}

// TwilioSmsNotification.cs (Infrastructure)
public class TwilioSmsNotification : ISmsNotification
{
    private readonly TwilioOptions _options;

    public async Task SendAsync(SmsMessage smsMessage, CancellationToken cancellationToken = default)
    {
        TwilioClient.Init(_options.AccountSid, _options.AuthToken);

        await MessageResource.CreateAsync(
            body: smsMessage.Message,
            from: new Twilio.Types.PhoneNumber(_options.FromNumber),
            to: new Twilio.Types.PhoneNumber(smsMessage.PhoneNumber));
    }
}
```

---

## AI Services

### Embedding Service

```csharp
// EmbeddingService.cs
public class EmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly IRepository<EmbeddingCacheEntry, Guid> _cacheRepository;

    public EmbeddingService(
        AzureOpenAIClient azureOpenAIClient,
        IRepository<EmbeddingCacheEntry, Guid> cacheRepository,
        IOptions<OpenAIOptions> options)
    {
        _embeddingClient = azureOpenAIClient.GetEmbeddingClient(options.Value.EmbeddingDeploymentName);
        _cacheRepository = cacheRepository;
    }

    public async Task<EmbeddingResult> GenerateAsync(string text)
    {
        // 1. Check cache
        var hash = ComputeHash(text);
        var cached = await _cacheRepository.FirstOrDefaultAsync(
            _cacheRepository.GetQueryableSet().Where(x => x.Hash == hash));

        if (cached != null)
        {
            return new EmbeddingResult
            {
                Vector = JsonSerializer.Deserialize<float[]>(cached.Embedding),
                TokenDetails = cached.TokenDetails
            };
        }

        // 2. Generate embedding
        var response = await _embeddingClient.GenerateEmbeddingAsync(text);
        var embedding = response.Value;

        // 3. Cache result
        var cacheEntry = new EmbeddingCacheEntry
        {
            Hash = hash,
            Text = text,
            Embedding = JsonSerializer.Serialize(embedding.ToFloats().ToArray()),
            TokenDetails = $"Input: {embedding.Usage.InputTokenCount}, Total: {embedding.Usage.TotalTokenCount}"
        };

        await _cacheRepository.AddAsync(cacheEntry);
        await _cacheRepository.UnitOfWork.SaveChangesAsync();

        return new EmbeddingResult
        {
            Vector = embedding.ToFloats().ToArray(),
            TokenDetails = cacheEntry.TokenDetails
        };
    }
}
```

### Image Analysis Service

```csharp
// ImageAnalysisService.cs
public class ImageAnalysisService
{
    private readonly ChatClient _chatClient;

    public ImageAnalysisService(AzureOpenAIClient azureOpenAIClient, IOptions<OpenAIOptions> options)
    {
        _chatClient = azureOpenAIClient.GetChatClient(options.Value.ChatDeploymentName);
    }

    public async Task<string> AnalyzeAsync(byte[] imageBytes, string prompt)
    {
        var imageData = BinaryData.FromBytes(imageBytes);
        var imagePart = ChatMessageContentPart.CreateImagePart(imageData, "image/jpeg");
        var textPart = ChatMessageContentPart.CreateTextPart(prompt);

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(imagePart, textPart)
        };

        var response = await _chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
    }
}
```

---

## Caching

### PostgreSQL Distributed Cache

```csharp
// CachingExtensions.cs
public static IServiceCollection AddCachingServices(
    this IServiceCollection services,
    CachingOptions options)
{
    if (options.Provider == "PostgreSQL")
    {
        services.AddDistributedPostgreSqlCache(opts =>
        {
            opts.ConnectionString = options.PostgreSQL.ConnectionString;
            opts.SchemaName = "public";
            opts.TableName = "DistributedCache";
        });
    }

    return services;
}
```

### Sử dụng Cache

```csharp
public class SomeService
{
    private readonly IDistributedCache _cache;

    public async Task<Product> GetProductAsync(Guid id)
    {
        var cacheKey = $"product:{id}";

        // Try get from cache
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached != null)
        {
            return JsonSerializer.Deserialize<Product>(cached);
        }

        // Get from database
        var product = await _repository.GetByIdAsync(id);

        // Cache result
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(product),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

        return product;
    }
}
```

---

## Health Checks

```csharp
// HealthChecksExtensions.cs
public static IServiceCollection AddHealthChecks(
    this IServiceCollection services,
    HealthChecksOptions options)
{
    var builder = services.AddHealthChecks();

    // Database health check
    builder.AddNpgSql(
        options.ConnectionString,
        name: "postgres",
        tags: new[] { "database" });

    // RabbitMQ health check
    if (options.RabbitMQ.Enabled)
    {
        builder.AddRabbitMQ(
            options.RabbitMQ.ConnectionString,
            name: "rabbitmq",
            tags: new[] { "messaging" });
    }

    // Custom health check
    builder.AddCheck<CustomHealthCheck>("custom");

    return services;
}
```

---

## Logging & Monitoring

### OpenTelemetry Configuration

```csharp
// MonitoringExtensions.cs
public static IServiceCollection AddMonitoringServices(
    this IServiceCollection services,
    MonitoringOptions options)
{
    if (options.OpenTelemetry.Enabled)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(options.OpenTelemetry.Endpoint);
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            });
    }

    return services;
}
```

---

## Quy tắc Implementation

### ✅ PHẢI LÀM

```csharp
// 1. Implement interface từ Domain Layer
public class AzureBlobStorageManager : IFileStorageManager

// 2. Configuration qua Options pattern
public class AzureBlobStorageManager
{
    private readonly AzureBlobOption _option;

    public AzureBlobStorageManager(IOptions<AzureBlobOption> option)
    {
        _option = option.Value;
    }
}

// 3. Proper exception handling
public async Task CreateAsync(IFileEntry fileEntry, Stream stream, CancellationToken cancellationToken = default)
{
    try
    {
        // implementation
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        throw new NotFoundException($"Container not found: {_option.Container}");
    }
}

// 4. Sử dụng CancellationToken
public async Task<byte[]> ReadAsync(IFileEntry fileEntry, CancellationToken cancellationToken = default)

// 5. Dispose resources properly
public async Task SendAsync(SmsMessage smsMessage, CancellationToken cancellationToken = default)
{
    await using var client = new ServiceBusClient(_options.ConnectionString);
    // ...
}
```

### ❌ KHÔNG ĐƯỢC LÀM

```csharp
// 1. KHÔNG có business logic trong Infrastructure
public class EmailService : IEmailNotification
{
    public async Task SendAsync(EmailMessage msg, CancellationToken cancellationToken = default)
    {
        // ❌ Sai! Business logic thuộc về Application Layer
        if (msg.To.Contains("@blocked.com"))
        {
            throw new Exception("Blocked email");
        }
    }
}

// 2. KHÔNG hardcode configuration
var client = new BlobServiceClient("AccountName=...");  // ❌ Sai!

// 3. KHÔNG ignore CancellationToken
public async Task SendAsync(Message msg)  // ❌ Thiếu CancellationToken

// 4. KHÔNG catch exception và swallow
catch (Exception ex)
{
    // ❌ Sai! Phải log hoặc rethrow
}
```

---

## Checklist khi tạo Infrastructure Service mới

- [ ] Service implement interface từ Domain Layer
- [ ] Configuration sử dụng Options pattern
- [ ] CancellationToken được support
- [ ] Resources được dispose properly (using/await using)
- [ ] Exceptions được handle và wrap phù hợp
- [ ] Health check được thêm nếu cần
- [ ] Logging được thêm cho operations quan trọng
- [ ] Unit tests với mocks đã được viết
- [ ] DI registration được thêm trong extension method
