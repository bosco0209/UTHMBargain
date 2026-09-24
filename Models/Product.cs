using System.ComponentModel.DataAnnotations;

namespace UTHMBargain.Models;

public enum ProductCondition { New, LikeNew, Good, Fair, Used }
public enum ProductStatus { Active, Reserved, Sold, Removed, Flagged }

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 100000)]
    public decimal Price { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public ProductCondition Condition { get; set; } = ProductCondition.Good;

    [StringLength(120)]
    public string Location { get; set; } = "Parit Raja";

    public ProductStatus Status { get; set; } = ProductStatus.Active;

    public string SellerId { get; set; } = string.Empty;
    public ApplicationUser? Seller { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int ViewCount { get; set; } = 0;

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsCover { get; set; } = false;
}

public class Category
{
    public int Id { get; set; }
    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-tag";
    public ICollection<Product> Products { get; set; } = new List<Product>();
}