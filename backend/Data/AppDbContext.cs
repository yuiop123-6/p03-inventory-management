using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Data.Entities;

namespace P03.Inventory.Api.Data;

/// <summary>应用数据库上下文（迁移仅由成员A生成）。</summary>
public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---- Category ----
        builder.Entity<Category>(e =>
        {
            e.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            e.Property(x => x.Description)
                .HasMaxLength(500);

            // 忽略大小写与首尾空格的唯一性：持久化计算列 + 唯一索引（数据库层强制，禁止仅应用层校验）
            e.Property<string>("NameNormalized")
                .HasComputedColumnSql("UPPER(LTRIM(RTRIM([Name])))", stored: true)
                .HasMaxLength(100);

            // 计算列由必填 Name 派生、恒非空；显式去掉 EF 默认的 IS NOT NULL 过滤（SQL Server 不允许筛选列含计算列）
            e.HasIndex("NameNormalized").IsUnique().HasDatabaseName("UX_Categories_NameNormalized").HasFilter(null);

            e.ToTable(t => t.HasCheckConstraint("CK_Categories_Name_NotEmpty", "LEN(LTRIM(RTRIM([Name]))) > 0"));
        });

        // ---- Product ----
        builder.Entity<Product>(e =>
        {
            e.Property(x => x.ProductCode).IsRequired().HasMaxLength(40);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.ProductCode).IsUnique().HasDatabaseName("UX_Products_ProductCode");
            e.HasIndex(x => x.CategoryId).HasDatabaseName("IX_Products_CategoryId");
            e.HasIndex(x => x.Name).HasDatabaseName("IX_Products_Name"); // search（名称包含匹配）

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Products_UnitPrice_Nonnegative", "[UnitPrice] >= 0");
                t.HasCheckConstraint("CK_Products_CurrentStock_Nonnegative", "[CurrentStock] >= 0");
                t.HasCheckConstraint("CK_Products_MinimumStockLevel_Nonnegative", "[MinimumStockLevel] >= 0");
                t.HasCheckConstraint("CK_Products_Name_NotEmpty", "LEN(LTRIM(RTRIM([Name]))) > 0");
            });

            // 分类有商品时数据库层同样阻止删除（应用层返回 409 REFERENTIAL_CONFLICT）
            e.HasOne(x => x.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- StockTransaction ----
        builder.Entity<StockTransaction>(e =>
        {
            e.Property(x => x.TransactionReference).IsRequired().HasMaxLength(64);
            e.Property(x => x.RequestId).IsRequired().HasMaxLength(64);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.Type).IsRequired().HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TransactionReference).IsUnique().HasDatabaseName("UX_StockTransactions_Reference");
            e.HasIndex(x => x.RequestId).IsUnique().HasDatabaseName("UX_StockTransactions_RequestId");
            e.HasIndex(x => new { x.ProductId, x.CreatedAt }).HasDatabaseName("IX_StockTransactions_Product_CreatedAt");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_StockTransactions_CreatedAt");

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StockTransactions_Type", "[Type] IN ('IN','OUT','ADJUST')");
                // IN/OUT 为正整数变动量；ADJUST 为新的目标库存，允许 0
                t.HasCheckConstraint("CK_StockTransactions_Quantity",
                    "([Type] IN ('IN','OUT') AND [Quantity] > 0) OR ([Type] = 'ADJUST' AND [Quantity] >= 0)");
                t.HasCheckConstraint("CK_StockTransactions_PreviousStock_Nonnegative", "[PreviousStock] >= 0");
                t.HasCheckConstraint("CK_StockTransactions_NewStock_Nonnegative", "[NewStock] >= 0");
            });

            e.HasOne(x => x.Product)
                .WithMany(p => p.Transactions)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
