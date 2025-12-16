using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using WedNightFury.Models.ViewModels;

namespace WedNightFury.Controllers
{
    public class AdminComplaintsController : Controller
    {
        private readonly AppDbContext _context;

        public AdminComplaintsController(AppDbContext context)
        {
            _context = context;
        }

        // ================== DANH SÁCH ==================
        // GET: /AdminComplaints
        // Có thể lọc theo trạng thái: all | pending | replied
        public async Task<IActionResult> Index(string? status)
        {
            var query =
                from t in _context.SupportTickets
                join u in _context.Users on t.DriverId equals u.Id
                where u.Role == "driver"
                orderby t.CreatedAt descending
                select new AdminComplaintListItemViewModel
                {
                    Id             = t.Id,
                    DriverId       = t.DriverId,
                    DriverUserName = u.UserName,
                    DriverFullName = u.CompanyName, // hoặc lấy từ Profile nếu muốn
                    Message        = t.Message,
                    Status         = t.Status,
                    Reply          = t.Reply,
                    CreatedAt      = t.CreatedAt,
                    RepliedAt      = t.RepliedAt
                };

            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                status = status.Trim().ToLower();
                query = query.Where(x => x.Status.ToLower() == status);
            }

            var list = await query.ToListAsync();

            ViewBag.Status = status ?? "all";
            return View(list);
        }

        // ================== XEM HỘI THOẠI ==================
        // GET: /AdminComplaints/ViewThread/5
        public async Task<IActionResult> ViewThread(int id)
        {
            var vm = await
                (from t in _context.SupportTickets
                 join u in _context.Users on t.DriverId equals u.Id
                 where t.Id == id && u.Role == "driver"
                 select new AdminComplaintThreadViewModel
                 {
                     Id             = t.Id,
                     DriverId       = t.DriverId,
                     DriverUserName = u.UserName,
                     DriverFullName = u.CompanyName,
                     Message        = t.Message,
                     Reply          = t.Reply,
                     Status         = t.Status,
                     CreatedAt      = t.CreatedAt,
                     RepliedAt      = t.RepliedAt
                 }).FirstOrDefaultAsync();

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        // ================== TRANG REPLY CŨ (nếu muốn dùng) ==================
        // GET: /AdminComplaints/Reply/5
        public async Task<IActionResult> Reply(int id)
        {
            var vm = await
                (from t in _context.SupportTickets
                 join u in _context.Users on t.DriverId equals u.Id
                 where t.Id == id && u.Role == "driver"
                 select new AdminComplaintReplyViewModel
                 {
                     Id             = t.Id,
                     DriverId       = t.DriverId,
                     DriverUserName = u.UserName,
                     DriverFullName = u.CompanyName,
                     Message        = t.Message,
                     Reply          = t.Reply,
                     Status         = t.Status,
                     CreatedAt      = t.CreatedAt,
                     RepliedAt      = t.RepliedAt
                 }).FirstOrDefaultAsync();

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        // POST: /AdminComplaints/Reply/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int id, AdminComplaintReplyViewModel model)
        {
            if (id != model.Id)
                return BadRequest();

            var ticket = await _context.SupportTickets
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket == null)
                return NotFound();

            ticket.Reply     = model.Reply;
            ticket.Status    = string.IsNullOrWhiteSpace(model.Reply)
                                ? "pending"
                                : "replied";
            ticket.RepliedAt = string.IsNullOrWhiteSpace(model.Reply)
                                ? (DateTime?)null
                                : DateTime.Now;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ================== GỬI TIN NHẮN NGAY TRONG VIEWTHREAD ==================
        // POST: /AdminComplaints/AddMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMessage(int id, string replyText)
        {
            if (string.IsNullOrWhiteSpace(replyText))
            {
                TempData["Error"] = "Vui lòng nhập nội dung trả lời.";
                return RedirectToAction(nameof(ViewThread), new { id });
            }

            var ticket = await _context.SupportTickets
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket == null)
                return NotFound();

            var now = DateTime.Now;
            var newLine = $"[Admin - {now:dd/MM/yyyy HH:mm}] {replyText}";

            if (string.IsNullOrWhiteSpace(ticket.Reply))
                ticket.Reply = newLine;
            else
                ticket.Reply += "\n" + newLine;

            ticket.Status    = "replied";
            ticket.RepliedAt = now;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ViewThread), new { id });
        }
    }
}
