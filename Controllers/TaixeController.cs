using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using WedNightFury.Models.ViewModels;
using WedNightFury.Services;

namespace WedNightFury.Controllers
{
    [Authorize]
    public class TaixeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IOrderTrackingService _tracking;

        public TaixeController(AppDbContext context, IWebHostEnvironment env, IOrderTrackingService tracking)
        {
            _context = context;
            _env = env;
            _tracking = tracking;
        }

        // =========================
        // Helpers
        // =========================
        private int? GetCurrentDriverId() => HttpContext.Session.GetInt32("UserId");

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
            var driverId = GetCurrentDriverId();
            if (driverId == null) return RedirectToAction("Login", "Auth");
            return Ok();
        }

        private async Task TrackAsync(Order order, string status, string? title, string? note, CancellationToken ct)
        {
            // location ưu tiên địa chỉ nhận -> tỉnh
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
        // DASHBOARD – đơn đang phụ trách
        // =========================
        public async Task<IActionResult> Dashboard(CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.DriverId == driverId &&
                            (o.Status == "pending" ||
                             o.Status == "assigned" ||
                             o.Status == "shipping"))
                .OrderBy(o =>
                    o.Status == "pending" ? 1 :
                    o.Status == "assigned" ? 2 :
                    o.Status == "shipping" ? 3 : 4
                )
                .ToListAsync(ct);

            return View(orders);
        }

        // =========================
        // AvailableOrders – chỉ hiện đơn hỏa tốc
        // =========================
        public async Task<IActionResult> AvailableOrders(CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o =>
                    o.DriverId == null &&
                    o.Status == "pending" &&
                    (o.ServiceLevel ?? "").ToLower() == "express"
                )
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync(ct);

            return View(orders);
        }

        // Tài xế nhận đơn (chỉ cho hỏa tốc)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOrder(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return NotFound();

            var level = (order.ServiceLevel ?? "").ToLower();
            if (level != "express")
            {
                TempData["Message"] = "Chỉ đơn hỏa tốc mới được tài xế nhận trực tiếp. Đơn thường do Admin phân công.";
                return RedirectToAction(nameof(AvailableOrders));
            }

            if (order.DriverId != null || order.Status != "pending")
            {
                TempData["Message"] = "Đơn đã được xử lý hoặc gán cho tài xế khác.";
                return RedirectToAction(nameof(AvailableOrders));
            }

            order.DriverId = driverId;
            order.AssignedAt = DateTime.Now;
            order.DeliveryDate = DateTime.Today;
            order.Status = "assigned";

            await _context.SaveChangesAsync(ct);

            // ✅ Tracking
            await TrackAsync(order, "assigned", "Tài xế đã nhận đơn", $"DriverId: {driverId}", ct);

            TempData["Message"] = "Bạn đã nhận đơn hỏa tốc.";
            return RedirectToAction(nameof(AvailableOrders));
        }

        // =========================
        // StopDetail
        // =========================
        public async Task<IActionResult> StopDetail(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();
            return View(order);
        }

        // =========================
        // UpdateStatus (đơn lẻ)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string status, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            status = (status ?? "").Trim().ToLowerInvariant();

            // Hiện tại bạn đang dùng action này để "Bắt đầu giao"
            if (status == "shipping")
            {
                if (order.Status != "shipping")
                {
                    order.Status = "shipping";
                    await _context.SaveChangesAsync(ct);

                    // ✅ Tracking
                    await TrackAsync(order, "shipping", "Đang giao hàng", $"DriverId: {driverId}", ct);
                }

                TempData["Message"] = "Đã bắt đầu giao.";
                return RedirectToAction(nameof(Dashboard));
            }

            TempData["Message"] = "Trạng thái không hợp lệ.";
            return RedirectToAction(nameof(Dashboard));
        }

        // ==========================================================
        // ✅ GPS: DTO + API nhận vị trí từ trình duyệt tài xế
        // ==========================================================
        public class UpdateLocationRequest
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
            public double? Accuracy { get; set; }
            public double? Speed { get; set; }
            public double? Heading { get; set; }
            public int? OrderId { get; set; } // optional: tài xế đang giao đơn nào
            public long? ClientTimestampMs { get; set; } // optional
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest req, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            if (req == null) return BadRequest(new { ok = false, message = "Body rỗng." });

            if (req.Lat < -90 || req.Lat > 90 || req.Lng < -180 || req.Lng > 180)
                return BadRequest(new { ok = false, message = "Lat/Lng không hợp lệ." });

            if (req.OrderId.HasValue)
            {
                var okOrder = await _context.Orders
                    .AsNoTracking()
                    .AnyAsync(o => o.Id == req.OrderId.Value && o.DriverId == driverId, ct);
                if (!okOrder) req.OrderId = null;
            }

            var now = DateTime.Now;

            var last = await _context.DriverLocations
                .Where(x => x.DriverId == driverId)
                .OrderByDescending(x => x.RecordedAt)
                .FirstOrDefaultAsync(ct);

            if (last != null && (now - last.RecordedAt).TotalSeconds <= 15)
            {
                last.Lat = req.Lat;
                last.Lng = req.Lng;
                last.Accuracy = req.Accuracy;
                last.Speed = req.Speed;
                last.Heading = req.Heading;
                last.OrderId = req.OrderId;
                last.RecordedAt = now;

                await _context.SaveChangesAsync(ct);
            }
            else
            {
                var row = new DriverLocation
                {
                    DriverId = driverId,
                    OrderId = req.OrderId,
                    Lat = req.Lat,
                    Lng = req.Lng,
                    Accuracy = req.Accuracy,
                    Speed = req.Speed,
                    Heading = req.Heading,
                    RecordedAt = now
                };

                _context.DriverLocations.Add(row);
                await _context.SaveChangesAsync(ct);

                var count = await _context.DriverLocations.CountAsync(x => x.DriverId == driverId, ct);
                if (count > 500)
                {
                    var toDelete = await _context.DriverLocations
                        .Where(x => x.DriverId == driverId)
                        .OrderBy(x => x.RecordedAt)
                        .Take(count - 500)
                        .ToListAsync(ct);

                    _context.DriverLocations.RemoveRange(toDelete);
                    await _context.SaveChangesAsync(ct);
                }
            }

            return Json(new { ok = true });
        }

        // ==========================================================
        // ✅ GPS: lấy vị trí mới nhất của tài xế (để Tracking dùng)
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> LatestLocation(int? orderId, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var q = _context.DriverLocations.AsNoTracking().Where(x => x.DriverId == driverId);
            if (orderId.HasValue) q = q.Where(x => x.OrderId == orderId.Value);

            var last = await q.OrderByDescending(x => x.RecordedAt).FirstOrDefaultAsync(ct);
            if (last == null) return Json(new { ok = true, hasData = false });

            return Json(new
            {
                ok = true,
                hasData = true,
                lat = last.Lat,
                lng = last.Lng,
                accuracy = last.Accuracy,
                speed = last.Speed,
                heading = last.Heading,
                recordedAt = last.RecordedAt
            });
        }

        // =========================
        // Delivered (GET)
        // =========================
        public async Task<IActionResult> Delivered(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            return View(new DeliveredViewModel
            {
                OrderId = order.Id,
                Code = order.Code,
                ReceiverName = order.ReceiverName,
                ReceiverAddress = order.ReceiverAddress,
                CodAmount = order.CodAmount,
                CollectedCod = order.CodAmount
            });
        }

        // Delivered (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delivered(DeliveredViewModel model, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == model.OrderId && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            if (model.PodImage == null)
            {
                ModelState.AddModelError("PodImage", "Bạn phải tải lên ảnh POD.");
                return View(model);
            }

            var folder = Path.Combine(_env.WebRootPath, "uploads/pod");
            Directory.CreateDirectory(folder);

            var fileName = $"{order.Code}_POD_{DateTime.Now:yyyyMMddHHmmss}.jpg";
            var filePath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await model.PodImage.CopyToAsync(stream, ct);

            order.PodImagePath = "/uploads/pod/" + fileName;
            order.DeliveredAt = DateTime.Now;
            order.Status = "done";

            if (!string.IsNullOrWhiteSpace(model.Note))
                order.Note = model.Note;

            var payer = order.ShipPayer ?? "receiver";
            bool codCollected = false;

            if (order.CodAmount > 0 && payer == "receiver")
            {
                order.IsCodPaid = true;
                order.CodPaidAt = DateTime.Now;
                codCollected = true;
            }

            await _context.SaveChangesAsync(ct);

            // ✅ Tracking: giao thành công
            var note = $"Đã giao thành công. POD: {order.PodImagePath}";
            if (!string.IsNullOrWhiteSpace(order.Note))
                note += $". Ghi chú: {order.Note}";
            if (codCollected)
                note += $". Đã thu COD: {order.CodAmount:N0}";

            await TrackAsync(order, "done", "Giao thành công", note, ct);

            TempData["Message"] = "Đã giao hàng và ghi nhận COD (nếu có).";
            return RedirectToAction("StopDetail", new { id = order.Id });
        }

        // ConfirmCOD
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmCOD(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            order.IsCodPaid = true;
            order.CodPaidAt = DateTime.Now;

            await _context.SaveChangesAsync(ct);

            // ✅ Tracking: event riêng cho COD (không bị anti-spam theo status)
            await TrackAsync(order, "cod_collected", "Đã thu COD", $"Số tiền: {order.CodAmount:N0}. DriverId: {driverId}", ct);

            TempData["Message"] = "Đã ghi nhận thu COD.";
            return RedirectToAction("StopDetail", new { id });
        }

        // Failed (GET)
        public async Task<IActionResult> Failed(int id, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            return View(new FailedDeliveryViewModel
            {
                OrderId = order.Id,
                Code = order.Code,
                ReceiverName = order.ReceiverName,
                ReceiverAddress = order.ReceiverAddress
            });
        }

        // Failed (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Failed(FailedDeliveryViewModel model, CancellationToken ct)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == model.OrderId && o.DriverId == driverId, ct);

            if (order == null) return NotFound();

            order.FailedReason = model.FailedReason;
            order.FailedAt = DateTime.Now;
            order.Status = "failed";

            await _context.SaveChangesAsync(ct);

            // ✅ Tracking
            var note = string.IsNullOrWhiteSpace(model.FailedReason) ? $"DriverId: {driverId}" : model.FailedReason;
            await TrackAsync(order, "failed", "Giao thất bại", note, ct);

            TempData["Message"] = "Đã lưu giao thất bại.";
            return RedirectToAction(nameof(Dashboard));
        }

        // History
        public async Task<IActionResult> History(DateTime? day, string status = "all", CancellationToken ct = default)
        {
            var guard = DriverGuard();
            if (guard is not OkResult) return guard;

            var driverId = GetCurrentDriverId()!.Value;

            var query = _context.Orders
                .AsNoTracking()
                .Where(o => o.DriverId == driverId &&
                            (o.Status == "done" || o.Status == "failed"));

            if (day.HasValue)
            {
                var d = day.Value.Date;

                query = query.Where(o =>
                    (o.Status == "done" && o.DeliveredAt.HasValue && o.DeliveredAt.Value.Date == d) ||
                    (o.Status == "failed" && o.FailedAt.HasValue && o.FailedAt.Value.Date == d)
                );
            }

            if (status != "all")
                query = query.Where(o => o.Status == status);

            query = query.OrderByDescending(o => o.DeliveredAt ?? o.FailedAt);

            ViewBag.Day = day?.ToString("yyyy-MM-dd");
            ViewBag.Status = status;

            return View(await query.ToListAsync(ct));
        }
    }
}
