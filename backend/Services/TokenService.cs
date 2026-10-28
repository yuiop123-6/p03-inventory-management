using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using P03.Inventory.Api.Data;

namespace P03.Inventory.Api.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user, IEnumerable<string> roles);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration) => _configuration = configuration;

    public (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user, IEnumerable<string> roles)
    {
        var issuer = _configuration["Jwt:Issuer"] ?? "P03.Inventory.Api";
        var audience = _configuration["Jwt:Audience"] ?? "P03.Inventory.Client";
        var durationMinutes = _configuration.GetValue("Jwt:DurationMinutes", 480);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(durationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(ResolveSigningKey(_configuration), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expiresAtUtc, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    /// <summary>解析签名密钥：生产必须注入 Jwt:Key（环境变量 Jwt__Key），开发环境使用兜底密钥。</summary>
    public static SymmetricSecurityKey ResolveSigningKey(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // 开发环境兜底密钥；生产必须通过环境变量 Jwt__Key 注入（见 docs/deployment.md）
            secret = "P03-Inventory-DEV-ONLY-signing-key-0123456789abcdef";
        }

        // 密钥长度须满足 HMAC-SHA256 要求（>= 256 bits）
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32)
        {
            bytes = SHA256.HashData(bytes);
        }

        return new SymmetricSecurityKey(bytes);
    }
}
