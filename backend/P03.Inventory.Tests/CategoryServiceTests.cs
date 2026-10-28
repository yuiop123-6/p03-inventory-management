using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

public class CategoryServiceTests
{
    private static CategoryService CreateService(out AppDbContext db)
    {
        db = TestHost.CreateDb();
        return new CategoryService(db);
    }

    [Fact]
    public async Task Create_TrimsName_AndReturnsDto()
    {
        var service = CreateService(out var db);
        var dto = await service.CreateAsync(new CreateCategoryRequest { Name = "  Electronics  " });
        Assert.Equal("Electronics", dto.Name);
    }

    [Fact]
    public async Task Create_DuplicateNameCaseInsensitive_Throws409Duplicate()
    {
        var service = CreateService(out var db);
        await service.CreateAsync(new CreateCategoryRequest { Name = "Electronics" });

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(new CreateCategoryRequest { Name = "  electronics " }));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.DuplicateValue, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_Throws400Validation(string name)
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(new CreateCategoryRequest { Name = name }));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
    }

    [Fact]
    public async Task Update_NotFound_Throws404()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.UpdateAsync(999, new UpdateCategoryRequest { Name = "X" }));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Update_ToDuplicateName_Throws409()
    {
        var service = CreateService(out var db);
        var a = await service.CreateAsync(new CreateCategoryRequest { Name = "Alpha" });
        var b = await service.CreateAsync(new CreateCategoryRequest { Name = "Beta" });

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.UpdateAsync(b.Id, new UpdateCategoryRequest { Name = "ALPHA" }));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Beta", (await db.Categories.AsNoTracking().SingleAsync(c => c.Id == b.Id)).Name);
    }

    [Fact]
    public async Task Delete_NotFound_Throws404()
    {
        var service = CreateService(out _);
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.DeleteAsync(123));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Delete_WithProducts_Throws409Referential()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        TestHost.CreateProduct(db, categoryId, "P001", "Mouse");

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.DeleteAsync(categoryId));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.ReferentialConflict, ex.Code);
        Assert.NotNull(await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId));
    }

    [Fact]
    public async Task Delete_EmptyCategory_Succeeds()
    {
        var service = CreateService(out var db);
        var categoryId = TestHost.CreateCategory(db, "Empty");
        await service.DeleteAsync(categoryId);
        Assert.Null(await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId));
    }

    [Fact]
    public async Task GetAll_ReturnsItemsOrderedById()
    {
        var service = CreateService(out var db);
        TestHost.CreateCategory(db, "C");
        TestHost.CreateCategory(db, "A");

        var list = await service.GetAllAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("C", list[0].Name);
    }
}
