using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------- 数据库（SQL Server / LocalDB，EF Core Code-First，迁移仅成员A生成） ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("Default")));

// ---------- ASP.NET Core Identity（Admin/Operator 角色，密码哈希不落明文） ----------
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager<SignInManager<AppUser>>();

// ---------- JWT Bearer 鉴权（未登录 401 / 无权限 403，统一错误结构） ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "P03.Inventory.Api",
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"] ?? "P03.Inventory.Client",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.ResolveSigningKey(configuration),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ErrorResponse(
                    ErrorCodes.Unauthorized,
                    "Authentication required. Provide a valid bearer token.",
                    null,
                    TraceIdMiddleware.Get(context.HttpContext)));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ErrorResponse(
                    ErrorCodes.Forbidden,
                    "You do not have permission to perform this action.",
                    null,
                    TraceIdMiddleware.Get(context.HttpContext)));
            },
        };
    });

builder.Services.AddAuthorization();

// ---------- 控制器 + 模型校验错误统一返回 VALIDATION_ERROR ----------
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .ToDictionary(
                kv => JsonNamingPolicy.CamelCase.ConvertName(kv.Key),
                kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var payload = new ErrorResponse(
            ErrorCodes.ValidationError,
            "One or more validation errors occurred.",
            details,
            TraceIdMiddleware.Get(context.HttpContext));

        return new BadRequestObjectResult(payload);
    };
});

// ---------- Swagger / OpenAPI ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "P03 Inventory API",
        Version = "v1",
        Description = "企业库存管理与数据可视化后台 API（契约见 docs/api-contract.md）",
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "粘贴登录返回的 JWT。",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

// ---------- CORS（Angular 开发服务器默认 4200；开发环境放行任意 localhost 端口，避免前端端口不一致被拦） ----------
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    if (builder.Environment.IsDevelopment())
    {
        policy.SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost") || origin.StartsWith("http://127.0.0.1"))
            .AllowAnyHeader().AllowAnyMethod();
    }
    else
    {
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod();
    }
}));

// ---------- 登录限流：每 IP 每分钟 10 次 ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(
            ErrorCodes.RateLimited,
            "Too many requests. Please try again later.",
            null,
            TraceIdMiddleware.Get(context.HttpContext)), cancellationToken);
    };
    options.AddPolicy("login", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"login:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// ---------- 业务服务 ----------
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ISeedImportService, SeedImportService>();
builder.Services.AddScoped<IIdentitySeeder, IdentitySeeder>();
builder.Services.AddSingleton<ITokenService, TokenService>();

var app = builder.Build();

// ---------- 种子导入 CLI 模式：dotnet run -- --seed-import ----------
if (args.Contains("--seed-import"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        await services.GetRequiredService<IIdentitySeeder>().SeedAsync();
        var summary = await services.GetRequiredService<ISeedImportService>().ImportAsync();
        return summary.MatchesExpectedBaseline ? 0 : 2; // 2 = 导入完成但基线与期望不一致
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[seed-import] FAILED: {ex.Message}");
        return 1;
    }
}

// ---------- 中间件管道（顺序：TraceId → 全局异常 → Swagger → CORS → 限流 → 认证 → 授权） ----------
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "P03 Inventory API v1"));

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ---------- 启动时幂等初始化角色与默认账号 ----------
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IIdentitySeeder>().SeedAsync();
}

await app.RunAsync();
return 0;
