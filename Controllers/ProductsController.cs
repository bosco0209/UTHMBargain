using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;
using UTHMBargain.Models.ViewModels;

namespace UTHMBargain.Controllers;

public class ProductsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly IWebHostEnvironment _env;

    public ProductsController(ApplicationDbContext db, UserManager<ApplicationUser> userMgr, IWebHostEnvironment env)
    {
        _db = db; _userMgr = userMgr; _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index(ProductSearchViewModel search)
    {
        var q = _db.Products
            .Include(p => p.Images).Include(p => p.Category).Include(p => p.Seller)
            .Where(p => p.Status == ProductStatus.Active);

        if (!string.IsNullOrWhiteSpace(search.Query))
            q = q.Where(p => p.Title.Contains(search.Query) || p.Description.Contains(search.Query));
        if (search.CategoryId is > 0) q = q.Where(p => p.CategoryId == search.CategoryId);
        if (search.MinPrice.HasValue) q = q.Where(p => p.Price >= search.MinPrice);
        if (search.MaxPrice.HasValue) q = q.Where(p => p.Price <= search.MaxPrice);
        if (search.Condition.HasValue) q = q.Where(p => p.Condition == search.Condition);
        if (!string.IsNullOrWhiteSpace(search.Location)) q = q.Where(p => p.Location.Contains(search.Location));

        q = search.SortBy switch
        {
            "price_asc" => q.OrderBy(p => p.Price),
            "price_desc" => q.OrderByDescending(p => p.Price),
            _ => q.OrderByDescending(p => p.CreatedAt)
        };

        const int pageSize = 12;
        var total = await q.CountAsync();
        search.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        search.Page = Math.Max(1, search.Page);
        search.Results = await q.Skip((search.Page - 1) * pageSize).Take(pageSize).ToListAsync();
        search.Categories = await _db.Categories.ToListAsync();
        return View(search);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var product = await _db.Products
            .Include(p => p.Images).Include(p => p.Category).Include(p => p.Seller)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        product.ViewCount++;
        await _db.SaveChangesAsync();

        var ratings = await _db.Ratings.Where(r => r.RatedUserId == product.SellerId).ToListAsync();
        ViewBag.SellerAvg = ratings.Any() ? ratings.Average(r => r.Stars) : 0.0;
        ViewBag.SellerCount = ratings.Count;

        return View(product);
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Create()
    {
        return View(new ProductFormViewModel { Categories = await _db.Categories.ToListAsync() });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await _db.Categories.ToListAsync();
            return View(vm);
        }
        var user = await _userMgr.GetUserAsync(User);
        var product = new Product
        {
            Title = vm.Title,
            Description = vm.Description,
            Price = vm.Price,
            CategoryId = vm.CategoryId,
            Condition = vm.Condition,
            Location = vm.Location,
            SellerId = user!.Id,
            Status = ProductStatus.Active
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        await SaveImagesAsync(product, vm.Images);
        TempData["Success"] = "Listing created successfully!";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userMgr.GetUserAsync(User);
        var product = await _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null || product.SellerId != user!.Id) return Forbid();
        return View(new ProductFormViewModel
        {
            Id = product.Id,
            Title = product.Title,
            Description = product.Description,
            Price = product.Price,
            CategoryId = product.CategoryId,
            Condition = product.Condition,
            Location = product.Location,
            Categories = await _db.Categories.ToListAsync(),
            ExistingImages = product.Images.ToList()
        });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductFormViewModel vm)
    {
        var user = await _userMgr.GetUserAsync(User);
        var product = await _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == vm.Id);
        if (product == null || product.SellerId != user!.Id) return Forbid();
        if (!ModelState.IsValid)
        {
            vm.Categories = await _db.Categories.ToListAsync();
            vm.ExistingImages = product.Images.ToList();
            return View(vm);
        }
        product.Title = vm.Title; product.Description = vm.Description; product.Price = vm.Price;
        product.CategoryId = vm.CategoryId; product.Condition = vm.Condition; product.Location = vm.Location;
        await _db.SaveChangesAsync();
        await SaveImagesAsync(product, vm.Images);
        TempData["Success"] = "Listing updated.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userMgr.GetUserAsync(User);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();
        if (product.SellerId != user!.Id && !User.IsInRole("Admin")) return Forbid();
        product.Status = ProductStatus.Removed;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Listing removed.";
        return RedirectToAction("MyListings");
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> MyListings()
    {
        var user = await _userMgr.GetUserAsync(User);
        var items = await _db.Products.Include(p => p.Images).Include(p => p.Category)
            .Where(p => p.SellerId == user!.Id && p.Status != ProductStatus.Removed)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();
        return View(items);
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(int id, string reason)
    {
        var user = await _userMgr.GetUserAsync(User);
        _db.Reports.Add(new Report { ProductId = id, ReporterId = user!.Id, Reason = reason });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Report submitted. Admin will review.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task SaveImagesAsync(Product product, List<IFormFile>? files)
    {
        if (files == null || files.Count == 0) return;
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);
        bool hasCover = product.Images.Any(i => i.IsCover);
        foreach (var f in files)
        {
            if (f.Length == 0 || f.Length > 5_000_000) continue;
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")) continue;
            var name = $"{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(uploadsDir, name);
            using (var stream = System.IO.File.Create(path))
                await f.CopyToAsync(stream);
            _db.ProductImages.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = $"/uploads/{name}",
                IsCover = !hasCover
            });
            hasCover = true;
        }
        await _db.SaveChangesAsync();
    }
}