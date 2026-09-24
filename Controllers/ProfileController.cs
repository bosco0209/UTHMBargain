using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;
using UTHMBargain.Services;

namespace UTHMBargain.Controllers;

public class ProfileController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly IWebHostEnvironment _env;
    private readonly AnalyticsService _analytics;

    public ProfileController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userMgr,
        IWebHostEnvironment env,
        AnalyticsService analytics)
    {
        _db = db;
        _userMgr = userMgr;
        _env = env;
        _analytics = analytics;
    }

    // ==================== PUBLIC PROFILE ====================
    public async Task<IActionResult> Index(string? id = null)
    {
        var uid = id ?? _userMgr.GetUserId(User);
        if (uid == null) return RedirectToAction("Login", "Account");

        var user = await _userMgr.FindByIdAsync(uid);
        if (user == null) return NotFound();

        var listings = await _db.Products
            .Include(p => p.Images)
            .Where(p => p.SellerId == uid && p.Status != ProductStatus.Removed)
            .OrderByDescending(p => p.CreatedAt)
            .Take(12)
            .ToListAsync();

        var ratings = await _db.Ratings
            .Include(r => r.Rater)
            .Where(r => r.RatedUserId == uid)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        ViewBag.Listings = listings;
        ViewBag.Ratings = ratings;
        ViewBag.AvgStars = ratings.Any() ? ratings.Average(r => r.Stars) : 0.0;
        ViewBag.CompletedDeals = await _db.EscrowTransactions.CountAsync(e =>
            (e.SellerId == uid || e.BuyerId == uid) && e.Status == EscrowStatus.Completed);
        ViewBag.IsOwn = uid == _userMgr.GetUserId(User);

        return View(user);
    }

    // ==================== PERSONAL ANALYTICS DASHBOARD ====================
    [Authorize]
    public async Task<IActionResult> Dashboard()
    {
        var user = await _userMgr.GetUserAsync(User);
        if (user == null) return Forbid();

        var data = await _analytics.GetUserAnalyticsAsync(user.Id);
        ViewBag.User = user;
        return View(data);
    }

    // ==================== EDIT PROFILE ====================
    [HttpGet, Authorize]
    public async Task<IActionResult> Edit()
    {
        var user = await _userMgr.GetUserAsync(User);
        return View(user);
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string fullName, string faculty, string campus, string? bio, IFormFile? avatar)
    {
        var user = await _userMgr.GetUserAsync(User);
        if (user == null) return Forbid();

        user.FullName = fullName;
        user.Faculty = faculty;
        user.Campus = campus;
        user.Bio = bio;

        if (avatar != null && avatar.Length > 0 && avatar.Length < 3_000_000)
        {
            var ext = Path.GetExtension(avatar.FileName).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                var dir = Path.Combine(_env.WebRootPath, "uploads");
                Directory.CreateDirectory(dir);
                var name = $"avatar_{user.Id}{ext}";
                using var s = System.IO.File.Create(Path.Combine(dir, name));
                await avatar.CopyToAsync(s);
                user.AvatarUrl = $"/uploads/{name}";
            }
        }

        await _userMgr.UpdateAsync(user);
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }
}