using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;
using UTHMBargain.Models.ViewModels;

namespace UTHMBargain.Controllers;

[Authorize]
public class EscrowController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;

    public EscrowController(ApplicationDbContext db, UserManager<ApplicationUser> userMgr)
    { _db = db; _userMgr = userMgr; }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int productId)
    {
        var user = await _userMgr.GetUserAsync(User);
        var product = await _db.Products.FindAsync(productId);
        if (product == null || product.Status != ProductStatus.Active) return NotFound();
        if (product.SellerId == user!.Id) { TempData["Error"] = "You cannot buy your own listing."; return RedirectToAction("Details", "Products", new { id = productId }); }
        if (user.WalletBalance < product.Price)
        {
            TempData["Error"] = $"Insufficient wallet balance. You need RM {product.Price:F2}. Please top up.";
            return RedirectToAction("Index", "Wallet");
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        user.WalletBalance -= product.Price;
        _db.WalletTransactions.Add(new WalletTransaction { UserId = user.Id, Amount = -product.Price, Type = "EscrowHold", Reference = $"Product #{product.Id}" });

        var escrow = new EscrowTransaction
        {
            ProductId = product.Id,
            BuyerId = user.Id,
            SellerId = product.SellerId,
            Amount = product.Price,
            Status = EscrowStatus.Pending
        };
        _db.EscrowTransactions.Add(escrow);
        product.Status = ProductStatus.Reserved;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Success"] = $"Funds RM {product.Price:F2} held in escrow. Escrow code: {escrow.Code}";
        return RedirectToAction(nameof(Details), new { id = escrow.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkShipped(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.FindAsync(id);
        if (e == null || e.SellerId != uid || e.Status != EscrowStatus.Pending) return BadRequest();
        e.Status = EscrowStatus.Shipped; e.ShippedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Marked as handed over. Waiting for buyer confirmation.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceipt(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (e == null || e.BuyerId != uid) return BadRequest();
        if (e.Status != EscrowStatus.Shipped && e.Status != EscrowStatus.Pending) return BadRequest();

        using var tx = await _db.Database.BeginTransactionAsync();
        var seller = await _userMgr.FindByIdAsync(e.SellerId);
        seller!.WalletBalance += e.Amount;
        _db.WalletTransactions.Add(new WalletTransaction { UserId = seller.Id, Amount = e.Amount, Type = "EscrowRelease", Reference = $"Escrow {e.Code}" });
        e.Status = EscrowStatus.Completed; e.CompletedAt = DateTime.UtcNow;
        if (e.Product != null) e.Product.Status = ProductStatus.Sold;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Success"] = "Funds released to seller. Please rate your experience.";
        return RedirectToAction("Rate", new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (e == null || (e.BuyerId != uid && e.SellerId != uid) || e.Status != EscrowStatus.Pending) return BadRequest();
        using var tx = await _db.Database.BeginTransactionAsync();
        var buyer = await _userMgr.FindByIdAsync(e.BuyerId);
        buyer!.WalletBalance += e.Amount;
        _db.WalletTransactions.Add(new WalletTransaction { UserId = buyer.Id, Amount = e.Amount, Type = "EscrowRefund", Reference = $"Escrow {e.Code} cancelled" });
        e.Status = EscrowStatus.Cancelled;
        if (e.Product != null && e.Product.Status == ProductStatus.Reserved) e.Product.Status = ProductStatus.Active;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"] = "Escrow cancelled and buyer refunded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispute(int id, string reason)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.FindAsync(id);
        if (e == null || (e.BuyerId != uid && e.SellerId != uid)) return BadRequest();
        if (e.Status is EscrowStatus.Completed or EscrowStatus.Refunded or EscrowStatus.Cancelled) return BadRequest();
        e.Status = EscrowStatus.Disputed; e.DisputeReason = reason;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Dispute raised. Admin will investigate.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions
            .Include(x => x.Product).ThenInclude(p => p!.Images)
            .Include(x => x.Buyer).Include(x => x.Seller)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (e.BuyerId != uid && e.SellerId != uid && !User.IsInRole("Admin")) return Forbid();
        ViewBag.Rating = await _db.Ratings.FirstOrDefaultAsync(r => r.EscrowTransactionId == id && r.RaterId == uid);
        return View(e);
    }

    public async Task<IActionResult> Index()
    {
        var uid = _userMgr.GetUserId(User)!;
        var list = await _db.EscrowTransactions
            .Include(x => x.Product).ThenInclude(p => p!.Images)
            .Include(x => x.Buyer).Include(x => x.Seller)
            .Where(x => x.BuyerId == uid || x.SellerId == uid)
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Rate(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.FindAsync(id);
        if (e == null || (e.BuyerId != uid && e.SellerId != uid)) return Forbid();
        if (e.Status != EscrowStatus.Completed) return RedirectToAction(nameof(Details), new { id });
        var exists = await _db.Ratings.FirstOrDefaultAsync(r => r.EscrowTransactionId == id && r.RaterId == uid);
        if (exists != null) return RedirectToAction(nameof(Details), new { id });
        return View(new RatingFormViewModel { EscrowTransactionId = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rate(RatingFormViewModel vm)
    {
        var uid = _userMgr.GetUserId(User)!;
        var e = await _db.EscrowTransactions.FindAsync(vm.EscrowTransactionId);
        if (e == null || (e.BuyerId != uid && e.SellerId != uid) || e.Status != EscrowStatus.Completed) return Forbid();
        var ratedId = e.BuyerId == uid ? e.SellerId : e.BuyerId;
        var exists = await _db.Ratings.FirstOrDefaultAsync(r => r.EscrowTransactionId == vm.EscrowTransactionId && r.RaterId == uid);
        if (exists != null) return RedirectToAction(nameof(Details), new { id = vm.EscrowTransactionId });

        _db.Ratings.Add(new Rating { EscrowTransactionId = vm.EscrowTransactionId, RaterId = uid, RatedUserId = ratedId, Stars = vm.Stars, Comment = vm.Comment });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Thanks for rating!";
        return RedirectToAction(nameof(Details), new { id = vm.EscrowTransactionId });
    }
}