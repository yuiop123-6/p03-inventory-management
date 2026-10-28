using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

public class ProductServiceTests
{
    private static ProductService CreateService(out AppDbContext db)
    {
        db = TestHost.CreateDb();
        return new ProductService(db);
    }

    private static CreateProductRequest ValidRequest(long categoryId = 1, string code = "P001") => new()
    {
        ProductCode = code,
        Name = "Wireless Mouse",
        CategoryId = categoryId,
        UnitPrice = 29.90m,
        CurrentStock = 35,
        MinimumStockLevel = 10,
    };

    [Fact]
    public async Task Create_NormalizesCode_TrimAndUpper()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");

        var dto = await service.CreateAsync(ValidRequest(categoryId, code: "  p001  "));

        Assert.Equal("P001", dto.ProductCode);
    }

    [Fact]
    public async Task Create_DuplicateCodeCaseInsensitive_Throws409()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        await service.CreateAsync(ValidRequest(categoryId, code: "P001"));

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(categoryId, code: "p001")));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.DuplicateValue, ex.Code);
    }

    [Fact]
    public async Task Create_UnknownCategory_Throws400()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(ValidRequest(categoryId: 42)));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
    }

    [Fact]
    public async Task Create_InitialStock_DoesNotProduceTransaction()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");

        var dto = await service.CreateAsync(ValidRequest(categoryId));

        Assert.Equal(35, dto.CurrentStock);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Search_FiltersByStockStatus_LowOutNormal_AreMutuallyExclusive()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        // 0 = out；0 < stock <= min = low；stock > min = normal；等于阈值属于低库存
        TestHost.CreateProduct(db, categoryId, "P001", "OutItem", stock: 0, minStock: 5);
        TestHost.CreateProduct(db, categoryId, "P002", "LowBoundary", stock: 5, minStock: 5);
        TestHost.CreateProduct(db, categoryId, "P003", "NormalItem", stock: 9, minStock: 5);

        Assert.Equal(1, (await service.SearchAsync(null, null, StockStatus.Out, 1, 20)).Total);
        Assert.Equal(1, (await service.SearchAsync(null, null, StockStatus.Low, 1, 20)).Total);
        Assert.Equal(1, (await service.SearchAsync(null, null, StockStatus.Normal, 1, 20)).Total);
    }

    [Fact]
    public async Task Search_FiltersBySearchAndCategory()
    {
        var service = CreateService(out var db);
        var catA = TestHost.CreateCategory(db, "A");
        var catB = TestHost.CreateCategory(db, "B");
        TestHost.CreateProduct(db, catA, "P001", "Keyboard");
        TestHost.CreateProduct(db, catB, "P002", "Mouse");

        // InMemory 提供程序按 C# 大小写敏感语义执行 Contains（SQL Server 上为 CI 排序规则，不区分大小写）
        Assert.Equal(1, (await service.SearchAsync("board", null, null, 1, 20)).Total);
        Assert.Equal(1, (await service.SearchAsync(null, catB, null, 1, 20)).Total);
        Assert.Equal("P002", (await service.SearchAsync(null, catB, null, 1, 20)).Items[0].ProductCode);
    }

    [Fact]
    public async Task Search_InvalidStockStatus_Throws400()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.SearchAsync(null, null, "bogus", 1, 20));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Search_PagingOrderedById_Stable()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        for (var i = 1; i <= 9; i++)
        {
            TestHost.CreateProduct(db, categoryId, $"P{i:000}", $"Item {i}", stock: 1, minStock: 0);
        }

        var page1 = await service.SearchAsync(null, null, StockStatus.All, 1, 4);
        var page1Again = await service.SearchAsync(null, null, StockStatus.All, 1, 4);
        var page2 = await service.SearchAsync(null, null, StockStatus.All, 2, 4);
        var page3 = await service.SearchAsync(null, null, StockStatus.All, 3, 4);

        Assert.Equal(9, page1.Total);
        Assert.Equal(4, page1.Items.Count);
        Assert.Equal(page1.Items[0].Id, page1Again.Items[0].Id); // 稳定排序
        Assert.Equal(1, page1.Items[0].Id);
        Assert.Equal(5, page2.Items[0].Id);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task GetById_NotFound_Throws404()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.GetByIdAsync(999));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesAllowedFields_KeepsCodeAndStock()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var newCategory = TestHost.CreateCategory(db, "Furniture");
        var product = await service.CreateAsync(ValidRequest(categoryId));

        var updated = await service.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = "Updated Mouse",
            Description = "new desc",
            CategoryId = newCategory,
            UnitPrice = 33.30m,
            MinimumStockLevel = 3,
        });

        Assert.Equal("Updated Mouse", updated.Name);
        Assert.Equal(newCategory, updated.CategoryId);
        Assert.Equal(33.30m, updated.UnitPrice);
        Assert.Equal(3, updated.MinimumStockLevel);
        var entity = await db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.Equal("P001", entity.ProductCode);   // 编号不可变
        Assert.Equal(35, entity.CurrentStock);      // 库存编辑接口不可修改
    }

    [Fact]
    public async Task Update_NotFound_Throws404()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.UpdateAsync(999, new UpdateProductRequest { Name = "X", CategoryId = 1 }));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Delete_WithTransactions_Throws409()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = TestHost.CreateProduct(db, categoryId, "P001", "Mouse", stock: 0);
        db.StockTransactions.Add(new StockTransaction
        {
            TransactionReference = "TX-000001",
            RequestId = Guid.NewGuid().ToString(),
            ProductId = productId,
            Type = "IN",
            Quantity = 5,
            PreviousStock = 0,
            NewStock = 5,
            CreatedAt = DateTime.UtcNow,
            ActorUserId = "tester",
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.DeleteAsync(productId));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.ReferentialConflict, ex.Code);
    }

    [Fact]
    public async Task Delete_WithPositiveStock_Throws409()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = TestHost.CreateProduct(db, categoryId, "P001", "Mouse", stock: 3);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.DeleteAsync(productId));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.ReferentialConflict, ex.Code);
    }

    [Fact]
    public async Task Delete_ZeroStockNoTransaction_Succeeds()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = TestHost.CreateProduct(db, categoryId, "P001", "Mouse", stock: 0);

        await service.DeleteAsync(productId);

        Assert.Null(await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId));
    }

    [Fact]
    public async Task Delete_NotFound_Throws404()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.DeleteAsync(999));
        Assert.Equal(404, ex.StatusCode);
    }
}
