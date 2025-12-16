using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Filters;
using WedNightFury.Models;
using WedNightFury.Services;

namespace WedNightFury.Controllers
{
    [AdminAuthorize]
    public class AdminOrdersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _nominatim;
        private readonly IOrderTrackingService _tracking;

        // ✅ TempData key để lưu dữ liệu preview (PRG chống 400 khi refresh)
        private const string RoutePreviewTempDataKey = "RoutePreview";

        // ======= GEO CONFIG =======
        private const string GeoEmail = "hoainam1872004@gmail.com";
        private const string GeoUserAgent = "WedNightFury/1.0 (contact: hoainam1872004@gmail.com)";

        // ✅ MƯỢT: giảm số candidate + retry + timeout
        private const int MaxCandidatesToTry = 3;
        private const int MaxRetries = 1;
        private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(3.5);
        private static readonly TimeSpan OriginTimeout = TimeSpan.FromSeconds(5);

        // ✅ MƯỢT: giới hạn số đơn geocode trong 1 lần Preview
        private const int MaxOrdersToGeocodePerPreview = 8;

        // Cache theo địa chỉ (in-memory)
        private static readonly ConcurrentDictionary<string, (double lat, double lng)> _geoCache = new();

        // Rate limit toàn app ~ 1 req / ~1s
        private static readonly SemaphoreSlim _geoGate = new(1, 1);
        private static DateTime _lastGeoCallUtc = DateTime.MinValue;
        private static readonly TimeSpan _minGeoInterval = TimeSpan.FromMilliseconds(950);

        public AdminOrdersController(AppDbContext db, IHttpClientFactory httpClientFactory, IOrderTrackingService tracking)
        {
            _db = db;
            _tracking = tracking;

            _nominatim = httpClientFactory.CreateClient("nominatim");
            if (_nominatim.BaseAddress == null)
                _nominatim.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");

            _nominatim.DefaultRequestHeaders.UserAgent.Clear();
            _nominatim.DefaultRequestHeaders.UserAgent.ParseAdd(GeoUserAgent);

            if (!_nominatim.DefaultRequestHeaders.Contains("Accept-Language"))
                _nominatim.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8");

            if (!_nominatim.DefaultRequestHeaders.Contains("Accept"))
                _nominatim.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        // =========================================================
        // ✅ PRG - GET Preview: đọc dữ liệu đã lưu ở TempData để refresh không bị 400
        // =========================================================
        [HttpGet]
        public IActionResult PreviewAssignRoute()
        {
            var json = TempData.Peek(RoutePreviewTempDataKey) as string;

            if (string.IsNullOrWhiteSpace(json))
            {
                TempData["Error"] = "Trang xem tuyến chỉ mở sau khi bấm 'Tối ưu tuyến' từ trang Đơn mới.";
                return RedirectToAction(nameof(NewOrders));
            }

            try
            {
                var vm = JsonSerializer.Deserialize<AdminRoutePreviewViewModel>(json);
                if (vm == null)
                {
                    TempData["Error"] = "Dữ liệu tuyến không hợp lệ. Vui lòng tối ưu lại.";
                    return RedirectToAction(nameof(NewOrders));
                }

                return View("PreviewAssignRoute", vm);
            }
            catch
            {
                TempData["Error"] = "Không đọc được dữ liệu tuyến. Vui lòng tối ưu lại.";
                return RedirectToAction(nameof(NewOrders));
            }
        }

        // ===========================
        // Helper: append TempData Warning (không bị ghi đè)
        // ===========================
        private void AddWarning(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            var cur = TempData["Warning"]?.ToString();
            TempData["Warning"] = string.IsNullOrWhiteSpace(cur) ? msg : (cur + " | " + msg);
        }

        // ===========================
        // HUB: load danh sách hub active cho view admin
        // ===========================
        private async Task LoadActiveHubsToViewBagAsync(CancellationToken ct = default)
        {
            ViewBag.Hubs = await _db.Hubs
                .AsNoTracking()
                .Where(h => h.IsActive)
                .Select(h => new HubOptionVm
                {
                    Id = h.Id,
                    Name = h.Name ?? "",
                    Code = h.Code ?? "",
                    Address = h.Address ?? "",
                    Lat = h.Lat,
                    Lng = h.Lng
                })
                .OrderBy(h => h.Name)
                .ToListAsync(ct);
        }

        // =========================================================
        // TEST NHANH: /AdminOrders/GeoTest?q=...
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GeoTest(string q, CancellationToken ct)
        {
            q = (q ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return Json(new
                {
                    ok = false,
                    msg = "Thiếu q. Ví dụ: /AdminOrders/GeoTest?q=98%20Phan%20Xích%20Long,%20Hồ%20Chí%20Minh"
                });

            var normalized = NormalizeForGeocode(q);
            var candidates = BuildGeocodeCandidates(normalized).Take(10).ToList();
            var (lat, lng, err) = await GeocodeFallbackAsync(normalized, ct, PerRequestTimeout);

            return Json(new { ok = lat.HasValue && lng.HasValue, input = q, normalized, lat, lng, err, candidates });
        }

        // =========================================================
        // 1) INDEX – TẤT CẢ ĐƠN
        // =========================================================
        public async Task<IActionResult> Index(
            string? status,
            string? keyword,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var query = _db.Orders.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "all")
                query = query.Where(o => o.Status == status);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();
                query = query.Where(o =>
                    (o.Code != null && o.Code.Contains(keyword)) ||
                    (o.SenderName != null && o.SenderName.Contains(keyword)) ||
                    (o.ReceiverName != null && o.ReceiverName.Contains(keyword)) ||
                    (o.SenderPhone != null && o.SenderPhone.Contains(keyword)) ||
                    (o.ReceiverPhone != null && o.ReceiverPhone.Contains(keyword))
                );
            }

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                query = query.Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value >= from);
            }

            if (toDate.HasValue)
            {
                var toExclusive = toDate.Value.Date.AddDays(1);
                query = query.Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value < toExclusive);
            }

            query = query.OrderByDescending(o => o.CreatedAt);

            var totalItems = await query.CountAsync(ct);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new AdminOrderListItemViewModel
                {
                    Id = o.Id,
                    Code = o.Code ?? "",
                    CustomerName = o.User != null
                        ? (string.IsNullOrEmpty(o.User.UserName) ? "(Khách lẻ)" : o.User.UserName)
                        : "(Khách lẻ)",
                    ReceiverName = o.ReceiverName ?? "",
                    ReceiverPhone = o.ReceiverPhone ?? "",
                    ReceiverAddress = o.ReceiverAddress ?? "",
                    Province = o.Province ?? "",
                    ProductName = o.ProductName ?? "",
                    CodAmount = o.CodAmount,
                    ShipFee = o.ShipFee,
                    Status = o.Status ?? "",
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync(ct);

            var statusStats = await _db.Orders
                .AsNoTracking()
                .GroupBy(o => o.Status ?? "unknown")
                .Select(g => new AdminOrderStatusStat { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var vmIndex = new AdminOrderListViewModel
            {
                Items = items,
                TotalItems = totalItems,
                PageIndex = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                StatusFilter = status,
                Keyword = keyword,
                FromDate = fromDate,
                ToDate = toDate,
                StatusStats = statusStats
            };

            return View(vmIndex);
        }

        // =========================================================
        // 2) NEW ORDERS – CHỜ PHÂN CÔNG  ✅ LỌC THEO HUB
        // =========================================================
        public async Task<IActionResult> NewOrders(
            string? keyword,
            DateTime? fromDate,
            DateTime? toDate,
            int? hubId, // ✅ lọc theo hub phụ trách (HandlingHubId)
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var query = _db.Orders
                .AsNoTracking()
                .Where(o => o.DriverId == null && (o.Status == "pending" || o.Status == "awaiting_assignment"))
                .AsQueryable();

            // ✅ lọc đơn theo HandlingHubId
            if (hubId.HasValue && hubId.Value > 0)
                query = query.Where(o => o.HandlingHubId == hubId.Value);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();
                query = query.Where(o =>
                    (o.Code != null && o.Code.Contains(keyword)) ||
                    (o.SenderName != null && o.SenderName.Contains(keyword)) ||
                    (o.ReceiverName != null && o.ReceiverName.Contains(keyword)) ||
                    (o.SenderPhone != null && o.SenderPhone.Contains(keyword)) ||
                    (o.ReceiverPhone != null && o.ReceiverPhone.Contains(keyword))
                );
            }

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                query = query.Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value >= from);
            }

            if (toDate.HasValue)
            {
                var toExclusive = toDate.Value.Date.AddDays(1);
                query = query.Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value < toExclusive);
            }

            query = query.OrderBy(o => o.CreatedAt);

            var totalItems = await query.CountAsync(ct);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new AdminOrderListItemViewModel
                {
                    Id = o.Id,
                    Code = o.Code ?? "",
                    CustomerName = o.User != null
                        ? (string.IsNullOrEmpty(o.User.UserName) ? "(Khách lẻ)" : o.User.UserName)
                        : "(Khách lẻ)",
                    ReceiverName = o.ReceiverName ?? "",
                    ReceiverPhone = o.ReceiverPhone ?? "",
                    ReceiverAddress = o.ReceiverAddress ?? "",
                    Province = o.Province ?? "",
                    ProductName = o.ProductName ?? "",
                    CodAmount = o.CodAmount,
                    ShipFee = o.ShipFee,
                    Status = o.Status ?? "",
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync(ct);

            ViewBag.Drivers = await _db.Users
                .AsNoTracking()
                .Where(u => u.Role == "driver" || u.Role == "taixe")
                .ToListAsync(ct);

            await LoadActiveHubsToViewBagAsync(ct);

            var vmNew = new AdminOrderListViewModel
            {
                Items = items,
                TotalItems = totalItems,
                PageIndex = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                Keyword = keyword,
                FromDate = fromDate,
                ToDate = toDate,
                HubId = hubId,         // ✅ giữ hub đang lọc để view dùng lại
                StatusFilter = "new"
            };

            return View(vmNew);
        }

        // =========================================================
        // 3) GÁN 1 ĐƠN (gán lẻ)  ✅ nhận hubId để redirect giữ lọc
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignDriverSimple(int orderId, int driverId, int? hubId, CancellationToken ct = default)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null) return NotFound();

            var driver = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == driverId && (u.Role == "driver" || u.Role == "taixe"), ct);

            order.DriverId = driverId;
            order.AssignedAt = DateTime.Now;
            order.DeliveryDate = DateTime.Today;
            order.Status = "assigned";

            await _db.SaveChangesAsync(ct);

            // ✅ Tracking timeline
            var driverName = driver?.UserName;
            if (string.IsNullOrWhiteSpace(driverName)) driverName = $"Tài xế #{driverId}";

            string? hubName = null;
            if (hubId.HasValue && hubId.Value > 0)
            {
                hubName = await _db.Hubs.AsNoTracking()
                    .Where(h => h.Id == hubId.Value)
                    .Select(h => h.Name)
                    .FirstOrDefaultAsync(ct);
            }

            await _tracking.AddAsync(
                orderId: order.Id,
                status: "assigned",
                title: "Đã phân công tài xế",
                note: $"Tài xế: {driverName}",
                location: !string.IsNullOrWhiteSpace(hubName) ? hubName : (order.Province ?? order.ReceiverAddress),
                ct: ct);

            TempData["Success"] = "Gán tài xế thành công.";

            // ✅ giữ hubId đang lọc (nếu có)
            return RedirectToAction(nameof(NewOrders), new { hubId });
        }

        // =========================================================
        // 4) PREVIEW ROUTE – POST (PRG)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("PreviewAssignRoute")]
        public async Task<IActionResult> PreviewAssignRoutePost(
            int driverId,
            int[] orderIds,
            int? hubId,
            string? driverOriginAddress,
            CancellationToken ct)
        {
            if (orderIds == null || orderIds.Length == 0)
            {
                TempData["Error"] = "Vui lòng chọn ít nhất một đơn hàng để phân công.";
                return RedirectToAction(nameof(NewOrders), new { hubId });
            }

            var driver = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == driverId && (u.Role == "driver" || u.Role == "taixe"), ct);

            if (driver == null)
            {
                TempData["Error"] = "Không tìm thấy tài xế.";
                return RedirectToAction(nameof(NewOrders), new { hubId });
            }

            // tracking vì có thể update Lat/Lng
            var fetched = await _db.Orders
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(ct);

            var map = fetched.ToDictionary(o => o.Id);
            var orders = orderIds
                .Select(id => map.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null)
                .Cast<Order>()
                .ToList();

            if (!orders.Any())
            {
                TempData["Error"] = "Không tìm thấy đơn hàng phù hợp.";
                return RedirectToAction(nameof(NewOrders), new { hubId });
            }

            // ✅ CẢNH BÁO: hub xuất phát khác HandlingHubId của đơn (chỉ cảnh báo)
            if (hubId.HasValue && hubId.Value > 0)
            {
                var mismatch = orders
                    .Where(o => o.HandlingHubId.HasValue && o.HandlingHubId.Value > 0)
                    .Where(o => o.HandlingHubId.Value != hubId.Value)
                    .ToList();

                if (mismatch.Any())
                {
                    var sample = string.Join(", ", mismatch.Take(5).Select(o => string.IsNullOrWhiteSpace(o.Code) ? $"#{o.Id}" : o.Code));
                    AddWarning($"⚠️ HUB bạn chọn để xuất phát KHÁC HUB phụ trách của {mismatch.Count}/{orders.Count} đơn. Ví dụ: {sample}" +
                               (mismatch.Count > 5 ? " ..." : ""));
                }
            }

            // ===========================
            // ORIGIN = HUB (ưu tiên)
            // ===========================
            string originDisplay = "";
            string originQuery = "";
            double? originLat = null;
            double? originLng = null;

            string? hubName = null;
            string? hubAddress = null;
            double? hubLat = null;
            double? hubLng = null;

            if (hubId.HasValue && hubId.Value > 0)
            {
                var hub = await _db.Hubs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == hubId.Value && h.IsActive, ct);

                if (hub == null)
                {
                    TempData["Error"] = "Hub không tồn tại hoặc không còn hoạt động.";
                    return RedirectToAction(nameof(NewOrders), new { hubId });
                }

                hubName = hub.Name ?? "";
                hubAddress = hub.Address ?? "";
                hubLat = hub.Lat;
                hubLng = hub.Lng;

                originDisplay = !string.IsNullOrWhiteSpace(hubAddress)
                    ? $"{hubName} - {hubAddress}"
                    : hubName;

                originLat = hubLat;
                originLng = hubLng;

                originQuery = !string.IsNullOrWhiteSpace(hubAddress)
                    ? hubAddress.Trim()
                    : ((originLat.HasValue && originLng.HasValue)
                        ? $"{originLat.Value.ToString(CultureInfo.InvariantCulture)},{originLng.Value.ToString(CultureInfo.InvariantCulture)}"
                        : hubName);

                // hub thiếu lat/lng => geocode 1 lần (cache)
                if ((!originLat.HasValue || !originLng.HasValue) && !string.IsNullOrWhiteSpace(hubAddress))
                {
                    var norm = NormalizeForGeocode(hubAddress);
                    var cacheKey = ("hub|" + hub.Id + "|" + norm).ToLowerInvariant();

                    if (_geoCache.TryGetValue(cacheKey, out var cached))
                    {
                        originLat = cached.lat;
                        originLng = cached.lng;
                    }
                    else
                    {
                        var (olat, olng, oerr) = await GeocodeFallbackAsync(norm, ct, OriginTimeout);
                        if (olat.HasValue && olng.HasValue)
                        {
                            originLat = olat;
                            originLng = olng;
                            _geoCache[cacheKey] = (olat.Value, olng.Value);
                            AddWarning("Hub thiếu Lat/Lng => geocode tạm. Nên lưu Lat/Lng hub vào DB để chạy nhanh hơn.");
                        }
                        else
                        {
                            AddWarning("Hub thiếu Lat/Lng và geocode không được => tuyến tối ưu có thể lệch. (" + (oerr ?? "No result") + ")");
                        }
                    }
                }
            }
            else
            {
                // fallback: origin nhập tay
                var originRaw = (driverOriginAddress ?? "").Trim();
                originDisplay = originRaw;
                originQuery = originRaw;

                if (!string.IsNullOrWhiteSpace(originRaw) && TryParseLatLng(originRaw, out var plat, out var plng))
                {
                    originLat = plat;
                    originLng = plng;
                    originQuery = $"{originLat.Value.ToString(CultureInfo.InvariantCulture)},{originLng.Value.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            // ===========================
            // GEO orders (mượt)
            // ===========================
            if (orders.Count <= 2)
            {
                AddWarning("Chỉ 1–2 đơn: không geocode để chạy nhanh (Google Maps tự tính theo địa chỉ).");
            }
            else
            {
                var (filled, debug) = await EnsureCoordinatesForOrdersAsync(orders, ct);

                if (filled > 0)
                {
                    await _db.SaveChangesAsync(ct);
                    TempData["Success"] = $"Đã tự cập nhật Lat/Lng cho {filled} đơn để tối ưu tuyến.";
                }

                if (!string.IsNullOrWhiteSpace(debug))
                    AddWarning("GEOCODE DEBUG: " + debug);

                // Nếu không chọn hub mà origin là text => geocode origin (optional)
                if (!hubId.HasValue && !string.IsNullOrWhiteSpace(originQuery) && (!originLat.HasValue || !originLng.HasValue))
                {
                    var normOrigin = NormalizeForGeocode(originQuery);
                    var cacheKey = ("origin|" + normOrigin).ToLowerInvariant();

                    if (_geoCache.TryGetValue(cacheKey, out var cachedO))
                    {
                        originLat = cachedO.lat;
                        originLng = cachedO.lng;
                    }
                    else
                    {
                        var (olat, olng, oerr) = await GeocodeFallbackAsync(normOrigin, ct, OriginTimeout);
                        if (olat.HasValue && olng.HasValue)
                        {
                            originLat = olat;
                            originLng = olng;
                            _geoCache[cacheKey] = (olat.Value, olng.Value);
                        }
                        else
                        {
                            AddWarning("Không geocode được origin => tuyến tối ưu có thể lệch. (" + (oerr ?? "No result") + ")");
                        }
                    }
                }
            }

            // ===========================
            // Build VM
            // ===========================
            var vm = new AdminRoutePreviewViewModel
            {
                DriverId = driverId,
                DriverName = string.IsNullOrWhiteSpace(driver.UserName) ? $"Tài xế #{driver.Id}" : driver.UserName,
                DeliveryDate = DateTime.Today,

                HubId = hubId,
                HubName = hubName,
                HubAddress = hubAddress,
                HubLat = originLat ?? hubLat,
                HubLng = originLng ?? hubLng,

                DriverOriginAddress = originDisplay,
                DriverOriginQuery = originQuery
            };

            var withCoord = orders.Where(o => o.Lat.HasValue && o.Lng.HasValue).ToList();
            var noCoord = orders.Where(o => !o.Lat.HasValue || !o.Lng.HasValue).ToList();

            if (orders.Count >= 3)
            {
                if (withCoord.Count == 0) AddWarning("Tất cả đơn thiếu Lat/Lng => không tối ưu theo tọa độ, giữ nguyên thứ tự chọn.");
                else if (noCoord.Count > 0) AddWarning($"Có {noCoord.Count} đơn thiếu Lat/Lng => sẽ xếp cuối (không tối ưu được).");
            }

            List<Order> optimized;
            if (withCoord.Count >= 3 && originLat.HasValue && originLng.HasValue)
                optimized = OptimizeOpenRouteFromOrigin(withCoord, originLat.Value, originLng.Value);
            else if (withCoord.Count >= 3)
                optimized = OptimizeOpenRoute(withCoord);
            else
                optimized = withCoord;

            var finalList = optimized.Concat(noCoord).ToList();

            int seq = 1;
            foreach (var o in finalList)
            {
                vm.Orders.Add(new AdminRouteOrderItemViewModel
                {
                    OrderId = o.Id,
                    Code = o.Code ?? "",
                    ReceiverAddress = o.ReceiverAddress ?? "",
                    Lat = o.Lat,
                    Lng = o.Lng,
                    Sequence = seq++
                });
            }

            vm.GoogleMapsUrl = BuildGoogleMapsDirectionsUrl(vm);

            // ✅ PRG: lưu TempData rồi Redirect sang GET
            TempData[RoutePreviewTempDataKey] = JsonSerializer.Serialize(vm);
            return RedirectToAction(nameof(PreviewAssignRoute));
        }

        // =========================================================
        // 5) CONFIRM ROUTE  ✅ CẢNH BÁO LẦN CUỐI (KHÔNG CHẶN) + ✅ TRACKING
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmAssignRoute(AdminRoutePreviewViewModel model, CancellationToken ct)
        {
            if (model.Orders == null || model.Orders.Count == 0)
            {
                TempData["Error"] = "Không có đơn nào để phân công.";
                return RedirectToAction(nameof(NewOrders), new { hubId = model.HubId });
            }

            if (model.Orders.Select(x => x.Sequence).Distinct().Count() != model.Orders.Count)
            {
                TempData["Error"] = "Sequence bị trùng. Vui lòng xem lại tuyến.";
                return RedirectToAction(nameof(NewOrders), new { hubId = model.HubId });
            }

            var orderIds = model.Orders.Select(o => o.OrderId).ToList();
            var orders = await _db.Orders
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(ct);

            // ✅ cảnh báo hub mismatch (nếu có hubId)
            if (model.HubId.HasValue && model.HubId.Value > 0)
            {
                var mismatch = orders
                    .Where(o => o.HandlingHubId.HasValue && o.HandlingHubId.Value > 0)
                    .Where(o => o.HandlingHubId.Value != model.HubId.Value)
                    .ToList();

                if (mismatch.Any())
                {
                    var sample = string.Join(", ", mismatch.Take(5).Select(o => string.IsNullOrWhiteSpace(o.Code) ? $"#{o.Id}" : o.Code));
                    AddWarning($"⚠️ Xác nhận: HUB tuyến KHÁC HUB phụ trách của {mismatch.Count}/{orders.Count} đơn. Ví dụ: {sample}" +
                               (mismatch.Count > 5 ? " ..." : ""));
                }
            }

            var map = orders.ToDictionary(o => o.Id);

            // driver note
            var driver = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == model.DriverId && (u.Role == "driver" || u.Role == "taixe"), ct);

            var driverName = driver?.UserName;
            if (string.IsNullOrWhiteSpace(driverName)) driverName = $"Tài xế #{model.DriverId}";

            string? hubName = null;
            if (model.HubId.HasValue && model.HubId.Value > 0)
            {
                hubName = await _db.Hubs.AsNoTracking()
                    .Where(h => h.Id == model.HubId.Value)
                    .Select(h => h.Name)
                    .FirstOrDefaultAsync(ct);
            }

            foreach (var item in model.Orders)
            {
                if (!map.TryGetValue(item.OrderId, out var order)) continue;

                order.DriverId = model.DriverId;
                order.DeliveryDate = model.DeliveryDate;
                order.AssignedAt = DateTime.Now;
                order.Status = "assigned";
                order.Sequence = item.Sequence;

                // Nếu bạn muốn “assign hub tuyến” vào order:
                // order.HandlingHubId = model.HubId;
            }

            await _db.SaveChangesAsync(ct);

            // ✅ TRACKING timeline (gán tuyến)
            foreach (var item in model.Orders.OrderBy(x => x.Sequence))
            {
                if (!map.TryGetValue(item.OrderId, out var order)) continue;

                await _tracking.AddAsync(
                    orderId: order.Id,
                    status: "assigned",
                    title: "Đã phân công tài xế",
                    note: $"Tài xế: {driverName}. STT tuyến: {item.Sequence}",
                    location: !string.IsNullOrWhiteSpace(hubName) ? hubName : (order.Province ?? order.ReceiverAddress),
                    ct: ct);
            }

            TempData["Success"] = "Đã phân công tài xế và lưu tuyến.";
            return RedirectToAction(nameof(NewOrders), new { hubId = model.HubId });
        }

        // =========================================================
        // 6) SEARCH
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Search(string code, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(code))
                return RedirectToAction(nameof(Index));

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.Code != null && o.Code.Contains(code))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new AdminOrderListItemViewModel
                {
                    Id = o.Id,
                    Code = o.Code ?? "",
                    CustomerName = o.User != null ? (o.User.UserName ?? "(Khách lẻ)") : "(Khách lẻ)",
                    ReceiverName = o.ReceiverName ?? "",
                    ReceiverPhone = o.ReceiverPhone ?? "",
                    ReceiverAddress = o.ReceiverAddress ?? "",
                    Province = o.Province ?? "",
                    ProductName = o.ProductName ?? "",
                    CodAmount = o.CodAmount,
                    ShipFee = o.ShipFee,
                    Status = o.Status ?? "",
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync(ct);

            return View("Index", new AdminOrderListViewModel { Items = orders });
        }

        // =========================================================
        // 7) CHANGE STATUS ✅ TRACKING
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> ChangeStatus(int id, string status, CancellationToken ct = default)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null)
                return Json(new { ok = false, msg = "Order not found" });

            status = (status ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(status))
                return Json(new { ok = false, msg = "Status is empty" });

            order.Status = status;
            await _db.SaveChangesAsync(ct);

            await _tracking.AddAsync(
                orderId: order.Id,
                status: status,
                title: null,
                note: "Admin cập nhật trạng thái",
                location: order.Province ?? order.ReceiverAddress,
                ct: ct);

            return Json(new { ok = true, msg = "Updated" });
        }

        // =========================================================
        // Fill Lat/Lng + debug - ✅ GIỚI HẠN SỐ ĐƠN
        // =========================================================
        private async Task<(int filled, string debug)> EnsureCoordinatesForOrdersAsync(List<Order> orders, CancellationToken ct)
        {
            int filled = 0;
            var debugMessages = new List<string>();

            var missing = orders
                .Where(o => (!o.Lat.HasValue || !o.Lng.HasValue) && !string.IsNullOrWhiteSpace(o.ReceiverAddress))
                .Take(MaxOrdersToGeocodePerPreview)
                .ToList();

            if (missing.Count == 0) return (0, "");

            if (orders.Count > MaxOrdersToGeocodePerPreview)
                AddWarning($"Đang giới hạn geocode tối đa {MaxOrdersToGeocodePerPreview} đơn/lần preview để tránh chậm.");

            foreach (var o in missing)
            {
                ct.ThrowIfCancellationRequested();

                var raw = (o.ReceiverAddress ?? "").Trim();
                var addr = NormalizeForGeocode(raw);

                var cacheKey = ("addr|" + addr).ToLowerInvariant();
                if (_geoCache.TryGetValue(cacheKey, out var cached))
                {
                    o.Lat = cached.lat;
                    o.Lng = cached.lng;
                    filled++;
                    continue;
                }

                var (lat, lng, err) = await GeocodeFallbackAsync(addr, ct, PerRequestTimeout);

                if (lat.HasValue && lng.HasValue)
                {
                    o.Lat = lat;
                    o.Lng = lng;
                    _geoCache[cacheKey] = (lat.Value, lng.Value);
                    filled++;
                }
                else
                {
                    if (debugMessages.Count < 2)
                        debugMessages.Add($"[{o.Code ?? o.Id.ToString()}] {err ?? "No result"}");
                }
            }

            return (filled, string.Join(" | ", debugMessages));
        }

        private async Task<(double? lat, double? lng, string? err)> GeocodeFallbackAsync(
            string address,
            CancellationToken ct,
            TimeSpan timeoutPerTry)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, "Empty address");

            string lastErr = "No result";

            var candidates = BuildGeocodeCandidates(address)
                .Take(MaxCandidatesToTry)
                .ToList();

            foreach (var cand in candidates)
            {
                var r1 = await GeocodeByNominatimAsync(cand, ct, useCountryVn: true, timeoutPerTry);
                if (r1.lat.HasValue && r1.lng.HasValue) return r1;
                if (!string.IsNullOrWhiteSpace(r1.err)) lastErr = r1.err!;

                var r2 = await GeocodeByNominatimAsync(cand, ct, useCountryVn: false, timeoutPerTry);
                if (r2.lat.HasValue && r2.lng.HasValue) return r2;
                if (!string.IsNullOrWhiteSpace(r2.err)) lastErr = r2.err!;
            }

            return (null, null, lastErr);
        }

        private IEnumerable<string> BuildGeocodeCandidates(string address)
        {
            address = NormalizeForGeocode(address);

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string s)
            {
                s = (s ?? "").Trim().Trim(',');
                if (string.IsNullOrWhiteSpace(s)) return;
                if (seen.Add(s)) list.Add(s);
            }

            Add(address + ", Việt Nam");
            Add(address);

            var stripped = StripVietnameseAdministrative(address);
            Add(stripped + ", Hồ Chí Minh, Việt Nam");
            Add(stripped);

            var noNumber = RemoveLeadingHouseNumber(stripped);
            Add(noNumber + ", Hồ Chí Minh, Việt Nam");
            Add(noNumber);

            var nodiac = RemoveDiacritics(address);
            Add(nodiac + ", Viet Nam");

            return list;
        }

        private async Task<(double? lat, double? lng, string? err)> GeocodeByNominatimAsync(
            string address,
            CancellationToken ct,
            bool useCountryVn,
            TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, "Empty address");

            await _geoGate.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                var needWait = (_lastGeoCallUtc + _minGeoInterval) - now;
                if (needWait > TimeSpan.Zero)
                    await Task.Delay(needWait, ct);
                _lastGeoCallUtc = DateTime.UtcNow;

                var url =
                    "search?format=jsonv2&limit=1&addressdetails=0" +
                    (useCountryVn ? "&countrycodes=vn" : "") +
                    "&email=" + Uri.EscapeDataString(GeoEmail) +
                    "&q=" + Uri.EscapeDataString(address);

                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(timeout);

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.Clear();
                    req.Headers.UserAgent.ParseAdd(GeoUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8");

                    using var res = await _nominatim.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);

                    if (res.StatusCode == (HttpStatusCode)429)
                    {
                        await Task.Delay(600, ct);
                        continue;
                    }

                    var body = await res.Content.ReadAsStringAsync(linked.Token);

                    if (res.StatusCode == HttpStatusCode.Forbidden)
                        return (null, null, "403 Forbidden (Nominatim chặn) - kiểm tra User-Agent / tần suất.");

                    if (!res.IsSuccessStatusCode)
                        return (null, null, $"HTTP {(int)res.StatusCode}: {Trim160(body)}");

                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                            return (null, null, "No result");

                        var first = root[0];
                        var latStr = first.GetProperty("lat").GetString();
                        var lonStr = first.GetProperty("lon").GetString();

                        if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
                            return (null, null, "Parse lat fail");

                        if (!double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
                            return (null, null, "Parse lon fail");

                        return (lat, lng, null);
                    }
                    catch (JsonException)
                    {
                        return (null, null, "Non-JSON response (có thể bị chặn/proxy).");
                    }
                }

                return (null, null, "429 Too Many Requests");
            }
            catch (TaskCanceledException)
            {
                return (null, null, "Timeout");
            }
            catch (Exception ex)
            {
                return (null, null, ex.Message);
            }
            finally
            {
                _geoGate.Release();
            }
        }

        private static string Trim160(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 160 ? s : s.Substring(0, 160);
        }

        // =========================================================
        // ✅ GOOGLE MAPS URL
        // =========================================================
        private string BuildGoogleMapsDirectionsUrl(AdminRoutePreviewViewModel vm)
        {
            var ordered = vm.Orders.OrderBy(o => o.Sequence).ToList();
            if (ordered.Count == 0) return string.Empty;

            string Encode(string s) => Uri.EscapeDataString((s ?? "").Trim());

            string StopToQuery(AdminRouteOrderItemViewModel o)
            {
                var addr = (o.ReceiverAddress ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(addr)) return addr;

                if (o.Lat.HasValue && o.Lng.HasValue)
                    return $"{o.Lat.Value.ToString(CultureInfo.InvariantCulture)},{o.Lng.Value.ToString(CultureInfo.InvariantCulture)}";

                return "";
            }

            var stops = ordered.Select(StopToQuery).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (stops.Count == 0) return string.Empty;

            var originRaw = (vm.DriverOriginQuery ?? vm.DriverOriginAddress ?? "").Trim();

            if (stops.Count == 1)
            {
                if (string.IsNullOrWhiteSpace(originRaw))
                    return $"https://www.google.com/maps/search/?api=1&query={Encode(stops[0])}";

                return $"https://www.google.com/maps/dir/?api=1&travelmode=driving" +
                       $"&origin={Encode(originRaw)}&destination={Encode(stops[0])}";
            }

            if (!string.IsNullOrWhiteSpace(originRaw))
            {
                var destination = stops.Last();
                var waypoints = stops.Take(stops.Count - 1).Select(Encode).ToList();

                var url = $"https://www.google.com/maps/dir/?api=1&travelmode=driving" +
                          $"&origin={Encode(originRaw)}&destination={Encode(destination)}";

                if (waypoints.Any())
                    url += $"&waypoints={string.Join("|", waypoints)}";

                return url;
            }
            else
            {
                var origin = stops.First();
                var destination = stops.Last();
                var middle = stops.Skip(1).Take(stops.Count - 2).Select(Encode).ToList();

                var url = $"https://www.google.com/maps/dir/?api=1&travelmode=driving" +
                          $"&origin={Encode(origin)}&destination={Encode(destination)}";

                if (middle.Any())
                    url += $"&waypoints={string.Join("|", middle)}";

                return url;
            }
        }

        // =========================================================
        // ROUTE OPTIMIZE
        // =========================================================
        private static List<Order> OptimizeOpenRoute(List<Order> orders)
        {
            if (orders.Count <= 2) return orders;

            var remaining = new List<Order>(orders);
            var route = new List<Order>();

            var current = remaining[0];
            route.Add(current);
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                var next = remaining
                    .OrderBy(o => HaversineKm(current.Lat!.Value, current.Lng!.Value, o.Lat!.Value, o.Lng!.Value))
                    .First();

                route.Add(next);
                remaining.Remove(next);
                current = next;
            }

            TwoOptImproveKeepFirst(route);
            return route;
        }

        private static List<Order> OptimizeOpenRouteFromOrigin(List<Order> stops, double originLat, double originLng)
        {
            if (stops.Count <= 2) return stops;

            var remaining = new List<Order>(stops);
            var route = new List<Order>();

            var first = remaining
                .OrderBy(o => HaversineKm(originLat, originLng, o.Lat!.Value, o.Lng!.Value))
                .First();

            route.Add(first);
            remaining.Remove(first);

            var current = first;
            while (remaining.Count > 0)
            {
                var next = remaining
                    .OrderBy(o => HaversineKm(current.Lat!.Value, current.Lng!.Value, o.Lat!.Value, o.Lng!.Value))
                    .First();

                route.Add(next);
                remaining.Remove(next);
                current = next;
            }

            TwoOptImproveKeepFirst(route);
            return route;
        }

        private static void TwoOptImproveKeepFirst(List<Order> route)
        {
            int n = route.Count;
            if (n <= 3) return;

            bool improved = true;
            int guard = 0;

            while (improved && guard++ < 40)
            {
                improved = false;

                for (int i = 0; i < n - 3; i++)
                {
                    for (int k = i + 1; k < n - 1; k++)
                    {
                        var a = route[i];
                        var b = route[i + 1];
                        var c = route[k];
                        var d = route[k + 1];

                        double currentDist =
                            HaversineKm(a.Lat!.Value, a.Lng!.Value, b.Lat!.Value, b.Lng!.Value) +
                            HaversineKm(c.Lat!.Value, c.Lng!.Value, d.Lat!.Value, d.Lng!.Value);

                        double newDist =
                            HaversineKm(a.Lat!.Value, a.Lng!.Value, c.Lat!.Value, c.Lng!.Value) +
                            HaversineKm(b.Lat!.Value, b.Lng!.Value, d.Lat!.Value, d.Lng!.Value);

                        if (newDist + 1e-9 < currentDist)
                        {
                            route.Reverse(i + 1, k - i);
                            improved = true;
                        }
                    }
                }
            }
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        // =========================================================
        // Parse "lat,lng" (hoặc "lat lng")
        // =========================================================
        private static bool TryParseLatLng(string input, out double lat, out double lng)
        {
            lat = 0; lng = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var s = input.Trim().Replace(";", ",");

            var parts = s.Contains(',')
                ? s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length != 2) return false;

            if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out lat)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out lng)) return false;

            if (lat < -90 || lat > 90) return false;
            if (lng < -180 || lng > 180) return false;

            return true;
        }

        // =========================================================
        // Helpers normalize
        // =========================================================
        private static string NormalizeForGeocode(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return address;

            var s = address.Trim();

            s = s.Replace("TP.HCM", "Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
                 .Replace("TP. HCM", "Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
                 .Replace("TPHCM", "Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
                 .Replace("HCM", "Hồ Chí Minh", StringComparison.OrdinalIgnoreCase);

            s = s.Replace("Q.", "Quận", StringComparison.OrdinalIgnoreCase)
                 .Replace("P.", "Phường", StringComparison.OrdinalIgnoreCase);

            s = Regex.Replace(s, @"\s+", " ");
            while (s.Contains(",,")) s = s.Replace(",,", ",");

            return s.Trim().Trim(',');
        }

        private static string StripVietnameseAdministrative(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return address;

            var parts = address.Split(',')
                               .Select(x => x.Trim())
                               .Where(x => !string.IsNullOrWhiteSpace(x))
                               .ToList();

            parts = parts.Where(p =>
            {
                var t = p.ToLowerInvariant();

                if (t.Contains("phường")) return false;
                if (t.StartsWith("p ")) return false;

                if (t.Contains("quận")) return false;
                if (Regex.IsMatch(t, @"^q\.?\s*\d+")) return false;

                if (t.Contains("huyện")) return false;

                if (t.Contains("hồ chí minh") || t.Contains("ho chi minh")) return true;

                if (t.Contains("thành phố")) return false;
                if (t.StartsWith("tp")) return false;

                return true;
            }).ToList();

            return string.Join(", ", parts);
        }

        private static string RemoveLeadingHouseNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return Regex.Replace(text.Trim(), @"^\s*\d+\s*[-/\\]?\s*", "", RegexOptions.CultureInvariant);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    // ========================= VIEW MODELS =========================
    public class AdminOrderListItemViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ReceiverName { get; set; } = "";
        public string ReceiverPhone { get; set; } = "";
        public string ReceiverAddress { get; set; } = "";
        public string Province { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal CodAmount { get; set; }
        public decimal ShipFee { get; set; }
        public string Status { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
    }

    public class AdminOrderStatusStat
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    public class AdminOrderListViewModel
    {
        public List<AdminOrderListItemViewModel> Items { get; set; } = new();
        public string? StatusFilter { get; set; }
        public string? Keyword { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public int? HubId { get; set; } // ✅ GIỮ HUB ĐANG LỌC (HandlingHubId)

        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<AdminOrderStatusStat> StatusStats { get; set; } = new();
    }

    public class AdminRouteOrderItemViewModel
    {
        public int OrderId { get; set; }
        public string Code { get; set; } = "";
        public string ReceiverAddress { get; set; } = "";
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public int Sequence { get; set; }
    }

    public class AdminRoutePreviewViewModel
    {
        public int DriverId { get; set; }
        public string DriverName { get; set; } = "";
        public DateTime DeliveryDate { get; set; }
        public List<AdminRouteOrderItemViewModel> Orders { get; set; } = new();

        public int? HubId { get; set; }
        public string? HubName { get; set; }
        public string? HubAddress { get; set; }
        public double? HubLat { get; set; }
        public double? HubLng { get; set; }

        public string? DriverOriginAddress { get; set; }
        public string? DriverOriginQuery { get; set; }

        public string GoogleMapsUrl { get; set; } = "";
    }

    public class HubOptionVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Address { get; set; } = "";
        public double? Lat { get; set; }
        public double? Lng { get; set; }
    }
}
