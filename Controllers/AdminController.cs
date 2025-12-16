using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Filters;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    [AdminAuthorize]
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;

        public AdminController(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            // ===== 1) Lấy số liệu 4 ô thống kê TRUNG TUYẾN (sequential) =====
            var totalOrders = await _db.Orders.CountAsync();
            var successOrders = await _db.Orders.CountAsync(o => o.DeliveredAt != null);
            var failedOrders = await _db.Orders.CountAsync(o => o.FailedAt != null);
            var pendingComplaints = await _db.SupportTickets.CountAsync(s => s.Status == "pending");

            // ===== 2) 10 đơn mới nhất =====
            var latestOrders = await
                (from o in _db.Orders.AsNoTracking()
                 join cus in _db.Users.AsNoTracking() on o.CustomerId equals cus.Id
                 join drvUser in _db.Users.AsNoTracking() on o.DriverId equals drvUser.Id into drvJoin
                 from drv in drvJoin.DefaultIfEmpty()
                 orderby o.CreatedAt descending
                 select new LatestOrderViewModel
                 {
                     Code         = o.Code,
                     CustomerName = cus.UserName,
                     DriverName   = drv != null ? drv.UserName : "(Chưa gán tài xế)",
                     Amount       = o.CodAmount,   // decimal
                     Status       = o.Status
                 })
                .Take(10)
                .ToListAsync();

            // ===== 3) Gắn vào ViewModel =====
            var vm = new AdminDashboardViewModel
            {
                TotalOrders       = totalOrders,
                SuccessOrders     = successOrders,
                FailedOrders      = failedOrders,
                PendingComplaints = pendingComplaints,
                LatestOrders      = latestOrders
            };

            return View(vm);
        }
    }

    // ================== VIEWMODELS ==================

    public class AdminDashboardViewModel
    {
        public int TotalOrders { get; set; }
        public int SuccessOrders { get; set; }
        public int FailedOrders { get; set; }
        public int PendingComplaints { get; set; }

        public List<LatestOrderViewModel> LatestOrders { get; set; } = new();
    }

    public class LatestOrderViewModel
    {
        public string Code { get; set; }
        public string CustomerName { get; set; }
        public string DriverName { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
    }
}
