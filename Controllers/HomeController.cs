using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;

namespace UTHMBargain.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        ViewBag.Categories = await _db.Categories.ToListAsync();
        ViewBag.Featured = await _db.Products
            .Include(p => p.Images).Include(p => p.Category).Include(p => p.Seller)
            .Where(p => p.Status == ProductStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .Take(8).ToListAsync();
        ViewBag.Stats = new
        {
            Products = await _db.Products.CountAsync(p => p.Status == ProductStatus.Active),
            Users = await _db.Users.CountAsync(),
            Deals = await _db.EscrowTransactions.CountAsync(e => e.Status == EscrowStatus.Completed)
        };
        return View();
    }

    public IActionResult Privacy() => View();
    public IActionResult Error() => View();
}