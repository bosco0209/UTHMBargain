using Microsoft.AspNetCore.Identity;

namespace UTHMBargain.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string MatricNumber { get; set; } = string.Empty;
    public string? Faculty { get; set; }
    public string? Campus { get; set; } // Pagoh / Parit Raja
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public decimal WalletBalance { get; set; } = 0m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsBanned { get; set; } = false;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Rating> RatingsReceived { get; set; } = new List<Rating>();
}