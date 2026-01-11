# 🔍 N+1 Query Prevention Guide

## Mục Lục
1. [N+1 Query Problem là gì?](#n1-query-problem-là-gì)
2. [Interceptors trong Project](#interceptors-trong-project)
3. [Các Pattern gây N+1](#các-pattern-gây-n1)
4. [Giải pháp](#giải-pháp)
5. [Best Practices](#best-practices)
6. [Checklist](#checklist)

---

## N+1 Query Problem là gì?

**N+1 Query Problem** xảy ra khi bạn thực hiện:
- 1 query để lấy N items
- N queries bổ sung để lấy related data cho mỗi item

### Ví dụ điển hình

```csharp
// ❌ N+1 Problem
var products = await _context.Products.ToListAsync();  // 1 query

foreach (var product in products)  // N iterations
{
    var category = await _context.Categories
        .FirstOrDefaultAsync(c => c.Id == product.CategoryId);  // N queries!
}
// Total: 1 + N queries (100 products = 101 queries!)
```

### Impact

| Products | Queries (N+1) | Queries (Optimized) |
|----------|---------------|---------------------|
| 10 | 11 | 1-2 |
| 100 | 101 | 1-2 |
| 1,000 | 1,001 | 1-2 |
| 10,000 | 10,001 | 1-2 |

---

## Interceptors trong Project

Project này có **2 interceptors** để detect N+1 và related issues:

### 1. SelectWithoutWhereCommandInterceptor

Phát hiện queries không có WHERE clause - có thể trả về quá nhiều data.

```csharp
// SelectWithoutWhereCommandInterceptor.cs
public class SelectWithoutWhereCommandInterceptor : DbCommandInterceptor
{
    private void CheckCommand(DbCommand command)
    {
        // Skip COUNT queries
        if (command.CommandText.Contains("SELECT COUNT(*)"))
            return;

        if (command.CommandText.Contains("SELECT"))
        {
            // Cho phép nếu có WHERE, OFFSET, hoặc FETCH
            if (command.CommandText.Contains("WHERE"))
                return;
            if (command.CommandText.Contains("OFFSET"))  // Pagination
                return;
            if (command.CommandText.Contains("FETCH"))
                return;

            // Log warning
            _logger.LogWarning(LOG_TEMPLATE, command.CommandText, stackTrace);
        }
    }
}
```

**Khi nào trigger:**
```csharp
// ⚠️ Triggers warning
var products = await _context.Products.ToListAsync();

// ✅ No warning - has WHERE
var products = await _context.Products.Where(x => x.IsActive).ToListAsync();

// ✅ No warning - has pagination
var products = await _context.Products.Skip(0).Take(10).ToListAsync();
```

### 2. SelectWhereInCommandInterceptor

Phát hiện `SELECT ... WHERE ... IN (...)` - thường là dấu hiệu của N+1.

```csharp
// SelectWhereInCommandInterceptor.cs
public class SelectWhereInCommandInterceptor : DbCommandInterceptor
{
    private void CheckCommand(DbCommand command)
    {
        bool selectWhereIn = query.Contains("SELECT")
            && query.Contains("WHERE")
            && query.Contains(" IN (");  // Detect IN clause

        if (selectWhereIn)
        {
            _logger.LogWarning(LOG_TEMPLATE, command.CommandText, stackTrace);
        }
    }
}
```

**Khi nào trigger:**
```csharp
// ⚠️ Triggers warning - thường là kết quả của lazy loading hoặc multiple queries
SELECT * FROM "Products" WHERE "Id" IN (@p0, @p1, @p2, @p3, @p4)
```

---

## Các Pattern gây N+1

### Pattern 1: Loop với Lazy Loading

```csharp
// ❌ N+1 Pattern
var orders = await _context.Orders.ToListAsync();  // Query 1

foreach (var order in orders)
{
    // Each access triggers a query!
    var customer = order.Customer;           // N queries
    var items = order.Items.ToList();        // N queries
}
```

### Pattern 2: Accessing Navigation Property sau Query

```csharp
// ❌ N+1 Pattern
var products = await _context.Products.ToListAsync();

foreach (var product in products)
{
    Console.WriteLine(product.Category.Name);  // N queries for Category!
}
```

### Pattern 3: LINQ Contains trong Loop

```csharp
// ❌ N+1 Pattern
foreach (var categoryId in categoryIds)
{
    var products = await _context.Products
        .Where(p => p.CategoryId == categoryId)
        .ToListAsync();  // N queries!
}
```

### Pattern 4: FirstOrDefault trong Loop

```csharp
// ❌ N+1 Pattern
var productIds = new List<Guid> { ... };

foreach (var id in productIds)
{
    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.Id == id);  // N queries!
}
```

### Pattern 5: Projection với Navigation Property

```csharp
// ❌ Có thể gây N+1 nếu lazy loading enabled
var products = await _context.Products
    .Select(p => new ProductDto
    {
        Name = p.Name,
        CategoryName = p.Category.Name  // N+1 nếu không eager load
    })
    .ToListAsync();
```

---

## Giải pháp

### Solution 1: Eager Loading với Include

```csharp
// ✅ Eager loading - Single query với JOIN
var orders = await _context.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items)
        .ThenInclude(i => i.Product)
    .ToListAsync();

// Generated SQL:
// SELECT * FROM "Orders"
// LEFT JOIN "Customers" ON ...
// LEFT JOIN "OrderItems" ON ...
// LEFT JOIN "Products" ON ...
```

### Solution 2: Explicit Loading (khi cần conditional)

```csharp
// ✅ Explicit loading khi cần thiết
var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

if (order.Status == OrderStatus.Completed)
{
    await _context.Entry(order)
        .Collection(o => o.Items)
        .LoadAsync();
}
```

### Solution 3: Projection/Select (Best Performance)

```csharp
// ✅ Projection - chỉ lấy data cần thiết
var products = await _context.Products
    .Select(p => new ProductDto
    {
        Id = p.Id,
        Name = p.Name,
        CategoryName = p.Category.Name,  // JOIN được tạo tự động
        ItemCount = p.Items.Count()       // Subquery
    })
    .ToListAsync();

// ✅ EF Core automatically joins
// No N+1 because it's a single query with JOIN
```

### Solution 4: Batch Loading

```csharp
// ✅ Load related data một lần
var products = await _context.Products.ToListAsync();

var productIds = products.Select(p => p.Id).ToList();

// Single query cho tất cả embeddings
var embeddings = await _context.ProductEmbeddings
    .Where(e => productIds.Contains(e.ProductId))
    .ToListAsync();

// Map trong memory
var embeddingsByProduct = embeddings.ToLookup(e => e.ProductId);

foreach (var product in products)
{
    var productEmbeddings = embeddingsByProduct[product.Id].ToList();
}
```

### Solution 5: Split Query (cho large includes)

```csharp
// ✅ Split query - multiple queries nhưng tránh Cartesian explosion
var orders = await _context.Orders
    .Include(o => o.Items)
    .Include(o => o.Payments)
    .AsSplitQuery()  // Splits into multiple queries
    .ToListAsync();

// Generated: 3 separate queries
// SELECT * FROM "Orders"
// SELECT * FROM "OrderItems" WHERE "OrderId" IN (...)
// SELECT * FROM "Payments" WHERE "OrderId" IN (...)
```

### Solution 6: Join thủ công

```csharp
// ✅ Explicit join
var query = from product in _context.Products
            join category in _context.Categories on product.CategoryId equals category.Id
            select new ProductWithCategoryDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CategoryName = category.Name
            };

var results = await query.ToListAsync();
```

---

## Best Practices

### 1. Luôn dùng Projection cho Queries

```csharp
// ❌ Return full entity
public async Task<List<Product>> GetProductsAsync()
{
    return await _repository.ToListAsync(_repository.GetQueryableSet());
}

// ✅ Return projection/DTO
public async Task<List<ProductDto>> GetProductsAsync()
{
    return await _repository.ToListAsync(
        _repository.GetQueryableSet()
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category.Name
            }));
}
```

### 2. Include chỉ khi cần

```csharp
// ❌ Over-fetching
var product = await _context.Products
    .Include(p => p.Category)
    .Include(p => p.Reviews)
    .Include(p => p.Images)
    .Include(p => p.Variants)
    .FirstOrDefaultAsync(p => p.Id == id);

// ✅ Include chỉ data cần thiết
var product = await _context.Products
    .Include(p => p.Category)  // Chỉ cần category
    .FirstOrDefaultAsync(p => p.Id == id);
```

### 3. Sử dụng AsNoTracking cho Read-only

```csharp
// ✅ No change tracking = better performance
var products = await _context.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToListAsync();
```

### 4. Pagination cho List Queries

```csharp
// ✅ Always paginate large result sets
public async Task<PagedResult<ProductDto>> GetProductsAsync(int page, int pageSize)
{
    var query = _repository.GetQueryableSet()
        .Where(p => p.IsActive);

    var totalCount = await query.CountAsync();

    var items = await query
        .OrderBy(p => p.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(p => new ProductDto { ... })
        .ToListAsync();

    return new PagedResult<ProductDto>
    {
        Items = items,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize
    };
}
```

### 5. Preload Data cho Loops

```csharp
// ✅ Preload before loop
var productIds = orders.SelectMany(o => o.Items.Select(i => i.ProductId)).Distinct().ToList();
var products = await _context.Products
    .Where(p => productIds.Contains(p.Id))
    .ToDictionaryAsync(p => p.Id);

foreach (var order in orders)
{
    foreach (var item in order.Items)
    {
        var product = products[item.ProductId];  // Memory lookup, no query
    }
}
```

---

## Vector Search N+1 Prevention

Đặc biệt với pgvector, cần cẩn thận với N+1:

```csharp
// ❌ N+1 với vector search
var embedding = new Vector(searchVector);
var similarProducts = await _context.ProductEmbeddings
    .OrderBy(e => e.Embedding.CosineDistance(embedding))
    .Take(5)
    .ToListAsync();

foreach (var pe in similarProducts)
{
    var product = await _context.Products.FindAsync(pe.ProductId);  // N+1!
}

// ✅ Include Product trong query
var similarProducts = await _context.ProductEmbeddings
    .Include(e => e.Product)  // Eager load
    .OrderBy(e => e.Embedding.CosineDistance(embedding))
    .Take(5)
    .ToListAsync();

// ✅ Hoặc dùng projection
var similarProducts = await _context.ProductEmbeddings
    .OrderBy(e => e.Embedding.CosineDistance(embedding))
    .Take(5)
    .Select(e => new ProductSearchResult
    {
        ProductId = e.Product.Id,
        ProductName = e.Product.Name,
        SimilarityScore = e.Embedding.CosineDistance(embedding)
    })
    .ToListAsync();
```

---

## Checklist

### Trước khi viết Query

- [ ] Xác định data nào cần lấy
- [ ] Xác định relationships nào cần include
- [ ] Có cần pagination không?
- [ ] Có thể dùng projection thay vì full entity không?

### Khi Review Code

- [ ] Có query trong loop không?
- [ ] Có truy cập navigation property sau khi query không?
- [ ] Include có phù hợp không (không over-fetch)?
- [ ] Có AsNoTracking cho read-only queries không?

### Khi Test

- [ ] Log SQL queries để kiểm tra số lượng
- [ ] Test với data lớn (100+, 1000+ records)
- [ ] Check interceptor warnings trong logs

---

## Summary Table

| Problem | Solution |
|---------|----------|
| Loop with lazy loading | Eager loading với Include |
| Need conditional loading | Explicit loading |
| Only need some fields | Projection với Select |
| Large related collections | Split Query |
| Loop with individual queries | Batch loading |
| Full table scan | Add WHERE clause + pagination |
| Multiple nested includes | Projection hoặc AsSplitQuery |
| Vector search N+1 | Include trong query hoặc projection |

---

## Quick Reference

```csharp
// ✅ Patterns to USE
.Include(x => x.Related)           // Eager loading
.ThenInclude(x => x.Nested)        // Nested eager loading
.Select(x => new Dto { ... })      // Projection
.AsNoTracking()                    // Read-only
.AsSplitQuery()                    // Split large includes
.Skip().Take()                     // Pagination

// ❌ Patterns to AVOID
foreach (var x in items) {
    await query.FirstAsync(...);   // N+1!
}
x.NavigationProperty               // Lazy loading in loop
.ToList() without WHERE            // Full table scan
```
