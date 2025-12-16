using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using WedNightFury.Filters;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    [AdminAuthorize]
    public class AdminReportsController : Controller
    {
        private readonly AppDbContext _db;

        public AdminReportsController(AppDbContext db)
        {
            _db = db;
        }

        // =========================
        // TỔNG QUAN BÁO CÁO
        // URL: /AdminReports/Overview
        // =========================
        public async Task<IActionResult> Overview(DateTime? from, DateTime? to)
        {
            // Chốt khoảng thời gian
            DateTime fromDate = from?.Date ?? DateTime.Today.AddDays(-6);  // mặc định 7 ngày gần nhất
            DateTime toDate   = (to?.Date ?? DateTime.Today).AddDays(1);   // +1 để <= to

            // Lấy toàn bộ orders trong khoảng
            var ordersQuery = _db.Orders
                .Where(o => o.CreatedAt.HasValue &&
                            o.CreatedAt.Value >= fromDate &&
                            o.CreatedAt.Value < toDate);

            var orders = await ordersQuery.AsNoTracking().ToListAsync();

            int totalOrders    = orders.Count;
            int pendingOrders  = orders.Count(o => o.Status == "pending");
            int shippingOrders = orders.Count(o => o.Status == "shipping");
            int doneOrders     = orders.Count(o => o.Status == "done");
            int failedOrders   = orders.Count(o => o.Status == "failed" || o.Status == "cancelled");

            // Tỷ lệ giao thành công trong các đơn đã xử lý (done + failed)
            int processed = doneOrders + failedOrders;
            double successRate = processed > 0
                ? Math.Round(doneOrders * 100.0 / processed, 2)
                : 0;

            // Tài chính
            decimal totalShipFee = orders.Sum(o => o.ShipFee);
            decimal totalCod     = orders.Sum(o => o.CodAmount);
            decimal totalRevenue = totalShipFee + totalCod;

            int totalCustomers = orders
                .Where(o => o.CustomerId.HasValue)
                .Select(o => o.CustomerId!.Value)
                .Distinct()
                .Count();

            // Đơn theo ngày (để vẽ biểu đồ)
            var ordersPerDay = orders
                .Where(o => o.CreatedAt.HasValue)
                .GroupBy(o => o.CreatedAt!.Value.Date)
                .Select(g => new AdminReportOrdersPerDay
                {
                    Date  = g.Key,
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            // Đơn theo khu vực (Province)
            var areaStats = orders
                .GroupBy(o => string.IsNullOrWhiteSpace(o.Province) ? "Không rõ" : o.Province!)
                .Select(g => new AdminReportAreaStat
                {
                    Province = g.Key,
                    Count    = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10) // top 10 khu vực
                .ToList();

            // Hiệu suất tài xế (dựa trên đơn status=done)
            var doneOrderIds = orders
                .Where(o => o.Status == "done" && o.DriverId.HasValue)
                .Select(o => new { o.Id, o.DriverId })
                .ToList();

            var driverIds = doneOrderIds
                .Where(x => x.DriverId.HasValue)
                .Select(x => x.DriverId!.Value)
                .Distinct()
                .ToList();

            // Lấy tên tài xế từ bảng Users (giả định Role = "driver")
            var drivers = await _db.Users
                .Where(u => driverIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            // Rating theo đơn
            var ratedOrderIds = doneOrderIds.Select(x => x.Id).ToList();
            var ratings = await _db.Ratings
                .Where(r => ratedOrderIds.Contains(r.OrderId) && r.Score.HasValue)
                .AsNoTracking()
                .ToListAsync();

            // Map rating theo orderId
            var ratingByOrder = ratings
                .GroupBy(r => r.OrderId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(x => x.Score ?? 0)
                );

            var driverPerf = doneOrderIds
                .GroupBy(x => x.DriverId!.Value)
                .Select(g =>
                {
                    var driverId   = g.Key;
                    var orderIds   = g.Select(x => x.Id).ToList();
                    var driverRate = ratingByOrder
                        .Where(kv => orderIds.Contains(kv.Key))
                        .Select(kv => kv.Value)
                        .DefaultIfEmpty(0)
                        .Average();

                    return new AdminReportDriverPerformance
                    {
                        DriverId   = driverId,
                        DriverName = drivers.ContainsKey(driverId) ? drivers[driverId] ?? $"#{driverId}" : $"#{driverId}",
                        TotalDone  = g.Count(),
                        AvgRating  = Math.Round(driverRate, 2)
                    };
                })
                .OrderByDescending(x => x.TotalDone)
                .Take(5)
                .ToList();

            // Thống kê rating chung cho khoảng thời gian
            var allRatings = ratings; // dùng lại list ratings ở trên

            double avgScore = allRatings.Any()
                ? Math.Round(allRatings.Average(r => r.Score!.Value), 2)
                : 0;

            var ratingDist = Enumerable.Range(1, 5)
                .Select(score => new AdminReportRatingStat
                {
                    Score = score,
                    Count = allRatings.Count(r => r.Score == score)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var vm = new AdminReportOverviewViewModel
            {
                FromDate       = fromDate.Date,
                ToDate         = toDate.AddDays(-1).Date,

                TotalOrders    = totalOrders,
                PendingOrders  = pendingOrders,
                ShippingOrders = shippingOrders,
                DoneOrders     = doneOrders,
                FailedOrders   = failedOrders,
                SuccessRate    = successRate,

                TotalShipFee   = totalShipFee,
                TotalCod       = totalCod,
                TotalRevenue   = totalRevenue,
                TotalCustomers = totalCustomers,

                OrdersPerDay   = ordersPerDay,
                AreaStats      = areaStats,
                DriverStats    = driverPerf,
                AverageRating  = avgScore,
                RatingStats    = ratingDist
            };

            return View(vm);
        }

        // =========================
        // XUẤT CSV (MỞ ĐƯỢC BẰNG EXCEL)
        // URL: /AdminReports/ExportOrders?from=...&to=...
        // ĐÃ THÊM: DriverId, DriverUserName + UTF8 BOM + tiền dạng text
        // =========================
        public async Task<IActionResult> ExportOrders(DateTime? from, DateTime? to)
        {
            DateTime fromDate = from?.Date ?? DateTime.Today.AddDays(-6);
            DateTime toDate   = (to?.Date ?? DateTime.Today).AddDays(1);

            var orders = await _db.Orders
                .Where(o => o.CreatedAt.HasValue &&
                            o.CreatedAt.Value >= fromDate &&
                            o.CreatedAt.Value < toDate)
                .OrderBy(o => o.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            // Lấy danh sách DriverId xuất hiện trong các đơn
            var driverIds = orders
                .Where(o => o.DriverId.HasValue)
                .Select(o => o.DriverId!.Value)
                .Distinct()
                .ToList();

            // Lấy username tài xế từ bảng Users
            var drivers = await _db.Users
                .Where(u => driverIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("OrderId,Code,CustomerId,ReceiverName,Province,Status,ShipFee,CodAmount,DiscountCode,DiscountAmount,CreatedAt,DriverId,DriverUserName");

            foreach (var o in orders)
            {
                string driverIdStr = o.DriverId?.ToString() ?? "";
                string driverName  = "";

                if (o.DriverId.HasValue && drivers.TryGetValue(o.DriverId.Value, out var name))
                {
                    driverName = name ?? $"#{o.DriverId.Value}";
                }

                sb.AppendLine(string.Join(",", new[]
                {
                    o.Id.ToString(),
                    Escape(o.Code),
                    o.CustomerId?.ToString() ?? "",
                    Escape(o.ReceiverName),
                    Escape(o.Province),
                    Escape(o.Status),
                    FormatMoneyAsText(o.ShipFee),
                    FormatMoneyAsText(o.CodAmount),
                    Escape(o.DiscountCode),
                    FormatMoneyAsText(o.DiscountAmount),
                    o.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    driverIdStr,
                    Escape(driverName)
                }));
            }

            // Xuất UTF-8 có BOM để Excel hiểu đúng tiếng Việt
            var csv         = sb.ToString();
            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var bytes       = utf8WithBom.GetBytes(csv);

            var fileName = $"orders_report_{DateTime.Now:yyyyMMddHHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        private string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // bao chuỗi bằng " … " để Excel hiểu đúng
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        // Xuất tiền dưới dạng text để Excel không convert sang 5E+08
        private string FormatMoneyAsText(decimal value)
        {
            // dùng invariant để không sinh dấu phẩy/thập phân lạ
            var s = value.ToString("0.##", CultureInfo.InvariantCulture);
            // thêm ' để Excel hiểu là text, rồi Escape để bao bằng "
            return Escape("'" + s);
        }
    }

    // =========================
    // VIEW MODELS
    // =========================

    public class AdminReportOverviewViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate   { get; set; }

        // Tổng quan đơn hàng
        public int    TotalOrders    { get; set; }
        public int    PendingOrders  { get; set; }
        public int    ShippingOrders { get; set; }
        public int    DoneOrders     { get; set; }
        public int    FailedOrders   { get; set; }
        public double SuccessRate    { get; set; }

        // Tài chính
        public decimal TotalShipFee { get; set; }
        public decimal TotalCod     { get; set; }
        public decimal TotalRevenue { get; set; }
        public int     TotalCustomers { get; set; }

        // Dữ liệu chi tiết
        public List<AdminReportOrdersPerDay>      OrdersPerDay  { get; set; } = new();
        public List<AdminReportAreaStat>          AreaStats     { get; set; } = new();
        public List<AdminReportDriverPerformance> DriverStats   { get; set; } = new();
        public double                             AverageRating { get; set; }
        public List<AdminReportRatingStat>        RatingStats   { get; set; } = new();
    }

    public class AdminReportOrdersPerDay
    {
        public DateTime Date  { get; set; }
        public int      Count { get; set; }
    }

    public class AdminReportAreaStat
    {
        public string Province { get; set; } = string.Empty;
        public int    Count    { get; set; }
    }

    public class AdminReportDriverPerformance
    {
        public int    DriverId   { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public int    TotalDone  { get; set; }
        public double AvgRating  { get; set; }
    }

    public class AdminReportRatingStat
    {
        public int Score { get; set; }   // 1..5
        public int Count { get; set; }
    }
}
