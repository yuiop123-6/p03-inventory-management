using System.ComponentModel.DataAnnotations;

namespace P03.Inventory.Api.Dtos;

public record CategoryDto(long Id, string Name, string? Description);

public class CreateCategoryRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }
}

public class UpdateCategoryRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }
}
