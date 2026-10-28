using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

public class SeedImportServiceTests
{
    [Fact]
    public async Task Import_FreshDb_MatchesExpectedBaseline()
    {
        var db = TestHost.CreateDb();
        var importer = TestHost.CreateSeedImporter(db);

        var summary = await importer.ImportAsync();

        Assert.True(summary.MatchesExpectedBaseline);
        Assert.Equal(9, summary.TotalProducts);
        Assert.Equal(129, summary.TotalStockQuantity);
        Assert.Equal(4, summary.LowStockProducts);
        Assert.Equal(2, summary.OutOfStockProducts);
    }

    [Fact]
    public async Task Import_Twice_IsIdempotent_NoDuplicateRows()
    {
        var db = TestHost.CreateDb();
        var importer = TestHost.CreateSeedImporter(db);

        var first = await importer.ImportAsync();
        var second = await importer.ImportAsync();

        Assert.Equal(first.TotalProducts, second.TotalProducts);
        Assert.Equal(9, await db.Products.CountAsync());
        Assert.Equal(3, await db.Categories.CountAsync());
        Assert.Equal(9, db.Products.Select(p => p.ProductCode).Distinct().Count());
        Assert.Equal(129, second.TotalStockQuantity);
    }

    [Fact]
    public async Task Import_SecondRun_DoesNotModifyExistingStock()
    {
        var db = TestHost.CreateDb();
        var importer = TestHost.CreateSeedImporter(db);
        await importer.ImportAsync();

        // 模拟业务已改变库存（如 B 的库存事务扣减）
        var p001 = await db.Products.SingleAsync(p => p.ProductCode == "P001");
        p001.CurrentStock = 12;
        await db.SaveChangesAsync();

        await importer.ImportAsync();

        Assert.Equal(12, (await db.Products.SingleAsync(p => p.ProductCode == "P001")).CurrentStock);
    }

    [Fact]
    public async Task Import_WithExistingTransactions_Throws()
    {
        var db = TestHost.CreateDb();
        var importer = TestHost.CreateSeedImporter(db);
        await importer.ImportAsync();

        var product = await db.Products.FirstAsync();
        db.StockTransactions.Add(new P03.Inventory.Api.Data.Entities.StockTransaction
        {
            TransactionReference = "TX-000001",
            RequestId = Guid.NewGuid().ToString(),
            ProductId = product.Id,
            Type = "IN",
            Quantity = 1,
            PreviousStock = product.CurrentStock,
            NewStock = product.CurrentStock + 1,
            CreatedAt = DateTime.UtcNow,
            ActorUserId = "tester",
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync());
    }

    [Fact]
    public async Task Import_DuplicateNameDifferentCase_InFileOrDb_Skips()
    {
        var db = TestHost.CreateDb();
        // 预置同名（大小写不同）分类，导入应跳过而不重复插入
        TestHost.CreateCategory(db, "electronics");
        var importer = TestHost.CreateSeedImporter(db);

        await importer.ImportAsync();

        Assert.Equal(3, await db.Categories.CountAsync());
    }
}

public class TokenServiceTests
{
    [Fact]
    public void CreateToken_ContainsRoleClaims_AndExpiryAbout480Minutes()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:DurationMinutes"] = "480",
        }).Build();
        var service = new TokenService(configuration);
        var user = new AppUser { Id = "u-1", UserName = "admin" };

        var (token, expiresAtUtc) = service.CreateToken(user, new[] { "Admin" });

        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(token));
        var jwt = handler.ReadJwtToken(token);
        // TokenService 直接构造 JwtSecurityToken，claim 原样写入（无出站映射）：
        // 角色 = ClaimTypes.Role 完整 URI；用户 Id 同时存在于 "sub"
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "u-1");
        var minutes = (expiresAtUtc - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutes, 475, 481);
    }
}

public class StockStatusTests
{
    [Theory]
    [InlineData(0, 5, StockStatus.Out)]
    [InlineData(5, 5, StockStatus.Low)]   // 等于阈值属于低库存
    [InlineData(3, 5, StockStatus.Low)]
    [InlineData(6, 5, StockStatus.Normal)]
    public void Of_MatchesAcceptanceDefinitions(int stock, int minStock, string expected)
    {
        Assert.Equal(expected, StockStatus.Of(stock, minStock));
    }
}
