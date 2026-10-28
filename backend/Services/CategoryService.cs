using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Dtos;

namespace P03.Inventory.Api.Services;

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(long id, UpdateCategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public async Task<List<CategoryDto>> GetAllAsync(CancellationToken ct = default)
        => await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var name = NormalizeName(request.Name);
        await EnsureNameAvailableAsync(name, excludeId: null, ct);

        var category = new Category { Name = name, Description = request.Description?.Trim() };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Description);
    }

    public async Task<CategoryDto> UpdateAsync(long id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiException.NotFound($"Category {id} does not exist.");

        var name = NormalizeName(request.Name);
        await EnsureNameAvailableAsync(name, excludeId: id, ct);

        category.Name = name;
        category.Description = request.Description?.Trim();
        await _db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Description);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiException.NotFound($"Category {id} does not exist.");

        var hasProducts = await _db.Products.AnyAsync(p => p.CategoryId == id, ct);
        if (hasProducts)
        {
            throw ApiException.Referential("Category has associated products and cannot be deleted.",
                new { categoryId = id });
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(ct);
    }

    private static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw ApiException.Validation("Category name is required.");
        return trimmed;
    }

    private async Task EnsureNameAvailableAsync(string name, long? excludeId, CancellationToken ct)
    {
        var upper = name.ToUpperInvariant();
        var duplicated = await _db.Categories.AsNoTracking()
            .AnyAsync(c => c.Name.ToUpper() == upper && (excludeId == null || c.Id != excludeId), ct);
        if (duplicated)
            throw ApiException.Duplicate("Category name already exists.", new { name });
    }
}
