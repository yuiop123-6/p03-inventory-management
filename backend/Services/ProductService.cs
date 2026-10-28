using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Dtos;

namespace P03.Inventory.Api.Services;

public interface IProductService
{
    Task<PagedResult<ProductDto>> SearchAsync(string? search, long? categoryId, string? stockStatus,
        int page, int pageSize, CancellationToken ct = default);

    Task<ProductDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(long id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public class ProductService : IProductService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> ValidStockStatus = new(StringComparer.OrdinalIgnoreCase)
    { StockStatus.All, StockStatus.Normal, StockStatus.Low, StockStatus.Out };

    private readonly AppDbContext _db;

    public ProductService(AppDbContext db) => _db = db;

    public async Task<PagedResult<ProductDto>> SearchAsync(string? search, long? categoryId, string? stockStatus,
        int page, int pageSize, CancellationToken ct = default)
    {
        var status = stockStatus?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(status) && !ValidStockStatus.Contains(status))
            throw ApiException.Validation(
                $"stockStatus must be one of: {StockStatus.All}, {StockStatus.Normal}, {StockStatus.Low}, {StockStatus.Out}.",
                new { stockStatus });

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(p => p.ProductCode.Contains(keyword) || p.Name.Contains(keyword));
        }

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        switch (status)
        {
            case StockStatus.Out:
                query = query.Where(p => p.CurrentStock == 0);
                break;
            case StockStatus.Low:
                query = query.Where(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel);
                break;
            case StockStatus.Normal:
                query = query.Where(p => p.CurrentStock > p.MinimumStockLevel);
                break;
        }

        var total = await query.CountAsync(ct);

        // 按 Id 升序稳定分页（契约要求）
        var items = await query
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => Map(p, p.Category.Name))
            .ToListAsync(ct);

        return PagedResult<ProductDto>.Create(items, total, page, pageSize);
    }

    public async Task<ProductDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => Map(p, p.Category.Name))
            .FirstOrDefaultAsync(ct);

        return product ?? throw ApiException.NotFound($"Product {id} does not exist.");
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.ProductCode);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw ApiException.Validation("Product name is required.");

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw ApiException.Validation($"CategoryId {request.CategoryId} does not exist.");

        var duplicated = await _db.Products.AsNoTracking().AnyAsync(p => p.ProductCode == code, ct);
        if (duplicated)
            throw ApiException.Duplicate("Product code already exists.", new { productCode = code });

        // 新商品的初始库存作为种子/期初值，不产生库存交易
        var product = new Product
        {
            ProductCode = code,
            Name = name,
            Description = request.Description?.Trim(),
            CategoryId = category.Id,
            Category = category,
            UnitPrice = request.UnitPrice,
            CurrentStock = request.CurrentStock,
            MinimumStockLevel = request.MinimumStockLevel,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return Map(product, category.Name);
    }

    public async Task<ProductDto> UpdateAsync(long id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await _db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw ApiException.NotFound($"Product {id} does not exist.");

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw ApiException.Validation("Product name is required.");

        if (product.CategoryId != request.CategoryId)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
                ?? throw ApiException.Validation($"CategoryId {request.CategoryId} does not exist.");
            product.Category = category;
        }

        // productCode / currentStock / createdAt 不可更改（控制器已拒绝请求中出现这些字段）
        product.Name = name;
        product.Description = request.Description?.Trim();
        product.UnitPrice = request.UnitPrice;
        product.MinimumStockLevel = request.MinimumStockLevel;
        await _db.SaveChangesAsync(ct);
        return Map(product, product.Category.Name);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw ApiException.NotFound($"Product {id} does not exist.");

        var hasTransactions = await _db.StockTransactions.AnyAsync(t => t.ProductId == id, ct);
        if (hasTransactions)
            throw ApiException.Referential("Product has stock transactions and cannot be deleted.",
                new { productId = id });

        if (product.CurrentStock > 0)
            throw ApiException.Referential("Product still has stock and cannot be deleted.",
                new { productId = id, currentStock = product.CurrentStock });

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct);
    }

    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw ApiException.Validation("Product code is required.");
        return normalized;
    }

    internal static ProductDto Map(Product p, string categoryName)
        => new(
            p.Id,
            p.ProductCode,
            p.Name,
            p.Description,
            p.CategoryId,
            categoryName,
            p.UnitPrice,
            p.CurrentStock,
            p.MinimumStockLevel,
            DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
            StockStatus.Of(p.CurrentStock, p.MinimumStockLevel));
}
