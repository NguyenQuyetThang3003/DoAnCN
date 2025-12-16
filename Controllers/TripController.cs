using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using WedNightFury.Models;
using WedNightFury.Services;

namespace WedNightFury.Controllers
{
    [Authorize]
    public class TripController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IOrderTrackingService _tracking;

        public TripController(
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            IWebHostEnvironment env,
            IOrderTrackingService tracking)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _env = env;
            _tracking = tracking;
        }

        private int? GetDriverId() => HttpContext.Session.GetInt32("UserId");

        private bool IsDriver()
        {
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrWhiteSpace(role)) return false;

            role = role.ToLower().Trim();
            return role == "driver" || role == "taixe";
        }

        private IActionResult DriverGuard()
        {
            if (!IsDriver()) return Forbid();
            var driverId = GetDriverId();
            if (driverId == null) return RedirectToAction("Login", "Auth");
            return Ok();
        }

        // =========================
        // Nominatim DTO
        // =========================
        private sealed class NominatimResult
        {
            public string? lat { get; set; }
            public string? lon { get; set; }
        }

        // =========================
        // GEOCODING – Lấy Lat/Lng từ địa chỉ (nếu thiếu)
        // =========================
        private async Task<(double? lat, double? lng)> GetLatLngFromAddressAsync(string address, CancellationToken ct)
        {
            address = (address ?? "").Trim();
            if (string.IsNullOrWhiteSpace(address)) return (null, null);

            try
            {
                // Ưu tiên dùng named client "nominatim" đã cấu hình ở Program.cs
                var client = _httpClientFactory.CreateClient("nominatim");

                // Dùng jsonv2 cho ổn định hơn
                var url = $"search?format=jsonv2&limit=1&q={Uri.EscapeDataString(address)}";

                using var res = await client.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) return (null, null);

                var json = await res.Content.ReadAsStringAsync(ct);
                var data = JsonSerializer.Deserialize<List<NominatimResult>>(json);

                if (data == null || data.Count == 0) return (null, null);

                var latStr = data[0].lat;
                var lonStr = data[0].lon;

                if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)) return (null, null);
                if (!double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng)) return (null, null);

                if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return (null, null);

                return (lat, lng);
            }
            catch
            {
                return (null, null);
            }
        }

        private async Task TrackAsync(Order order, string status, string? title, string? note, CancellationToken ct)
        {
            var location = !string.IsNullOrWhiteSpace(order.ReceiverAddress) ? order.ReceiverAddress : (order.Province ?? null);

            await _tracking.AddAsync(
                orderId: order.Id,
                status: status,
                title: title,
                note: note,
                location: location,
                ct: ct
            );
        }

        // =========================
        // LỘ TRÌNH HÔM NAY
        // =========================
        [HttpGet]
        public async Task<IActionResult> Today(CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetDriverId()!.Value;
            var today = DateTime.Today;

            // lấy data trước (db), sort tiếng Việt ở memory
            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.DriverId == driverId
                            && o.DeliveryDate.HasValue
                            && o.DeliveryDate.Value.Date == today)
                .ToListAsync(ct);

            orders = orders
                .OrderBy(o =>
                    (o.Status ?? "").ToLower() == "shipping" ? 1 :
                    (o.Status ?? "").ToLower() == "assigned" ? 2 :
                    (o.Status ?? "").ToLower() == "pending" ? 3 :
                    (o.Status ?? "").ToLower() == "done" ? 4 :
                    (o.Status ?? "").ToLower() == "failed" ? 5 : 99
                )
                .ThenBy(o => o.Sequence)
                .ToList();

            return View(orders);
        }

        // =========================
        // CHI TIẾT ĐIỂM GIAO
        // =========================
        [HttpGet]
        public async Task<IActionResult> StopDetail(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            // Tự động geocode nếu chưa có tọa độ
            if (!order.Lat.HasValue || !order.Lng.HasValue)
            {
                var (lat, lng) = await GetLatLngFromAddressAsync(order.ReceiverAddress ?? "", ct);

                if (lat.HasValue && lng.HasValue)
                {
                    order.Lat = lat;
                    order.Lng = lng;
                    await _context.SaveChangesAsync(ct);

                    // (tuỳ chọn) tracking event nhỏ để debug dữ liệu
                    await TrackAsync(order, "geo_updated", "Cập nhật tọa độ", $"Lat/Lng: {lat.Value.ToString(CultureInfo.InvariantCulture)},{lng.Value.ToString(CultureInfo.InvariantCulture)}", ct);
                }
            }

            return View(order);
        }

        // =========================
        // BẮT ĐẦU GIAO (nếu bạn muốn có nút riêng trong Trip)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartShipping(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            if ((order.Status ?? "").ToLowerInvariant() != "shipping")
            {
                order.Status = "shipping";
                await _context.SaveChangesAsync(ct);
                await TrackAsync(order, "shipping", "Đang giao hàng", $"DriverId: {driverId}", ct);
            }

            TempData["Message"] = "Đã bắt đầu giao.";
            return RedirectToAction(nameof(Today));
        }

        // =========================
        // GIAO THÀNH CÔNG (POD)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteDelivery(int id, IFormFile? podImage, string? note, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            string? imagePath = null;

            if (podImage != null && podImage.Length > 0)
            {
                // Lưu vào wwwroot/images/pod
                var folder = Path.Combine(_env.WebRootPath, "images", "pod");
                Directory.CreateDirectory(folder);

                var ext = Path.GetExtension(podImage.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

                var fileName = $"POD_{order.Code ?? id.ToString()}_{DateTime.Now:yyyyMMddHHmmssfff}{ext}";
                var savePath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(savePath, FileMode.Create))
                    await podImage.CopyToAsync(stream, ct);

                imagePath = "/images/pod/" + fileName;
            }

            order.PodImagePath = imagePath;
            order.Status = "done";
            order.DeliveredAt = DateTime.Now;

            // dùng Note chung cho đơn (tránh lỗi compile nếu bạn không có DeliveredNote)
            if (!string.IsNullOrWhiteSpace(note))
                order.Note = note;

            await _context.SaveChangesAsync(ct);

            // ✅ tracking timeline
            var trackNote = "Giao thành công";
            if (!string.IsNullOrWhiteSpace(imagePath)) trackNote += $". POD: {imagePath}";
            if (!string.IsNullOrWhiteSpace(order.Note)) trackNote += $". Ghi chú: {order.Note}";

            await TrackAsync(order, "done", "Giao thành công", trackNote, ct);

            TempData["Message"] = "Đã giao thành công.";
            return RedirectToAction(nameof(Today));
        }

        // =========================
        // GIAO THẤT BẠI
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FailedDelivery(int id, string? reason, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            order.Status = "failed";
            order.FailedReason = reason;
            order.FailedAt = DateTime.Now;

            await _context.SaveChangesAsync(ct);

            // ✅ tracking timeline
            var r = string.IsNullOrWhiteSpace(reason) ? $"DriverId: {driverId}" : reason.Trim();
            await TrackAsync(order, "failed", "Giao thất bại", r, ct);

            TempData["Message"] = "Đã lưu giao thất bại.";
            return RedirectToAction(nameof(Today));
        }
    }
}
