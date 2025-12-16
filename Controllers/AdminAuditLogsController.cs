using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using WedNightFury.Filters;   // chứa AdminAuthorizeAttribute
using WedNightFury.Models;    // chứa AppDbContext, AuditLog

namespace WedNightFury.Controllers
{
    [AdminAuthorize]
    public class AdminAuditLogsController : Controller
    {
        private readonly AppDbContext _db;

        public AdminAuditLogsController(AppDbContext db)
        {
            _db = db;
        }

        // =========================
        // DANH SÁCH NHẬT KÝ
        // URL: /AdminAuditLogs
        // =========================
        public async Task<IActionResult> Index(
            string? from,
            string? to,
            string? action = "all",
            string? entityType = "all",
            string? keyword = "")
        {
            // Lưu filter vào ViewBag để view hiển thị lại
            ViewBag.From       = from;
            ViewBag.To         = to;
            ViewBag.Action     = action ?? "all";
            ViewBag.EntityType = entityType ?? "all";
            ViewBag.Keyword    = keyword ?? "";

            // Tạm thời: lấy toàn bộ log (chưa áp dụng lọc, để đảm bảo thấy dữ liệu)
            var logs = await _db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            return View(logs);   // Model: IEnumerable<AuditLog>
        }

        // =========================
        // CHI TIẾT 1 DÒNG NHẬT KÝ
        // URL: /AdminAuditLogs/Details/5
        // =========================
        public async Task<IActionResult> Details(int id)
        {
            var log = await _db.AuditLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (log == null) return NotFound();

            return View(log);
        }
    }
}
