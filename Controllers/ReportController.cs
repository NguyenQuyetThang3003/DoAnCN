using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;
using System;
using System.Linq;

namespace WedNightFury.Controllers
{
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context)
        {
            _context = context;
        }

        // ================== [GET] /Report/Today ==================
        [HttpGet]
        public IActionResult Today()
        {
            DateTime today = DateTime.Today;

            // Lấy danh sách đơn hàng trong ngày
            var ordersToday = _context.Orders
                .Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value.Date == today)
                .ToList();

            // Đếm theo trạng thái
            var pending = ordersToday.Count(o => o.Status == "pending");
            var shipping = ordersToday.Count(o => o.Status == "shipping");
            var done = ordersToday.Count(o => o.Status == "done");
            var cancelled = ordersToday.Count(o => o.Status == "cancelled");

            // Tổng đầu ngày (đơn trước hôm nay)
            var totalBefore = _context.Orders.Count(o => o.CreatedAt.HasValue && o.CreatedAt.Value.Date < today);
            var totalToday = ordersToday.Count;

            // Truyền dữ liệu sang View
            ViewBag.LastUpdated = DateTime.Now.ToString("HH:mm:ss");
            ViewBag.TotalBefore = totalBefore;
            ViewBag.TotalToday = totalToday;
            ViewBag.Pending = pending;
            ViewBag.Shipping = shipping;
            ViewBag.Done = done;
            ViewBag.Cancelled = cancelled;

            // Gửi dữ liệu dạng JSON cho Chart.js
            ViewBag.ChartData = new[] { pending, shipping, done, cancelled };

            return View();
        }
    }
}
