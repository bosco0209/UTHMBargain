using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Models;

namespace UTHMBargain.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<EscrowTransaction> EscrowTransactions => Set<EscrowTransaction>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>()
            .HasOne(p => p.Seller)
            .WithMany(u => u.Products)
            .HasForeignKey(p => p.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Conversation>()
            .HasOne(c => c.Buyer).WithMany().HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Conversation>()
            .HasOne(c => c.Seller).WithMany().HasForeignKey(c => c.SellerId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EscrowTransaction>()
            .HasOne(e => e.Buyer).WithMany().HasForeignKey(e => e.BuyerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EscrowTransaction>()
            .HasOne(e => e.Seller).WithMany().HasForeignKey(e => e.SellerId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Rating>()
            .HasOne(r => r.Rater).WithMany().HasForeignKey(r => r.RaterId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Rating>()
            .HasOne(r => r.RatedUser).WithMany(u => u.RatingsReceived).HasForeignKey(r => r.RatedUserId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>().Property(p => p.Price).HasColumnType("decimal(10,2)");
        builder.Entity<EscrowTransaction>().Property(e => e.Amount).HasColumnType("decimal(10,2)");
        builder.Entity<ApplicationUser>().Property(u => u.WalletBalance).HasColumnType("decimal(10,2)");
        builder.Entity<WalletTransaction>().Property(w => w.Amount).HasColumnType("decimal(10,2)");
    }
}

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var ctx = services.GetRequiredService<ApplicationDbContext>();
        var userMgr = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();

        await ctx.Database.MigrateAsync();

        foreach (var role in new[] { "Admin", "Student" })
        {
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new IdentityRole(role));
        }

        if (!ctx.Categories.Any())
        {
            ctx.Categories.AddRange(
                new Category { Name = "Textbooks", Icon = "fa-book" },
                new Category { Name = "Electronics", Icon = "fa-laptop" },
                new Category { Name = "Furniture", Icon = "fa-couch" },
                new Category { Name = "Clothing", Icon = "fa-shirt" },
                new Category { Name = "Sports & Hobbies", Icon = "fa-basketball" },
                new Category { Name = "Bicycles & Transport", Icon = "fa-bicycle" },
                new Category { Name = "Food & Snacks", Icon = "fa-utensils" },
                new Category { Name = "Services", Icon = "fa-handshake" },
                new Category { Name = "Others", Icon = "fa-box" }
            );
            await ctx.SaveChangesAsync();
        }

        // Seed admin
        var adminEmail = "admin@uthm.edu.my";
        var admin = await userMgr.FindByEmailAsync(adminEmail);
        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = "UTHM Admin",
                MatricNumber = "AD000001",
                Faculty = "Administration",
                Campus = "Parit Raja",
                WalletBalance = 0m
            };
            await userMgr.CreateAsync(admin, "Admin@123");
            await userMgr.AddToRoleAsync(admin, "Admin");
        }

        // Seed a demo student user
        var demoEmail = "demo@uthm.edu.my";
        var demo = await userMgr.FindByEmailAsync(demoEmail);
        if (demo == null)
        {
            demo = new ApplicationUser
            {
                UserName = demoEmail,
                Email = demoEmail,
                EmailConfirmed = true,
                FullName = "Demo Student",
                MatricNumber = "AI220001",
                Faculty = "FSKTM",
                Campus = "Parit Raja",
                WalletBalance = 500m
            };
            await userMgr.CreateAsync(demo, "Demo@123");
            await userMgr.AddToRoleAsync(demo, "Student");
        }
    }
}