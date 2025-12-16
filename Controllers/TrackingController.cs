using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WedNightFury.Controllers
{
    public class TrackingController : Controller
    {
        private readonly AppDbContext _context;

        public TrackingController(AppDbContext context)
        {
            _context = context;
        }

        // ================== [GET] /Tracking ==================
        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.Message = TempData["Message"];
            return View();
        }

        // ================== [POST] /Tracking/Find ==================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Find(string keyword)
        {
            keyword = (keyword ?? "").Trim();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                ViewBag.Message = "⚠️ Vui lòng nhập mã đơn hoặc số điện thoại!";
                return View("Index");
            }

            // Chuẩn hóa số: chỉ lấy digit từ input
            var digits = Regex.Replace(keyword, @"\D", "");
            bool looksLikeOrderCode = keyword.StartsWith("NF-", StringComparison.OrdinalIgnoreCase);

            var q = _context.Orders.AsNoTracking();

            // 1) Nếu giống mã đơn -> ưu tiên tìm theo Code
            if (looksLikeOrderCode)
            {
                var byCode = await q
                    .Where(o => o.Code == keyword)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                if (byCode.Any())
                    return View("Result", byCode);
            }

            // 2) Tạo các biến thể số điện thoại để tìm (09xx, 84xx, +84...)
            var phoneVariants = BuildPhoneVariants(digits);

            // 3) Query tìm theo Code hoặc theo phone (không dùng Regex trong Where vì EF không translate được)
            // Lưu ý: cách này giả định DB thường lưu phone dạng "098..." hoặc "84..."
            // Nếu DB lưu phone có dấu cách/dấu gạch, nên lưu thêm cột PhoneNormalized để tìm chuẩn hơn.
            IQueryable<Order> qFiltered = q.Where(o => o.Code == keyword);

            if (phoneVariants.Count > 0)
            {
                qFiltered = qFiltered.Union(
                    q.Where(o =>
                        (!string.IsNullOrEmpty(o.ReceiverPhone) && phoneVariants.Any(v => o.ReceiverPhone.Contains(v))) ||
                        (!string.IsNullOrEmpty(o.SenderPhone) && phoneVariants.Any(v => o.SenderPhone.Contains(v)))
                    )
                );
            }

            var orders = await qFiltered
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            if (!orders.Any())
            {
                ViewBag.Message = "❌ Không tìm thấy đơn hàng nào.";
                return View("Index");
            }

            return View("Result", orders);
        }

        // ================== [GET] /Tracking/Details/{id} ==================
        // Trang chi tiết 1 đơn (có timeline)
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.TrackingEvents)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                TempData["Message"] = "❌ Không tìm thấy đơn hàng.";
                return RedirectToAction(nameof(Index));
            }

            // Sort timeline cho đẹp (mới nhất lên đầu)
            if (order.TrackingEvents != null)
            {
                order.TrackingEvents = order.TrackingEvents
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();
            }

            // View: Views/Tracking/Details.cshtml
            return View("Details", order);
        }

        // =============== Helpers ===============
        private static List<string> BuildPhoneVariants(string digits)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(digits))
                return variants.ToList();

            // Trường hợp user nhập +84..., 84..., 0...
            variants.Add(digits);

            // Nếu bắt đầu bằng "84" và còn dài -> thêm biến thể "0..."
            if (digits.StartsWith("84") && digits.Length >= 11)
            {
                var local0 = "0" + digits.Substring(2);
                variants.Add(local0);
            }

            // Nếu bắt đầu bằng "0" -> thêm biến thể "84..."
            if (digits.StartsWith("0") && digits.Length >= 10)
            {
                var intl84 = "84" + digits.Substring(1);
                variants.Add(intl84);
            }

            // Loại bỏ biến thể quá ngắn (tránh match lung tung)
            variants.RemoveWhere(v => v.Length < 7);

            return variants.ToList();
        }
    }
}
