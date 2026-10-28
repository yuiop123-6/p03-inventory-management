using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Tests;

/// <summary>共享测试基建：InMemory DbContext、种子导入服务构造。</summary>
public static class TestHost
{
    public static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"p03-tests-{Guid.NewGuid():N}")
            .Options);

    /// <summary>定位仓库根下的 seed-data 目录（从测试输出目录向上查找）。</summary>
    public static string FindSeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "seed-data");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("seed-data directory not found.");
    }

    public static SeedImportService CreateSeedImporter(AppDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedData:Directory"] = FindSeedDirectory(),
            })
            .Build();

        return new SeedImportService(db, configuration, new TestWebEnvironment(), NullLogger<SeedImportService>.Instance);
    }

    public static long CreateCategory(AppDbContext db, string name, string? description = null)
    {
        var category = new P03.Inventory.Api.Data.Entities.Category { Name = name, Description = description };
        db.Categories.Add(category);
        db.SaveChanges();
        return category.Id;
    }

    public static long CreateProduct(AppDbContext db, long categoryId, string code, string name,
        decimal price = 10m, int stock = 0, int minStock = 0)
    {
        var product = new P03.Inventory.Api.Data.Entities.Product
        {
            ProductCode = code,
            Name = name,
            CategoryId = categoryId,
            UnitPrice = price,
            CurrentStock = stock,
            MinimumStockLevel = minStock,
            CreatedAt = DateTime.UtcNow,
        };
        db.Products.Add(product);
        db.SaveChanges();
        return product.Id;
    }
}

public class TestWebEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "P03.Inventory.Tests";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string EnvironmentName { get; set; } = "Development";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
}
