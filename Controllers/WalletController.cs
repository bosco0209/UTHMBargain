using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;
using UTHMBargain.Models.ViewModels;

namespace UTHMBargain.Controllers;

[Authorize]
public class WalletController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;

    public WalletController(ApplicationDbContext db, UserManager<ApplicationUser> userMgr)
    {
        _db = db;
        _userMgr = userMgr;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userMgr.GetUserAsync(User);
        ViewBag.Transactions = await _db.WalletTransactions
            .Where(t => t.UserId == user!.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(30)
            .ToListAsync();
        return View(user);
    }

    [HttpGet]
    public IActionResult TopUp() => View(new TopUpViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TopUp(TopUpViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _userMgr.GetUserAsync(User);
        user!.WalletBalance += vm.Amount;
        _db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = user.Id,
            Amount = vm.Amount,
            Type = "TopUp",
            Reference = "Simulated Top-up"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Wallet topped up with RM {vm.Amount:F2} (simulated).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(decimal amount)
    {
        var user = await _userMgr.GetUserAsync(User);
        if (amount <= 0 || user!.WalletBalance < amount)
        {
            TempData["Error"] = "Invalid withdrawal amount.";
            return RedirectToAction(nameof(Index));
        }
        user.WalletBalance -= amount;
        _db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = user.Id,
            Amount = -amount,
            Type = "Withdraw",
            Reference = "Simulated Withdraw"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Withdrew RM {amount:F2} (simulated).";
        return RedirectToAction(nameof(Index));
    }
}