using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;
using UTHMBargain.Services;

namespace UTHMBargain.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly AnalyticsService _analytics;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userMgr,
        AnalyticsService analytics)
    {
        _db = db;
        _userMgr = userMgr;
        _analytics = analytics;
    }

    // ==================== DASHBOARD ====================
    public async Task<IActionResult> Index()
    {
        ViewBag.UsersCount = await _db.Users.CountAsync();
        ViewBag.ProductsCount = await _db.Products.CountAsync();
        ViewBag.ActiveListings = await _db.Products.CountAsync(p => p.Status == ProductStatus.Active);
        ViewBag.EscrowActive = await _db.EscrowTransactions.CountAsync(e =>
            e.Status == EscrowStatus.Pending || e.Status == EscrowStatus.Shipped);
        ViewBag.DisputesOpen = await _db.EscrowTransactions.CountAsync(e => e.Status == EscrowStatus.Disputed);
        ViewBag.ReportsOpen = await _db.Reports.CountAsync(r => !r.Resolved);

        // ✅ Fixed for SQLite: sum in memory, not in SQL
        var heldAmounts = await _db.EscrowTransactions
            .Where(e => e.Status == EscrowStatus.Pending || e.Status == EscrowStatus.Shipped)
            .Select(e => e.Amount)
            .ToListAsync();
        ViewBag.RevenueHeld = heldAmounts.Sum();

        return View();
    }

    // ==================== ANALYTICS DASHBOARD ====================
    public async Task<IActionResult> Analytics(int days = 30)
    {
        var data = await _analytics.GetAdminAnalyticsAsync(days);
        return View(data);
    }

    // ==================== USERS ====================
    public async Task<IActionResult> Users()
    {
        var users = await _userMgr.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        return View(users);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBan(string id)
    {
        var u = await _userMgr.FindByIdAsync(id);
        if (u == null) return NotFound();
        u.IsBanned = !u.IsBanned;
        await _userMgr.UpdateAsync(u);
        TempData["Success"] = u.IsBanned ? "User banned." : "User unbanned.";
        return RedirectToAction(nameof(Users));
    }

    // ==================== PRODUCTS ====================
    public async Task<IActionResult> Products()
    {
        var products = await _db.Products
            .Include(p => p.Seller)
            .Include(p => p.Category)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return View(products);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FlagProduct(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p == null) return NotFound();
        p.Status = p.Status == ProductStatus.Flagged ? ProductStatus.Active : ProductStatus.Flagged;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Listing status updated.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveProduct(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p == null) return NotFound();
        p.Status = ProductStatus.Removed;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Listing removed.";
        return RedirectToAction(nameof(Products));
    }

    // ==================== DISPUTES ====================
    public async Task<IActionResult> Disputes()
    {
        var list = await _db.EscrowTransactions
            .Include(e => e.Product)
            .Include(e => e.Buyer)
            .Include(e => e.Seller)
            .Where(e => e.Status == EscrowStatus.Disputed)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveDispute(int id, string decision, string? note)
    {
        var e = await _db.EscrowTransactions
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null || e.Status != EscrowStatus.Disputed) return NotFound();

        using var tx = await _db.Database.BeginTransactionAsync();

        if (decision == "refund")
        {
            var buyer = await _userMgr.FindByIdAsync(e.BuyerId);
            buyer!.WalletBalance += e.Amount;
            _db.WalletTransactions.Add(new WalletTransaction
            {
                UserId = buyer.Id,
                Amount = e.Amount,
                Type = "EscrowRefund",
                Reference = $"Admin resolved {e.Code}"
            });
            e.Status = EscrowStatus.Refunded;
            if (e.Product != null && e.Product.Status == ProductStatus.Reserved)
                e.Product.Status = ProductStatus.Active;
        }
        else
        {
            var seller = await _userMgr.FindByIdAsync(e.SellerId);
            seller!.WalletBalance += e.Amount;
            _db.WalletTransactions.Add(new WalletTransaction
            {
                UserId = seller.Id,
                Amount = e.Amount,
                Type = "EscrowRelease",
                Reference = $"Admin resolved {e.Code}"
            });
            e.Status = EscrowStatus.Completed;
            e.CompletedAt = DateTime.UtcNow;
            if (e.Product != null) e.Product.Status = ProductStatus.Sold;
        }

        e.AdminNote = note;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Success"] = "Dispute resolved.";
        return RedirectToAction(nameof(Disputes));
    }

    // ==================== REPORTS ====================
    public async Task<IActionResult> Reports()
    {
        var list = await _db.Reports
            .Include(r => r.Product)
            .Include(r => r.Reporter)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveReport(int id)
    {
        var r = await _db.Reports.FindAsync(id);
        if (r == null) return NotFound();
        r.Resolved = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Report marked resolved.";
        return RedirectToAction(nameof(Reports));
    }

    // ==================== ALL ESCROWS ====================
    public async Task<IActionResult> Escrows()
    {
        var list = await _db.EscrowTransactions
            .Include(e => e.Product)
            .Include(e => e.Buyer)
            .Include(e => e.Seller)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync();
        return View(list);
    }
}