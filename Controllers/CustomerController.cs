using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using WedNightFury.Models;
using System;
using System.Linq;

namespace WedNightFury.Controllers
{
    public class CustomerController : Controller
    {
        private readonly AppDbContext _context;

        public CustomerController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Dashboard(DateTime? fromDate, DateTime? toDate)
        {
            // -----------------------------
            // 1. Kiểm tra đăng nhập
            // -----------------------------
            var username = HttpContext.Session.GetString("UserName");
            var role = HttpContext.Session.GetString("Role");
            var customerId = HttpContext.Session.GetInt32("UserId");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(role) ||
                role.ToLower() != "customer" || customerId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // -----------------------------
            // 2. Auto lọc theo tháng hiện tại
            // -----------------------------
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            if (!fromDate.HasValue) fromDate = startOfMonth;
            if (!toDate.HasValue)   toDate = endOfMonth;

            ViewBag.FromDate = fromDate.Value;
            ViewBag.ToDate = toDate.Value;

            // -----------------------------
            // 3. Lọc đơn theo KH + thời gian
            // -----------------------------
            var orders = _context.Orders
                .Where(o => o.CustomerId == customerId)
                .Where(o => o.CreatedAt.HasValue &&
                            o.CreatedAt.Value.Date >= fromDate.Value.Date &&
                            o.CreatedAt.Value.Date <= toDate.Value.Date)
                .AsQueryable();

            // -----------------------------
            // 4. Tổng quan
            // -----------------------------
            ViewBag.SuccessCount = orders.Count(o => o.Status != null && o.Status.ToLower() == "done");
            ViewBag.FailCount    = orders.Count(o => o.Status != null && o.Status.ToLower() == "cancelled");
            ViewBag.TotalOrders  = orders.Count();

            // -----------------------------
            // 5. BAR CHART – thống kê theo tháng
            // -----------------------------
            var rawBarData = orders
                .GroupBy(o => new { o.CreatedAt!.Value.Year, o.CreatedAt.Value.Month })
                .Select(g => new
                {
                    Year    = g.Key.Year,
                    Month   = g.Key.Month,
                    Success = g.Count(x => x.Status != null && x.Status.ToLower() == "done"),
                    Fail    = g.Count(x => x.Status != null && x.Status.ToLower() == "cancelled"),
                    Shipping= g.Count(x => x.Status != null && x.Status.ToLower() == "shipping"),
                    Pending = g.Count(x => x.Status != null && x.Status.ToLower() == "pending")
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();

            ViewBag.BarData = rawBarData.Select(x => new
            {
                month    = $"{x.Month:D2}/{x.Year}",
                success  = x.Success,
                fail     = x.Fail,
                shipping = x.Shipping,
                pending  = x.Pending
            }).ToList();

            // -----------------------------
            // 6. PIE CHART – Top 10 tỉnh
            // -----------------------------
            var pie = orders
                .Where(o => !string.IsNullOrEmpty(o.Province))
                .GroupBy(o => o.Province!)
                .Select(g => new { province = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            // Nếu không có tỉnh => group theo status
            ViewBag.PieData = pie.Any()
                ? pie
                : orders.GroupBy(o => o.Status ?? "Không rõ")
                        .Select(g => new { province = g.Key, count = g.Count() })
                        .ToList();

            // -----------------------------
            // 7. Phần thông tin hiển thị
            // -----------------------------
            ViewBag.CustomerName = username;
            ViewBag.LastUpdate = DateTime.Now.ToString("HH:mm");

            return View();
        }
    }
}
