using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;

public class AdminSupportController : Controller
{
    private readonly AppDbContext _context;

    public AdminSupportController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        var list = _context.SupportTickets
            .OrderBy(t => t.Status)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return View(list);
    }

    public IActionResult Reply(int id)
    {
        var ticket = _context.SupportTickets.FirstOrDefault(t => t.Id == id);
        if (ticket == null) return NotFound();

        return View(ticket);
    }

    [HttpPost]
    public IActionResult Reply(int id, string reply)
    {
        var ticket = _context.SupportTickets.FirstOrDefault(t => t.Id == id);
        if (ticket == null) return NotFound();

        ticket.Reply = reply;
        ticket.Status = "replied";
        ticket.RepliedAt = DateTime.Now;

        _context.SaveChanges();

        TempData["Message"] = "✔ Đã trả lời yêu cầu!";
        return RedirectToAction("Index");
    }
}
