using System.ComponentModel.DataAnnotations;

namespace P03.Inventory.Api.Dtos;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}

public record UserInfo(string Id, string UserName, IReadOnlyList<string> Roles);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, UserInfo User);
