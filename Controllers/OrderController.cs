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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using WedNightFury.Services;

namespace WedNightFury.Controllers
{
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _nominatim;
        private readonly IOrderTrackingService _tracking;

        // ===== GEO CONFIG =====
        private const string GeoEmail = "hoainam1872004@gmail.com";
        private const string GeoUserAgent = "WedNightFury/1.0 (contact: hoainam1872004@gmail.com)";

        private const bool RequireReceiverCoordinates = true;
        private const bool WarnOnDistrictMismatch = false;

        private static readonly ConcurrentDictionary<string, (double lat, double lng)> _geoCache = new();
        private static readonly ConcurrentDictionary<string, string> _revCache = new();
        private static readonly ConcurrentDictionary<string, DateTime> _geoFailCache = new();
        private static readonly TimeSpan GeoFailTtl = TimeSpan.FromMinutes(10);

        // Rate limit Nominatim: ~1 req/s
        private static readonly SemaphoreSlim _geoGate = new(1, 1);
        private static DateTime _lastGeoCallUtc = DateTime.MinValue;
        private static readonly TimeSpan _minGeoInterval = TimeSpan.FromMilliseconds(1100);

        private const int MaxRetries = 1;
        private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(10);

        // ===== HUB CACHE =====
        private record HubMini(int Id, string Name, string Address, double? Lat, double? Lng);
        private static readonly SemaphoreSlim _hubCacheGate = new(1, 1);
        private static (DateTime utc, List<HubMini> hubs)? _hubCache;
        private static readonly TimeSpan HubCacheTtl = TimeSpan.FromMinutes(5);

        public OrderController(
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            IOrderTrackingService tracking)
        {
            _context = context;
            _tracking = tracking;

            _nominatim = httpClientFactory.CreateClient("nominatim");
            _nominatim.Timeout = Timeout.InfiniteTimeSpan;
        }

        // =========================================================
        // PUBLIC TRACKING (KHÔNG CẦN ĐĂNG NHẬP)
        // URL: /Track  (GET hiển thị form)
        //      /Track  (POST tra cứu)
        // =========================================================

        [AllowAnonymous]
        [HttpGet("/Track")]
        public IActionResult Track()
        {
            return View("Track");
        }

        [AllowAnonymous]
        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Find(string keyword)
{
    keyword = (keyword ?? "").Trim();

    if (string.IsNullOrWhiteSpace(keyword))
    {
        ViewBag.Message = "Vui lòng nhập mã đơn.";
        return View("Index"); // hoặc View("Track") tùy tên view
    }

    // Chỉ tra theo mã đơn
    var code = keyword.ToLower();

    var orders = await _context.Orders
        .Where(o => o.Code != null && o.Code.ToLower() == code)
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync();

    if (orders.Count == 0)
    {
        ViewBag.Message = "Không có dữ liệu phù hợp với mã bạn nhập.";
        return View("Index"); // hoặc view tra cứu
    }

    return View("TrackResult", orders);
}

        // =========================================================
        // FORM HELPERS
        // =========================================================
        private string GetFirstFormValue(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (Request.Form.TryGetValue(k, out var v))
                {
                    var s = v.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
            return "";
        }

        private static string NormLower(string? s) => (s ?? "").Trim().ToLowerInvariant();

        // =========================================================
        // HUBS (cached)
        // =========================================================
        private async Task<List<HubMini>> GetActiveHubsCachedAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (_hubCache.HasValue && (now - _hubCache.Value.utc) < HubCacheTtl)
                return _hubCache.Value.hubs;

            await _hubCacheGate.WaitAsync(ct);
            try
            {
                now = DateTime.UtcNow;
                if (_hubCache.HasValue && (now - _hubCache.Value.utc) < HubCacheTtl)
                    return _hubCache.Value.hubs;

                var hubs = await _context.Hubs
                    .AsNoTracking()
                    .Where(h => h.IsActive)
                    .OrderBy(h => h.Name)
                    .Select(h => new HubMini(h.Id, h.Name ?? "", h.Address ?? "", (double?)h.Lat, (double?)h.Lng))
                    .ToListAsync(ct);

                _hubCache = (DateTime.UtcNow, hubs);
                return hubs;
            }
            finally
            {
                _hubCacheGate.Release();
            }
        }

        private async Task LoadActiveHubsToViewBagAsync(CancellationToken ct)
        {
            var hubs = await GetActiveHubsCachedAsync(ct);
            ViewBag.Hubs = hubs.Select(h => new { h.Id, h.Name, h.Address }).ToList();
        }

        // =========================================================
        // PROMO (DB: giamgia)
        // =========================================================
        private static decimal? GetPromoCapVnd(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            if (string.Equals(code.Trim(), "FREESHIP10K", StringComparison.OrdinalIgnoreCase))
                return 10000m;

            return null;
        }

        private async Task<(GiamGia? promo, string? err)> ValidatePromoFromDbAsync(string? code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (null, null);

            code = code.Trim();
            var now = DateTime.Now;

            var promo = await _context.Set<GiamGia>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Trangthai == true &&
                    x.Ngaybatdau <= now &&
                    x.Ngayketthuc >= now &&
                    x.Code != null &&
                    x.Code.ToLower() == code.ToLower(), ct);

            if (promo == null)
                return (null, "Mã giảm giá không tồn tại hoặc đã hết hạn.");

            if (promo.Giatriphantram <= 0 || promo.Giatriphantram > 100)
                return (null, "Mã giảm giá cấu hình không hợp lệ.");

            return (promo, null);
        }

        [HttpGet]
        public async Task<IActionResult> ValidatePromo(string code, CancellationToken ct)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized(new { ok = false, message = "Chưa đăng nhập." });

            code = (code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
                return Ok(new { ok = false, message = "Vui lòng nhập mã." });

            var (promo, err) = await ValidatePromoFromDbAsync(code, ct);
            if (promo == null)
                return Ok(new { ok = false, message = err ?? "Mã không hợp lệ." });

            return Ok(new
            {
                ok = true,
                id = promo.Magiamgia,
                code = promo.Code,
                percent = promo.Giatriphantram,
                cap = GetPromoCapVnd(promo.Code)
            });
        }

        // =========================================================
        // CHI TIẾT ĐƠN HÀNG (khách đã đăng nhập)
        // =========================================================
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.HandlingHub)
                .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == userId);

            if (order == null) return NotFound();
            return View(order);
        }

        // =========================================================
        // QUẢN LÝ VẬN ĐƠN
        // =========================================================
        public async Task<IActionResult> Manage(DateTime? startDate, DateTime? endDate, string status = "all")
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var query = _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == userId);

            if (startDate.HasValue)
            {
                var from = startDate.Value.Date;
                query = query.Where(o => o.CreatedAt >= from);
            }

            if (endDate.HasValue)
            {
                var to = endDate.Value.Date.AddDays(1);
                query = query.Where(o => o.CreatedAt < to);
            }

            if (!string.IsNullOrEmpty(status) && status != "all")
                query = query.Where(o => o.Status == status);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.TotalOrders = orders.Count;
            ViewBag.PendingOrders = orders.Count(o => o.Status == "pending");
            ViewBag.ShippingOrders = orders.Count(o => o.Status == "shipping");
            ViewBag.DoneOrders = orders.Count(o => o.Status == "done");
            ViewBag.CancelledOrders = orders.Count(o => o.Status == "cancelled");

            ViewBag.StartDate = startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Status = status;

            return View(orders);
        }

        // =========================================================
        // XUẤT EXCEL (CSV)
        // =========================================================
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate, string status = "all")
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var query = _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == userId);

            if (startDate.HasValue)
            {
                var from = startDate.Value.Date;
                query = query.Where(o => o.CreatedAt >= from);
            }

            if (endDate.HasValue)
            {
                var to = endDate.Value.Date.AddDays(1);
                query = query.Where(o => o.CreatedAt < to);
            }

            if (!string.IsNullOrEmpty(status) && status != "all")
                query = query.Where(o => o.Status == status);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("OrderId,Code,SenderName,ReceiverName,Value,ShipFee,DiscountCode,DiscountAmount,Status,CreatedAt");

            foreach (var o in orders)
            {
                var createdAtText = FormatDateTimeSafe(o.CreatedAt, "yyyy-MM-dd HH:mm:ss");

                sb.AppendLine(string.Join(",", new[]
                {
                    o.Id.ToString(),
                    EscapeCsv(o.Code),
                    EscapeCsv(o.SenderName),
                    EscapeCsv(o.ReceiverName),
                    FormatMoneyAsText(o.Value),
                    FormatMoneyAsText(o.ShipFee),
                    EscapeCsv(o.DiscountCode),
                    FormatMoneyAsText(o.DiscountAmount),
                    EscapeCsv(o.Status),
                    createdAtText
                }));
            }

            var csv = sb.ToString();

            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var preamble = utf8WithBom.GetPreamble();
            var body = utf8WithBom.GetBytes(csv);

            var bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);

            var fileName = $"orders_{DateTime.Now:yyyyMMddHHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // =========================================================
        // GET /Order/Create
        // =========================================================
        public async Task<IActionResult> Create(CancellationToken ct)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var model = new Order();
            LoadSenderInfo(userId.Value, model);

            await LoadActiveHubsToViewBagAsync(ct);
            return View(model);
        }

        private void LoadSenderInfo(int userId, Order model)
        {
            var profile = _context.Profiles.AsNoTracking().FirstOrDefault(p => p.UserId == userId);
            if (profile != null)
            {
                model.SenderName = profile.FullName;
                model.SenderPhone = profile.Phone;
                model.SenderAddress = profile.Address;
                ViewBag.SenderCity = profile.City ?? "";
            }
            else
            {
                ViewBag.SenderCity = "";
            }
        }

        // =========================================================
        // Haversine + SuggestHubByGeo
        // =========================================================
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

        private int? SuggestHubByGeo(IReadOnlyList<HubMini> hubs, double lat, double lng)
        {
            var candidates = hubs
                .Where(h => h.Lat.HasValue && h.Lng.HasValue)
                .Select(h => new { h.Id, Dist = HaversineKm(lat, lng, h.Lat!.Value, h.Lng!.Value) })
                .OrderBy(x => x.Dist)
                .ToList();

            return candidates.Count == 0 ? null : candidates[0].Id;
        }

        // =========================================================
        // TÍNH PHÍ SHIP SERVER-SIDE (GIỐNG JS CLIENT)
        // =========================================================
        private decimal CalculateShipFeeSameAsClient(string? areaType, string? pickupMethod, string? serviceLevel, decimal weightKg)
        {
            if (weightKg <= 0) return 0;

            var area = NormLower(areaType);
            var method = NormLower(pickupMethod);
            var service = NormLower(serviceLevel);

            decimal basePerKg = area == "inner" ? 15000m : 20000m;
            decimal fee = basePerKg * weightKg;

            if (method == "pickup")
                fee += 10000m;

            decimal factor = 1.0m;
            if (service == "fast") factor = 1.2m;
            if (service == "express") factor = 1.5m;

            fee = fee * factor;
            fee = Math.Round(fee, 0, MidpointRounding.AwayFromZero);

            return fee < 0 ? 0 : fee;
        }

        // =========================================================
        // POST /Order/Create
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order model, CancellationToken ct)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            LoadSenderInfo(userId.Value, model);
            await LoadActiveHubsToViewBagAsync(ct);

            model.GoodsType = GetFirstFormValue("GoodsType");
            model.AreaType = GetFirstFormValue("AreaType");
            model.PickupMethod = GetFirstFormValue("PickupMethod");
            model.ServiceLevel = GetFirstFormValue("ServiceLevel");
            model.ShipPayer = GetFirstFormValue("ShipPayer");

            model.Value = ParseDecimal(GetFirstFormValue("Value"));
            model.CodAmount = ParseDecimal(GetFirstFormValue("CodAmount"));

            if (model.Weight < 0) model.Weight = 0;

            ModelState.Remove(nameof(Order.Value));
            ModelState.Remove(nameof(Order.CodAmount));
            ModelState.Remove(nameof(Order.ShipFee));
            ModelState.Remove(nameof(Order.DiscountAmount));
            ModelState.Remove(nameof(Order.DiscountCode));

            string province = (GetFirstFormValue("Province") ?? "").Trim();
            string district = (GetFirstFormValue("District") ?? "").Trim();
            string ward = (GetFirstFormValue("Ward") ?? "").Trim();
            model.Province = province;

            bool hasPin = model.Lat.HasValue && model.Lng.HasValue && IsValidLatLng(model.Lat.Value, model.Lng.Value);
            bool hasAddressText = !string.IsNullOrWhiteSpace(model.ReceiverAddress);

            if (!hasPin && !hasAddressText)
            {
                ModelState.AddModelError("", "Vui lòng CHỌN vị trí trên bản đồ (Lat/Lng) hoặc nhập địa chỉ nhận.");
                return View(model);
            }

            if (hasPin && !hasAddressText)
            {
                var (display, err) = await ReverseGeocodeAsync(model.Lat!.Value, model.Lng!.Value, ct);

                if (!string.IsNullOrWhiteSpace(display))
                {
                    model.ReceiverAddress = NormalizeReceiverAddress(display, ward, district, province);
                }
                else
                {
                    var approxAdmin = BuildAdminOnlyText(ward, district, province);
                    model.ReceiverAddress =
                        $"Pinned location ({model.Lat.Value.ToString(CultureInfo.InvariantCulture)}, {model.Lng.Value.ToString(CultureInfo.InvariantCulture)})" +
                        (string.IsNullOrWhiteSpace(approxAdmin) ? "" : $" - {approxAdmin}");

                    TempData["Warning"] =
                        "Không reverse được địa chỉ từ bản đồ. Đơn vẫn tạo với tọa độ pin." +
                        (string.IsNullOrWhiteSpace(err) ? "" : $" (Lý do: {err})");
                }
            }

            model.ReceiverAddress = NormalizeReceiverAddress(model.ReceiverAddress, ward, district, province);

            var hubs = await GetActiveHubsCachedAsync(ct);
            if (hubs.Count == 0)
            {
                ModelState.AddModelError("", "Không có Hub hoạt động.");
                return View(model);
            }

            int? hubIdCommon =
                TryParseInt(GetFirstFormValue("HubId")) ??
                TryParseInt(GetFirstFormValue("hubId"));

            var pm = NormLower(model.PickupMethod);
            bool isDropoffHub = pm == "dropoff_hub" || pm == "self_dropoff" || pm == "tudem" || pm == "tu_dem_den_hub";

            if (isDropoffHub)
            {
                if (!hubIdCommon.HasValue || hubIdCommon.Value <= 0)
                {
                    ModelState.AddModelError("", "Bạn chọn 'Tự đem đến hub' thì phải chọn Hub.");
                    return View(model);
                }

                model.DropoffHubId = hubIdCommon.Value;
                model.HandlingHubId = hubIdCommon.Value;
                model.DropoffHub = hubs.FirstOrDefault(h => h.Id == hubIdCommon.Value)?.Name;
            }

            if (!hasPin)
            {
                var addrRaw = (model.ReceiverAddress ?? "").Trim();

                var (lat, lng, err) = await ResolveReceiverCoordinatesAsync(
                    addrRaw: addrRaw,
                    province: province,
                    district: district,
                    ward: ward,
                    ct: ct);

                model.Lat = lat;
                model.Lng = lng;

                if (WarnOnDistrictMismatch && lat.HasValue && lng.HasValue && !string.IsNullOrWhiteSpace(err))
                    TempData["Warning"] = err;

                if (!model.Lat.HasValue || !model.Lng.HasValue)
                {
                    var msg =
                        "Không lấy được tọa độ (Lat/Lng) cho địa chỉ nhận. " +
                        (string.IsNullOrWhiteSpace(err) ? "" : $"(Lý do: {err})");

                    if (RequireReceiverCoordinates)
                    {
                        ModelState.AddModelError("", msg);
                        return View(model);
                    }

                    TempData["Warning"] = msg + " Đơn vẫn được tạo, admin sẽ cập nhật sau.";
                }
            }

            if (!isDropoffHub)
            {
                if (model.Lat.HasValue && model.Lng.HasValue)
                {
                    var nearest = SuggestHubByGeo(hubs, model.Lat.Value, model.Lng.Value);
                    model.HandlingHubId = nearest ?? hubs[0].Id;
                }
                else
                {
                    model.HandlingHubId = hubs[0].Id;
                }

                model.DropoffHubId = null;
                model.DropoffHub = null;
            }

            if (!model.HandlingHubId.HasValue || !hubs.Any(h => h.Id == model.HandlingHubId.Value))
            {
                ModelState.AddModelError("", "Hub phụ trách không hợp lệ hoặc không còn hoạt động.");
                return View(model);
            }

            var shipFeeBase = CalculateShipFeeSameAsClient(
                areaType: model.AreaType,
                pickupMethod: model.PickupMethod,
                serviceLevel: model.ServiceLevel,
                weightKg: model.Weight
            );

            var promoCode = GetFirstFormValue("PromoCode");

            GiamGia? promo = null;
            string? promoErr = null;

            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                (promo, promoErr) = await ValidatePromoFromDbAsync(promoCode, ct);
                if (promo == null)
                {
                    ModelState.AddModelError("", promoErr ?? "Mã giảm giá không hợp lệ.");
                    return View(model);
                }
            }

            decimal discountAmount = 0m;
            decimal shipFeeFinal = shipFeeBase;

            if (promo != null && shipFeeBase > 0)
            {
                int percent = promo.Giatriphantram;
                decimal? cap = GetPromoCapVnd(promo.Code);

                discountAmount = Math.Round(shipFeeBase * percent / 100m, 0, MidpointRounding.AwayFromZero);

                if (cap.HasValue && discountAmount > cap.Value)
                    discountAmount = cap.Value;

                if (discountAmount < 0) discountAmount = 0;
                if (discountAmount > shipFeeBase) discountAmount = shipFeeBase;

                shipFeeFinal = shipFeeBase - discountAmount;
                if (shipFeeFinal < 0) shipFeeFinal = 0;

                model.DiscountCode = promo.Code?.Trim();
                model.DiscountAmount = discountAmount;
            }
            else
            {
                model.DiscountCode = null;
                model.DiscountAmount = 0m;
            }

            model.ShipFee = shipFeeFinal;

            model.Code = $"NF-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
            model.Status = "pending";
            model.CreatedAt = DateTime.Now;
            model.CustomerId = userId.Value;

            _context.Orders.Add(model);
            await _context.SaveChangesAsync(ct);

            await _tracking.AddAsync(
                orderId: model.Id,
                status: "pending",
                title: "Đã tạo đơn",
                note: $"Đơn {model.Code} đã được tạo.",
                location: string.IsNullOrWhiteSpace(model.Province) ? model.ReceiverAddress : model.Province,
                ct: ct);

            TempData["OrderId"] = model.Id;
            TempData["OrderCode"] = model.Code;

            return RedirectToAction(nameof(Success));
        }

        public IActionResult Success()
        {
            ViewBag.OrderId = TempData["OrderId"];
            ViewBag.OrderCode = TempData["OrderCode"];
            ViewBag.Warning = TempData["Warning"];
            return View();
        }

        // =========================================================
        // Reverse geocode
        // =========================================================
        private async Task<(string? displayName, string? err)> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct)
        {
            var key = $"rev|{Math.Round(lat, 6).ToString(CultureInfo.InvariantCulture)}|{Math.Round(lng, 6).ToString(CultureInfo.InvariantCulture)}";
            if (_revCache.TryGetValue(key, out var cached))
                return (cached, null);

            await _geoGate.WaitAsync(ct);
            try
            {
                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    var now = DateTime.UtcNow;
                    var needWait = (_lastGeoCallUtc + _minGeoInterval) - now;
                    if (needWait > TimeSpan.Zero) await Task.Delay(needWait, ct);
                    _lastGeoCallUtc = DateTime.UtcNow;

                    var url =
                        "reverse?format=jsonv2&addressdetails=1&zoom=18" +
                        "&email=" + Uri.EscapeDataString(GeoEmail) +
                        "&lat=" + lat.ToString(CultureInfo.InvariantCulture) +
                        "&lon=" + lng.ToString(CultureInfo.InvariantCulture);

                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(PerRequestTimeout);

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.Clear();
                    req.Headers.UserAgent.ParseAdd(GeoUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8");

                    using var res = await _nominatim.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);

                    if (res.StatusCode == (HttpStatusCode)429)
                    {
                        await Task.Delay(1200, ct);
                        continue;
                    }

                    var body = await res.Content.ReadAsStringAsync(linked.Token);

                    if (!res.IsSuccessStatusCode)
                        return (null, $"HTTP {(int)res.StatusCode}: {Trim160(body)}");

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    var display = root.TryGetProperty("display_name", out var dispEl) ? dispEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(display))
                    {
                        display = NormalizeForGeocode(display);
                        _revCache[key] = display;
                        return (display, null);
                    }

                    return (null, "No display_name");
                }

                return (null, "429 Too Many Requests");
            }
            catch (TaskCanceledException)
            {
                return (null, "Timeout");
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
            finally
            {
                _geoGate.Release();
            }
        }

        // =========================================================
        // GEO Resolve
        // =========================================================
        private async Task<(double? lat, double? lng, string? err)> ResolveReceiverCoordinatesAsync(
            string addrRaw,
            string province,
            string district,
            string ward,
            CancellationToken ct)
        {
            if (TryParseLatLng(addrRaw, out var plat, out var plng))
                return (plat, plng, null);

            string normalized = NormalizeForGeocode(addrRaw);

            var cacheKey = ("addr|" + NormalizeKey(normalized) + "|" +
                            NormalizeKey(province) + "|" + NormalizeKey(district) + "|" + NormalizeKey(ward))
                .ToLowerInvariant();

            if (_geoFailCache.TryGetValue(cacheKey, out var failAt) && (DateTime.UtcNow - failAt) < GeoFailTtl)
                return (null, null, "Gần đây địa chỉ này geocode thất bại. Hãy nhập chi tiết hơn (số nhà + tên đường / tên tòa nhà).");

            if (_geoCache.TryGetValue(cacheKey, out var ok))
                return (ok.lat, ok.lng, null);

            if (IsTooVagueForGeocode(normalized))
            {
                _geoFailCache[cacheKey] = DateTime.UtcNow;
                return (null, null,
                    "Địa chỉ quá mơ hồ nên không thể lấy tọa độ chính xác. " +
                    "Vui lòng nhập thêm SỐ NHÀ + TÊN ĐƯỜNG hoặc TÊN TÒA NHÀ/ĐỊA DANH cụ thể.");
            }

            var r = await GeocodeFastAsync(normalized, province, district, ward, ct);
            if (r.lat.HasValue && r.lng.HasValue)
            {
                _geoCache[cacheKey] = (r.lat.Value, r.lng.Value);
                return r;
            }

            _geoFailCache[cacheKey] = DateTime.UtcNow;
            return (null, null, r.err ?? "No result");
        }

        private async Task<(double? lat, double? lng, string? err)> GeocodeFastAsync(
            string address,
            string province,
            string district,
            string ward,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, "Empty address");

            string cand1 = BuildOneBestCandidate(address, ward, district, province);
            string cand2 = RemoveDiacritics(cand1);

            var r = await GeocodeOneShotAsync(cand1, ct);
            if (r.lat.HasValue && r.lng.HasValue) return r;

            r = await GeocodeOneShotAsync(cand2, ct);
            return r;
        }

        private static string BuildOneBestCandidate(string address, string ward, string district, string province)
        {
            address = NormalizeForGeocode(address).Trim().Trim(',');

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(address)) parts.Add(address);
            if (!string.IsNullOrWhiteSpace(ward)) parts.Add(ward);
            if (!string.IsNullOrWhiteSpace(district)) parts.Add(district);
            if (!string.IsNullOrWhiteSpace(province)) parts.Add(province);
            parts.Add("Việt Nam");

            return string.Join(", ", parts);
        }

        private async Task<(double? lat, double? lng, string? err)> GeocodeOneShotAsync(string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                return (null, null, "Empty query");

            await _geoGate.WaitAsync(ct);
            try
            {
                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    var now = DateTime.UtcNow;
                    var needWait = (_lastGeoCallUtc + _minGeoInterval) - now;
                    if (needWait > TimeSpan.Zero) await Task.Delay(needWait, ct);
                    _lastGeoCallUtc = DateTime.UtcNow;

                    var url =
                        "search?format=jsonv2&limit=5&addressdetails=1&countrycodes=vn" +
                        "&email=" + Uri.EscapeDataString(GeoEmail) +
                        "&q=" + Uri.EscapeDataString(query);

                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(PerRequestTimeout);

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.Clear();
                    req.Headers.UserAgent.ParseAdd(GeoUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8");

                    using var res = await _nominatim.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);

                    if (res.StatusCode == (HttpStatusCode)429)
                    {
                        await Task.Delay(1200, ct);
                        continue;
                    }

                    var body = await res.Content.ReadAsStringAsync(linked.Token);

                    if (res.StatusCode == HttpStatusCode.Forbidden)
                        return (null, null, "403 Forbidden (Nominatim chặn) - kiểm tra User-Agent / tần suất.");

                    if (!res.IsSuccessStatusCode)
                        return (null, null, $"HTTP {(int)res.StatusCode}: {Trim160(body)}");

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                        return (null, null, "No result");

                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        var item = root[i];
                        var latStr = item.TryGetProperty("lat", out var latEl) ? latEl.GetString() : null;
                        var lonStr = item.TryGetProperty("lon", out var lonEl) ? lonEl.GetString() : null;

                        if (double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
                            double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
                        {
                            return (lat, lng, null);
                        }
                    }

                    return (null, null, "No valid lat/lon");
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

        // =========================================================
        // Helpers
        // =========================================================
        private static bool IsValidLatLng(double lat, double lng)
            => lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;

        private static string BuildAdminOnlyText(string ward, string district, string province)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ward)) parts.Add(ward);
            if (!string.IsNullOrWhiteSpace(district)) parts.Add(district);
            if (!string.IsNullOrWhiteSpace(province)) parts.Add(province);
            parts.Add("Việt Nam");
            return string.Join(", ", parts);
        }

        private static bool IsTooVagueForGeocode(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return true;

            var s = NormalizeKey(address);
            bool hasNumber = Regex.IsMatch(s, @"\d+");

            bool hasStreetKeyword =
                s.Contains(" duong ") || s.Contains("đường") || s.Contains(" street ") ||
                s.Contains(" hem ") || s.Contains(" ngo ") || s.Contains(" alley ") ||
                s.Contains(" chung cu ") || s.Contains(" cư ") || s.Contains(" toa ") || s.Contains(" building ") ||
                s.Contains(" khu pho ") || s.Contains(" khu dan cu ");

            bool hasPoiKeyword =
                s.Contains("vincom") ||
                s.Contains("dai hoc") || s.Contains("đại học") ||
                s.Contains("benh vien") || s.Contains("bệnh viện") ||
                s.Contains("khu cong nghe cao") || s.Contains("công nghệ cao") ||
                s.Contains("suoi tien") || s.Contains("suối tiên") ||
                s.Contains("metro") || s.Contains("ga") || s.Contains("tram");

            bool longEnough = s.Length >= 25;
            return !(hasNumber || hasStreetKeyword || hasPoiKeyword || longEnough);
        }

        private static int? TryParseInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0) return v;
            if (int.TryParse(s, out v) && v > 0) return v;

            return null;
        }

        private static string NormalizeReceiverAddress(string? detail, string ward, string district, string province)
        {
            detail = (detail ?? "").Trim();
            detail = CleanDetailAddress(detail);

            bool Has(string s) => !string.IsNullOrWhiteSpace(s);

            bool ContainsPart(string full, string part)
            {
                if (string.IsNullOrWhiteSpace(full) || string.IsNullOrWhiteSpace(part)) return false;
                return NormalizeKey(full).Contains(NormalizeKey(part));
            }

            var parts = new List<string>();
            if (Has(detail)) parts.Add(detail);

            if (Has(ward) && !ContainsPart(detail, ward)) parts.Add(ward);
            if (Has(district) && !ContainsPart(detail, district)) parts.Add(district);
            if (Has(province) && !ContainsPart(detail, province)) parts.Add(province);

            return string.Join(", ", parts).Replace(",,", ",").Trim().Trim(',');
        }

        private static string CleanDetailAddress(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            s = s.Trim();
            s = Regex.Replace(s, @"\([^)]*\)", " ");

            s = Regex.Replace(s, @",\s*(p\.?|phường)\s+[^,]+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @",\s*(q\.?|quận)\s+[^,]+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @",\s*(tp\.?|thành\s*phố)\s+[^,]+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @",\s*(huyện|tỉnh)\s+[^,]+", "", RegexOptions.IgnoreCase);

            s = Regex.Replace(s, @",\s*(tp\.?\s*hcm|tp\.?\s*ho\s*chi\s*minh|tphcm|hcm|hồ\s*chí\s*minh)\s*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @",\s*(việt\s*nam|viet\s*nam)\s*$", "", RegexOptions.IgnoreCase);

            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @",\s*,+", ", ");
            return s.Trim().Trim(',').Trim();
        }

        private static string NormalizeForGeocode(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return address;

            var s = address.Trim();
            s = Regex.Replace(s, @"\([^)]*\)", " ");
            s = Regex.Replace(s, @"\bĐHQG\b", "Đại học Quốc gia", RegexOptions.IgnoreCase);

            s = s.Replace("TP.HCM", "Thành phố Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
                 .Replace("TP. HCM", "Thành phố Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
                 .Replace("TPHCM", "Thành phố Hồ Chí Minh", StringComparison.OrdinalIgnoreCase);

            s = Regex.Replace(s, @"\bHCM\b", "Hồ Chí Minh", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bTP\.?\s+", "Thành phố ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bP\.\s*", "Phường ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bQ\.\s*", "Quận ", RegexOptions.IgnoreCase);

            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @",\s*,+", ", ");
            while (s.Contains(",,")) s = s.Replace(",,", ",");

            return s.Trim().Trim(',');
        }

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

            return IsValidLatLng(lat, lng);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeKey(string text)
        {
            var s = RemoveDiacritics(text ?? "");
            s = s.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"\s+", " ");
            return s;
        }

        private decimal ParseDecimal(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            raw = raw.Replace(".", "").Replace(",", "");
            return decimal.TryParse(raw, out var v) ? v : 0;
        }

        private static string EscapeCsv(object? value)
        {
            var s = value?.ToString() ?? "";
            s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", " ");

            if (s.Contains(',') || s.Contains(';') || s.Contains('"'))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";

            return s;
        }

        private static string FormatMoneyAsText(decimal value)
            => value.ToString("#,0", CultureInfo.InvariantCulture);

        private static string Trim160(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 160 ? s : s.Substring(0, 160);
        }

        private static string FormatDateTimeSafe(object? value, string format)
        {
            if (value == null) return "";

            if (value is DateTime dt)
                return dt.ToString(format, CultureInfo.InvariantCulture);

            if (value is DateTimeOffset dto)
                return dto.ToString(format, CultureInfo.InvariantCulture);

            if (value is string s && DateTime.TryParse(s, out var parsed))
                return parsed.ToString(format, CultureInfo.InvariantCulture);

            return value.ToString() ?? "";
        }
    }
}
