using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;

namespace UTHMBargain.Controllers;

[Authorize]
public class MessagesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;

    public MessagesController(ApplicationDbContext db, UserManager<ApplicationUser> userMgr)
    { _db = db; _userMgr = userMgr; }

    public async Task<IActionResult> Index()
    {
        var uid = _userMgr.GetUserId(User)!;
        var convos = await _db.Conversations
            .Include(c => c.Product).ThenInclude(p => p!.Images)
            .Include(c => c.Buyer).Include(c => c.Seller)
            .Where(c => c.BuyerId == uid || c.SellerId == uid)
            .OrderByDescending(c => c.LastMessageAt).ToListAsync();
        return View(convos);
    }

    [HttpGet]
    public async Task<IActionResult> Start(int productId)
    {
        var uid = _userMgr.GetUserId(User)!;
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return NotFound();
        if (product.SellerId == uid) { TempData["Error"] = "You can't message yourself."; return RedirectToAction("Details", "Products", new { id = productId }); }

        var convo = await _db.Conversations.FirstOrDefaultAsync(c =>
            c.ProductId == productId && c.BuyerId == uid && c.SellerId == product.SellerId);
        if (convo == null)
        {
            convo = new Conversation { ProductId = productId, BuyerId = uid, SellerId = product.SellerId };
            _db.Conversations.Add(convo);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Thread), new { id = convo.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Thread(int id)
    {
        var uid = _userMgr.GetUserId(User)!;
        var convo = await _db.Conversations
            .Include(c => c.Product).ThenInclude(p => p!.Images)
            .Include(c => c.Buyer).Include(c => c.Seller)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (convo == null || (convo.BuyerId != uid && convo.SellerId != uid)) return Forbid();

        foreach (var m in convo.Messages.Where(m => m.SenderId != uid && !m.IsRead))
            m.IsRead = true;
        await _db.SaveChangesAsync();
        return View(convo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int conversationId, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return RedirectToAction(nameof(Thread), new { id = conversationId });
        var uid = _userMgr.GetUserId(User)!;
        var convo = await _db.Conversations.FindAsync(conversationId);
        if (convo == null || (convo.BuyerId != uid && convo.SellerId != uid)) return Forbid();
        _db.Messages.Add(new Message { ConversationId = conversationId, SenderId = uid, Body = body.Trim() });
        convo.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Thread), new { id = conversationId });
    }

    [HttpGet]
    public async Task<IActionResult> Poll(int conversationId, int afterId = 0)
    {
        var uid = _userMgr.GetUserId(User)!;
        var convo = await _db.Conversations.FindAsync(conversationId);
        if (convo == null || (convo.BuyerId != uid && convo.SellerId != uid)) return Forbid();
        var messages = await _db.Messages
            .Where(m => m.ConversationId == conversationId && m.Id > afterId)
            .OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.SenderId, m.Body, sentAt = m.SentAt, mine = m.SenderId == uid })
            .ToListAsync();
        var toMark = await _db.Messages.Where(m => m.ConversationId == conversationId && m.SenderId != uid && !m.IsRead).ToListAsync();
        foreach (var m in toMark) m.IsRead = true;
        await _db.SaveChangesAsync();
        return Json(messages);
    }
}