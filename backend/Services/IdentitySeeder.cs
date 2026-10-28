using Microsoft.AspNetCore.Identity;
using P03.Inventory.Api.Data;

namespace P03.Inventory.Api.Services;

public interface IIdentitySeeder
{
    /// <summary>幂等初始化 Admin/Operator 角色与默认账号（已存在则跳过，不修改密码）。</summary>
    Task SeedAsync(CancellationToken ct = default);
}

public class IdentitySeeder : IIdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string OperatorRole = "Operator";

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        ILogger<IdentitySeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await EnsureRoleAsync(AdminRole);
        await EnsureRoleAsync(OperatorRole);

        await EnsureUserAsync(AdminRole, "SeedUsers:Admin");
        await EnsureUserAsync(OperatorRole, "SeedUsers:Operator");
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create role {roleName}: {Describe(result)}");
            _logger.LogInformation("Identity role created: {Role}", roleName);
        }
    }

    private async Task EnsureUserAsync(string role, string configKey)
    {
        var userName = _configuration[$"{configKey}:UserName"];
        var password = _configuration[$"{configKey}:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Seed user config missing at {ConfigKey}, skipped.", configKey);
            return;
        }

        var existing = await _userManager.FindByNameAsync(userName);
        if (existing != null)
        {
            if (!await _userManager.IsInRoleAsync(existing, role))
            {
                await _userManager.AddToRoleAsync(existing, role);
            }
            return;
        }

        var user = new AppUser { UserName = userName, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to create seed user {userName}: {Describe(result)}");

        await _userManager.AddToRoleAsync(user, role);
        _logger.LogInformation("Seed user created: {User} ({Role})", userName, role);
    }

    private static string Describe(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
