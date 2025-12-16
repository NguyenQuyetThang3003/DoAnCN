using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    public class SupportController : Controller
    {
        private readonly AppDbContext _context;

        public SupportController(AppDbContext context)
        {
            _context = context;
        }

        private int? GetDriverId()
        {
            return HttpContext.Session.GetInt32("UserId");
        }

        // ========== FORM GỬI HỖ TRỢ ==========

        // GET: /Support
        public IActionResult Index()
        {
            var driverId = GetDriverId();
            if (driverId == null)
                return RedirectToAction("DriverLogin", "Auth");

            return View();
        }

        // GET: /Support/Create  -> tránh 400 khi gõ trực tiếp
        [HttpGet]
        public IActionResult Create()
        {
            return RedirectToAction(nameof(Index));
        }

        // POST: /Support/Create  -> gửi ticket mới
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                TempData["Error"] = "Vui lòng nhập nội dung hỗ trợ.";
                return RedirectToAction(nameof(Index));
            }

            var driverId = GetDriverId();
            if (driverId == null)
            {
                TempData["Error"] = "Không tìm thấy tài xế. Vui lòng đăng nhập lại.";
                return RedirectToAction("DriverLogin", "Auth");
            }

            var ticket = new SupportTicket
            {
                DriverId  = driverId.Value,
                Message   = message,
                Status    = "pending",
                CreatedAt = DateTime.Now
            };

            _context.SupportTickets.Add(ticket);
            _context.SaveChanges();

            TempData["Success"] = "📩 Gửi yêu cầu thành công!";
            return RedirectToAction(nameof(History));
        }

        // ========== LỊCH SỬ HỖ TRỢ ==========

        // GET: /Support/History
        public IActionResult History()
        {
            var driverId = GetDriverId();
            if (driverId == null)
                return RedirectToAction("DriverLogin", "Auth");

            var list = _context.SupportTickets
                .Where(t => t.DriverId == driverId.Value)
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            return View(list);
        }

        // ========== TÀI XẾ TRẢ LỜI TIẾP ==========

        // GET: /Support/AddReply  -> nếu gõ trực tiếp URL thì quay lại History
        [HttpGet]
        public IActionResult AddReply()
        {
            return RedirectToAction(nameof(History));
        }

        // POST: /Support/AddReply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddReply(int id, string replyText)
        {
            if (string.IsNullOrWhiteSpace(replyText))
            {
                TempData["Error"] = "Vui lòng nhập nội dung trả lời.";
                return RedirectToAction(nameof(History));
            }

            var driverId = GetDriverId();
            if (driverId == null)
                return RedirectToAction("DriverLogin", "Auth");

            var ticket = _context.SupportTickets
                .FirstOrDefault(t => t.Id == id && t.DriverId == driverId.Value);

            if (ticket == null)
                return NotFound();

            var now = DateTime.Now;
            var newLine = $"[Driver - {now:dd/MM/yyyy HH:mm}] {replyText}";

            if (string.IsNullOrWhiteSpace(ticket.Reply))
                ticket.Reply = newLine;
            else
                ticket.Reply += "\n" + newLine;

            // Có thể giữ status cũ; hoặc đánh dấu lại là "pending" để admin biết có reply mới
            ticket.Status    = "pending";
            ticket.RepliedAt = now;

            _context.SaveChanges();

            TempData["Success"] = "Đã gửi trả lời cho bộ phận hỗ trợ.";
            return RedirectToAction(nameof(History));
        }
    }
}
