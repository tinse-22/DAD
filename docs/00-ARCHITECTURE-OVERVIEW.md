# 🏗️ Clean Architecture Overview

## Mục Lục
1. [Giới thiệu](#giới-thiệu)
2. [Cấu trúc Solution](#cấu-trúc-solution)
3. [Dependency Rule](#dependency-rule)
4. [Layer Diagram](#layer-diagram)
5. [Công nghệ sử dụng](#công-nghệ-sử-dụng)

---

## Giới thiệu

Dự án này được xây dựng theo **Clean Architecture** (còn gọi là Onion Architecture hoặc Hexagonal Architecture) của Robert C. Martin (Uncle Bob). Mục tiêu chính:

- **Độc lập với Framework**: Business logic không phụ thuộc vào framework cụ thể
- **Testable**: Business rules có thể được test độc lập
- **Độc lập với UI**: UI có thể thay đổi mà không ảnh hưởng đến business logic
- **Độc lập với Database**: Business rules không bị ràng buộc với database cụ thể
- **Độc lập với External Services**: Business logic không biết đến các service bên ngoài

---

## Cấu trúc Solution

```
src/Monolith/
├── ClassifiedAds.Domain/              # 🎯 Core - Domain Layer
├── ClassifiedAds.Application/          # 📋 Core - Application Layer  
├── ClassifiedAds.CrossCuttingConcerns/ # 🔧 Shared utilities
├── ClassifiedAds.Persistence/          # 💾 Infrastructure - Data Access
├── ClassifiedAds.Infrastructure/       # 🔌 Infrastructure - External Services
├── ClassifiedAds.WebAPI/              # 🌐 Presentation - REST API
├── ClassifiedAds.Background/           # ⚙️ Background Workers
├── ClassifiedAds.BlazorServerSide/     # 🖥️ Presentation - Blazor Server
├── ClassifiedAds.BlazorWebAssembly/    # 🌍 Presentation - Blazor WASM
└── ClassifiedAds.Blazor.Modules/       # 📦 Shared Blazor Components
```

---

## Dependency Rule

**Quy tắc quan trọng nhất**: Dependencies chỉ có thể trỏ **vào trong** (inward), không bao giờ trỏ ra ngoài.

```
┌─────────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                           │
│   WebAPI, Blazor Server, Blazor WASM, Background Workers        │
└───────────────────────────────┬─────────────────────────────────┘
                                │ depends on
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    INFRASTRUCTURE LAYER                         │
│        Persistence, Infrastructure, CrossCuttingConcerns        │
└───────────────────────────────┬─────────────────────────────────┘
                                │ depends on
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    APPLICATION LAYER                            │
│         Commands, Queries, Services, DTOs, Handlers             │
└───────────────────────────────┬─────────────────────────────────┘
                                │ depends on
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DOMAIN LAYER                               │
│        Entities, Value Objects, Domain Events, Interfaces       │
└─────────────────────────────────────────────────────────────────┘
```

### Dependency Matrix

| Layer | Có thể tham chiếu |
|-------|-------------------|
| Domain | Không tham chiếu layer nào |
| Application | Domain |
| Infrastructure | Domain, Application, CrossCuttingConcerns |
| Persistence | Domain, Application, CrossCuttingConcerns |
| WebAPI | Tất cả các layer |
| Background | Tất cả các layer |

---

## Layer Diagram

```
                    ┌──────────────────────────────────────┐
                    │           External World             │
                    │  (HTTP Clients, Message Bus, etc.)   │
                    └──────────────────┬───────────────────┘
                                       │
                    ┌──────────────────▼───────────────────┐
                    │       Presentation Layer             │
                    │  ┌─────────────────────────────────┐ │
                    │  │  Controllers / Endpoints        │ │
                    │  │  • ProductsController           │ │
                    │  │  • UsersController              │ │
                    │  │  • FilesController              │ │
                    │  └─────────────┬───────────────────┘ │
                    └────────────────┼─────────────────────┘
                                     │ uses Dispatcher
                    ┌────────────────▼─────────────────────┐
                    │        Application Layer             │
                    │  ┌─────────────────────────────────┐ │
                    │  │  CQRS Pattern                   │ │
                    │  │  • Commands + CommandHandlers   │ │
                    │  │  • Queries + QueryHandlers      │ │
                    │  │  • Decorators (AuditLog, Retry) │ │
                    │  └─────────────┬───────────────────┘ │
                    │  ┌─────────────▼───────────────────┐ │
                    │  │  Application Services           │ │
                    │  │  • CrudService<T>               │ │
                    │  │  • ProductService               │ │
                    │  │  • UserService                  │ │
                    │  └─────────────┬───────────────────┘ │
                    └────────────────┼─────────────────────┘
                                     │ uses Repository
    ┌────────────────────────────────┼────────────────────────────────┐
    │                                ▼                                │
    │  ┌─────────────────────────────────────────────────────────┐   │
    │  │                   Domain Layer                          │   │
    │  │  ┌───────────────┐ ┌───────────────┐ ┌───────────────┐  │   │
    │  │  │   Entities    │ │ Value Objects │ │ Domain Events │  │   │
    │  │  │  • Product    │ │               │ │ • Created     │  │   │
    │  │  │  • User       │ │               │ │ • Updated     │  │   │
    │  │  │  • FileEntry  │ │               │ │ • Deleted     │  │   │
    │  │  └───────────────┘ └───────────────┘ └───────────────┘  │   │
    │  │  ┌────────────────────────────────────────────────────┐ │   │
    │  │  │              Repository Interfaces                 │ │   │
    │  │  │  • IRepository<T, TKey>                           │ │   │
    │  │  │  • IUnitOfWork                                    │ │   │
    │  │  └────────────────────────────────────────────────────┘ │   │
    │  └─────────────────────────────────────────────────────────┘   │
    │                                                                 │
    │                    Infrastructure Layer                         │
    │  ┌─────────────────────────────────────────────────────────┐   │
    │  │  Persistence              │  Infrastructure            │   │
    │  │  • AdsDbContext           │  • Message Bus (RabbitMQ)  │   │
    │  │  • Repository<T>          │  • File Storage (Azure)    │   │
    │  │  • DbConfigurations       │  • Email/SMS Services      │   │
    │  │  • Interceptors           │  • AI Services             │   │
    │  └─────────────────────────────────────────────────────────┘   │
    └─────────────────────────────────────────────────────────────────┘
```

---

## Công nghệ sử dụng

### Core Framework
| Công nghệ | Phiên bản | Mục đích |
|-----------|-----------|----------|
| .NET | 10.0 | Runtime framework |
| ASP.NET Core | 10.0 | Web framework |
| Entity Framework Core | 10.0 | ORM |

### Database & Storage
| Công nghệ | Mục đích |
|-----------|----------|
| PostgreSQL | Primary database |
| Pgvector | Vector embeddings for AI search |
| Azure Blob Storage | File storage |
| Local File System | Alternative file storage |

### Messaging & Background Jobs
| Công nghệ | Mục đích |
|-----------|----------|
| RabbitMQ | Message queue |
| Azure Service Bus | Cloud message queue |
| Apache Kafka | Event streaming |
| Background Services | Scheduled jobs |

### Authentication & Security
| Công nghệ | Mục đích |
|-----------|----------|
| IdentityServer | OAuth 2.0 / OpenID Connect |
| JWT | Token-based authentication |
| Data Protection | Key management |

### Monitoring & Logging
| Công nghệ | Mục đích |
|-----------|----------|
| OpenTelemetry | Distributed tracing |
| Serilog | Structured logging |
| Health Checks | Application health monitoring |

### Documentation
| Công nghệ | Mục đích |
|-----------|----------|
| Scalar | API documentation |
| OpenAPI | API specification |

---

## Các tài liệu khác

1. [Domain Layer](./01-DOMAIN-LAYER.md) - Entities, Repositories, Domain Events
2. [Application Layer](./02-APPLICATION-LAYER.md) - CQRS, Services, Handlers
3. [Infrastructure Layer](./03-INFRASTRUCTURE-LAYER.md) - External Services
4. [Persistence Layer](./04-PERSISTENCE-LAYER.md) - Database, EF Core
5. [WebAPI Layer](./05-WEBAPI-LAYER.md) - Controllers, Endpoints
6. [AI Agent Rules](./06-AI-AGENT-RULES.md) - Strict rules for AI implementation
7. [Best Practices & SOLID](./07-BEST-PRACTICES-SOLID.md) - Coding standards
8. [N+1 Query Prevention](./08-N+1-QUERY-PREVENTION.md) - Query optimization
