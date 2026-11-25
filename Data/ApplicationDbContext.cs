using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Models;

namespace WebApp.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(100);
            entity.Property(user => user.LastName).HasMaxLength(100);
        });

        // Estas tablas pertenecen al esquema heredado y se crean mediante
        // LegacySchemaInitializer. Excluirlas evita que una migración intente
        // recrearlas sobre una base SQLite persistente con datos existentes.
        builder.Entity<Category>(entity =>
        {
            entity.ToTable("categories", table => table.ExcludeFromMigrations());
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Id).HasColumnName("id");
            entity.Property(category => category.Name).HasColumnName("name").IsRequired();
        });

        builder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks", table => table.ExcludeFromMigrations());
            entity.HasKey(task => task.Id);
            entity.Property(task => task.Id).HasColumnName("id");
            entity.Property(task => task.Title).HasColumnName("title").IsRequired();
            entity.Property(task => task.Completed).HasColumnName("completed").HasConversion<int>();
            entity.Property(task => task.CategoryId).HasColumnName("category_id");
            entity.HasOne(task => task.Category)
                .WithMany(category => category.Tasks)
                .HasForeignKey(task => task.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Product>(entity =>
        {
            entity.ToTable("products", table =>
            {
                table.HasCheckConstraint("CK_products_Price_NonNegative", "Price >= 0");
                table.HasCheckConstraint("CK_products_Stock_NonNegative", "Stock >= 0");
            });
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).HasMaxLength(150).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(1000);
            entity.Property(product => product.Price).HasColumnType("NUMERIC").IsRequired();
            entity.Property(product => product.Stock).IsRequired();
            entity.Property(product => product.CreatedAt).IsRequired();
            entity.Property(product => product.UpdatedAt).IsRequired();
        });
    }
}
