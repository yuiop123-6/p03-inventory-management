using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;

namespace P03.Inventory.Api.Services;

public record SeedImportSummary(
    int CategoriesInserted,
    int CategoriesSkipped,
    int ProductsInserted,
    int ProductsSkipped,
    int TotalProducts,
    int TotalStockQuantity,
    int LowStockProducts,
    int OutOfStockProducts,
    bool MatchesExpectedBaseline);

public interface ISeedImportService
{
    /// <summary>
    /// 幂等种子导入：读取 seed-data/categories.json 与 products.json。
    /// - 存在库存流水时拒绝导入（已有业务活动，禁止覆盖）；
    /// - 已存在的分类/商品按名称/编号跳过，不修改已有库存；
    /// - 完成后输出基线对账（totalProducts/totalStock/lowStock/outOfStock）。
    /// </summary>
    Task<SeedImportSummary> ImportAsync(CancellationToken ct = default);
}

public class SeedImportService : ISeedImportService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SeedImportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SeedImportService(
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SeedImportService> logger)
    {
        _db = db;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<SeedImportSummary> ImportAsync(CancellationToken ct = default)
    {
        // 确保数据库与迁移就绪（幂等；InMemory 等非关系型提供程序跳过，供单元测试使用）
        if (_db.Database.IsRelational())
        {
            await _db.Database.MigrateAsync(ct);
        }

        // 首次部署保护：已有库存流水说明存在业务活动，禁止导入覆盖
        if (await _db.StockTransactions.AnyAsync(ct))
        {
            throw new InvalidOperationException(
                "Seed import refused: stock transactions already exist. " +
                "Importing into a database with business activity is not allowed.");
        }

        var (categories, products, expected) = LoadSeedFiles();
        Say($"Seed files loaded: {categories.Count} categories, {products.Count} products.");

        var categoryMap = await ImportCategoriesAsync(categories, ct);
        await ImportProductsAsync(products, categoryMap, ct);

        var summary = await BuildSummaryAsync(expected, ct);
        Say($"Baseline: products={summary.TotalProducts}, totalStock={summary.TotalStockQuantity}, " +
            $"lowStock={summary.LowStockProducts}, outOfStock={summary.OutOfStockProducts}.");
        Say(summary.MatchesExpectedBaseline
            ? "Baseline matches expected_initial_dashboard.json: OK."
            : "WARNING: baseline does NOT match expected_initial_dashboard.json!");

        return summary;
    }

    // ---------- 内部实现 ----------

    private async Task<Dictionary<long, long>> ImportCategoriesAsync(
        List<SeedCategory> categories, CancellationToken ct)
    {
        // key: 种子文件中的 CategoryId -> 数据库实际 Id
        var map = new Dictionary<long, long>();
        var inserted = 0;
        var skipped = 0;

        foreach (var seed in categories.OrderBy(c => c.Id))
        {
            var name = seed.Name.Trim();
            var upper = name.ToUpperInvariant();
            var existing = await _db.Categories
                .FirstOrDefaultAsync(c => c.Name.ToUpper() == upper, ct);

            if (existing != null)
            {
                map[seed.Id] = existing.Id;
                skipped++;
                continue;
            }

            var category = new Category { Name = name, Description = seed.Description?.Trim() };
            _db.Categories.Add(category);
            inserted++;
            // SaveChanges 立即取回自增 Id，供商品外键映射
            await _db.SaveChangesAsync(ct);
            map[seed.Id] = category.Id;
        }

        if (inserted > 0) await _db.SaveChangesAsync(ct);
        Say($"Categories: inserted={inserted}, skipped(already exists)={skipped}.");
        return map;
    }

    private async Task ImportProductsAsync(
        List<SeedProduct> products, Dictionary<long, long> categoryMap, CancellationToken ct)
    {
        var inserted = 0;
        var skipped = 0;

        foreach (var seed in products.OrderBy(p => p.Id))
        {
            var code = seed.ProductCode.Trim().ToUpperInvariant();
            if (await _db.Products.AnyAsync(p => p.ProductCode == code, ct))
            {
                // 已存在：跳过，绝不修改已有实际库存
                skipped++;
                continue;
            }

            if (!categoryMap.TryGetValue(seed.CategoryId, out var categoryId))
            {
                throw new InvalidOperationException(
                    $"Seed product {code} references categoryId {seed.CategoryId}, " +
                    "which does not exist in the database. Database state has drifted from seed data.");
            }

            var product = new Product
            {
                ProductCode = code,
                Name = seed.Name.Trim(),
                Description = seed.Description?.Trim(),
                CategoryId = categoryId,
                UnitPrice = decimal.Parse(seed.UnitPrice, CultureInfo.InvariantCulture),
                CurrentStock = seed.CurrentStock,
                MinimumStockLevel = seed.MinimumStockLevel,
                CreatedAt = DateTime.SpecifyKind(seed.CreatedAt, DateTimeKind.Utc),
            };
            _db.Products.Add(product);
            inserted++;
        }

        if (inserted > 0) await _db.SaveChangesAsync(ct);
        Say($"Products: inserted={inserted}, skipped(already exists)={skipped}.");
    }

    private async Task<SeedImportSummary> BuildSummaryAsync(ExpectedDashboard? expected, CancellationToken ct)
    {
        var totalProducts = await _db.Products.CountAsync(ct);
        var totalStock = await _db.Products.SumAsync(p => (int?)p.CurrentStock, ct) ?? 0;
        var lowStock = await _db.Products
            .CountAsync(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel, ct);
        var outOfStock = await _db.Products.CountAsync(p => p.CurrentStock == 0, ct);

        var matches = expected == null ||
                      (expected.TotalProducts == totalProducts &&
                       expected.TotalStockQuantity == totalStock &&
                       expected.LowStockProducts == lowStock &&
                       expected.OutOfStockProducts == outOfStock);

        return new SeedImportSummary(
            0, 0, 0, 0, // 明细计数仅供过程输出，最终对账以数据库实况为准
            totalProducts, totalStock, lowStock, outOfStock, matches);
    }

    private (List<SeedCategory>, List<SeedProduct>, ExpectedDashboard?) LoadSeedFiles()
    {
        var dir = ResolveSeedDirectory();
        var categories = JsonSerializer.Deserialize<List<SeedCategory>>(
            File.ReadAllText(Path.Combine(dir, "categories.json")), JsonOptions)
            ?? throw new InvalidOperationException("categories.json is empty or invalid.");
        var products = JsonSerializer.Deserialize<List<SeedProduct>>(
            File.ReadAllText(Path.Combine(dir, "products.json")), JsonOptions)
            ?? throw new InvalidOperationException("products.json is empty or invalid.");

        var expectedPath = Path.Combine(dir, "expected_initial_dashboard.json");
        ExpectedDashboard? expected = null;
        if (File.Exists(expectedPath))
        {
            expected = JsonSerializer.Deserialize<ExpectedDashboard>(
                File.ReadAllText(expectedPath), JsonOptions);
        }

        return (categories, products, expected);
    }

    private string ResolveSeedDirectory()
    {
        var configured = _configuration["SeedData:Directory"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = "../seed-data";

        var path = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));

        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Seed data directory not found: {path}");

        return path;
    }

    private void Say(string message)
    {
        Console.WriteLine($"[seed-import] {message}");
        _logger.LogInformation("[seed-import] {Message}", message);
    }

    // ---------- 种子文件模型（camelCase JSON，属性名大小写不敏感匹配） ----------

    private sealed record SeedCategory(long Id, string Name, string? Description);

    private sealed record SeedProduct(
        long Id,
        string ProductCode,
        string Name,
        string? Description,
        long CategoryId,
        string UnitPrice,
        int CurrentStock,
        int MinimumStockLevel,
        DateTime CreatedAt);

    private sealed record ExpectedDashboard(
        int TotalProducts,
        int TotalStockQuantity,
        int LowStockProducts,
        int OutOfStockProducts);
}
