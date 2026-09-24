using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UTHMBargain.Models;
using UTHMBargain.Models.ViewModels;

namespace UTHMBargain.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly SignInManager<ApplicationUser> _signInMgr;

    public AccountController(UserManager<ApplicationUser> userMgr, SignInManager<ApplicationUser> signInMgr)
    {
        _userMgr = userMgr;
        _signInMgr = signInMgr;
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var existing = _userMgr.Users.FirstOrDefault(u => u.MatricNumber == vm.MatricNumber);
        if (existing != null)
        {
            ModelState.AddModelError(nameof(vm.MatricNumber), "Matric number already registered.");
            return View(vm);
        }

        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FullName = vm.FullName,
            MatricNumber = vm.MatricNumber.ToUpper(),
            Faculty = vm.Faculty,
            Campus = vm.Campus,
            EmailConfirmed = true,
            WalletBalance = 0m
        };

        var result = await _userMgr.CreateAsync(user, vm.Password);
        if (result.Succeeded)
        {
            await _userMgr.AddToRoleAsync(user, "Student");
            await _signInMgr.SignInAsync(user, isPersistent: false);
            TempData["Success"] = $"Welcome to UTHM Bargain, {user.FullName}!";
            return RedirectToAction("Index", "Home");
        }

        foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
        return View(vm);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userMgr.FindByEmailAsync(vm.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(vm);
        }
        if (user.IsBanned)
        {
            ModelState.AddModelError(string.Empty, "Your account has been banned. Contact admin.");
            return View(vm);
        }

        var result = await _signInMgr.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }
        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInMgr.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}