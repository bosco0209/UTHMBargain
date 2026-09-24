using System.ComponentModel.DataAnnotations;
using UTHMBargain.Models;

namespace UTHMBargain.Models.ViewModels;

public class RegisterViewModel
{
    [Required, Display(Name = "Full Name"), StringLength(80)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, Display(Name = "UTHM Email")]
    [RegularExpression(@"^[a-zA-Z0-9._%+-]+@(uthm\.edu\.my|student\.uthm\.edu\.my)$",
        ErrorMessage = "Only @uthm.edu.my or @student.uthm.edu.my emails are allowed.")]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Matric Number")]
    [RegularExpression(@"^[A-Z]{2}[0-9]{6}$", ErrorMessage = "Matric format: 2 letters + 6 digits (e.g. AI220001).")]
    public string MatricNumber { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Faculty { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Campus { get; set; } = "Parit Raja";

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

public class ProductFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 100000), DataType(DataType.Currency)]
    public decimal Price { get; set; }

    [Required, Display(Name = "Category")]
    public int CategoryId { get; set; }

    public ProductCondition Condition { get; set; } = ProductCondition.Good;

    [Required, StringLength(120)]
    public string Location { get; set; } = "Parit Raja";

    public List<IFormFile>? Images { get; set; }

    public List<Category>? Categories { get; set; }
    public List<ProductImage>? ExistingImages { get; set; }
}

public class ProductSearchViewModel
{
    public string? Query { get; set; }
    public int? CategoryId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public ProductCondition? Condition { get; set; }
    public string? Location { get; set; }
    public string SortBy { get; set; } = "newest"; // newest | price_asc | price_desc

    public List<Product> Results { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}

public class RatingFormViewModel
{
    public int EscrowTransactionId { get; set; }
    [Range(1, 5)] public int Stars { get; set; } = 5;
    [StringLength(500)] public string? Comment { get; set; }
}

public class TopUpViewModel
{
    [Range(1, 10000)] public decimal Amount { get; set; }
}