using System.ComponentModel.DataAnnotations;

namespace UTHMBargain.Models;

public enum EscrowStatus
{
    Pending,      // Buyer initiated, funds held
    Shipped,      // Seller marked as shipped/handed over
    Completed,    // Buyer confirmed receipt, funds released
    Disputed,     // Dispute raised
    Refunded,     // Admin refunded buyer
    Cancelled     // Cancelled before completion
}

public class EscrowTransaction
{
    public int Id { get; set; }
    public string Code { get; set; } = Guid.NewGuid().ToString("N")[..10].ToUpper();

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string BuyerId { get; set; } = string.Empty;
    public ApplicationUser? Buyer { get; set; }

    public string SellerId { get; set; } = string.Empty;
    public ApplicationUser? Seller { get; set; }

    [Range(0.01, 100000)]
    public decimal Amount { get; set; }

    public EscrowStatus Status { get; set; } = EscrowStatus.Pending;

    [StringLength(500)]
    public string? DisputeReason { get; set; }

    [StringLength(500)]
    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ShippedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WalletTransaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public decimal Amount { get; set; }    // positive = credit, negative = debit
    public string Type { get; set; } = "TopUp"; // TopUp, EscrowHold, EscrowRelease, EscrowRefund, Withdraw
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Rating
{
    public int Id { get; set; }
    public int EscrowTransactionId { get; set; }
    public EscrowTransaction? EscrowTransaction { get; set; }

    public string RaterId { get; set; } = string.Empty;
    public ApplicationUser? Rater { get; set; }

    public string RatedUserId { get; set; } = string.Empty;
    public ApplicationUser? RatedUser { get; set; }

    [Range(1, 5)]
    public int Stars { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Report
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ReporterId { get; set; } = string.Empty;
    public ApplicationUser? Reporter { get; set; }
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
    public bool Resolved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}