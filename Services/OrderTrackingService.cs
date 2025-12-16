using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;

namespace WedNightFury.Services
{
    public interface IOrderTrackingService
    {
        Task AddAsync(
            int orderId,
            string status,
            string? title = null,
            string? note = null,
            string? location = null,
            string? actor = null,
            CancellationToken ct = default);

        Task AddAsync(
            Order order,
            string status,
            string? title = null,
            string? note = null,
            string? location = null,
            string? actor = null,
            CancellationToken ct = default);
    }

    public class OrderTrackingService : IOrderTrackingService
    {
        private readonly AppDbContext _db;

        public OrderTrackingService(AppDbContext db)
        {
            _db = db;
        }

        public Task AddAsync(
            Order order,
            string status,
            string? title = null,
            string? note = null,
            string? location = null,
            string? actor = null,
            CancellationToken ct = default)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            return AddAsync(order.Id, status, title, note, location, actor, ct);
        }

        public async Task AddAsync(
            int orderId,
            string status,
            string? title = null,
            string? note = null,
            string? location = null,
            string? actor = null,
            CancellationToken ct = default)
        {
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));

            var normalizedStatus = NormalizeStatus(status);
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                throw new ArgumentException("Status không hợp lệ.", nameof(status));

            var normalizedTitle = NormalizeText(title) ?? DefaultTitle(normalizedStatus);
            var normalizedNote = NormalizeText(note);
            var normalizedLocation = NormalizeText(location);
            var normalizedActor = NormalizeText(actor);

            // chống spam: nếu event mới giống hệt event cuối (status/title/note/location) thì bỏ
            var last = await _db.OrderTrackingEvents.AsNoTracking()
                .Where(x => x.OrderId == orderId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Status,
                    x.Title,
                    x.Note,
                    x.Location
                })
                .FirstOrDefaultAsync(ct);

            if (last != null
                && string.Equals(last.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)
                && string.Equals(last.Title ?? "", normalizedTitle ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(last.Note ?? "", normalizedNote ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(last.Location ?? "", normalizedLocation ?? "", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Nếu bạn muốn gộp actor vào note cho dễ hiển thị (khỏi đổi DB):
            if (!string.IsNullOrWhiteSpace(normalizedActor))
            {
                normalizedNote = string.IsNullOrWhiteSpace(normalizedNote)
                    ? $"({normalizedActor})"
                    : $"{normalizedNote} ({normalizedActor})";
            }

            var ev = new OrderTrackingEvent
            {
                OrderId = orderId,
                Status = normalizedStatus,
                Title = normalizedTitle,
                Note = normalizedNote,
                Location = normalizedLocation,
                CreatedAt = DateTime.UtcNow // đổi DateTime.Now nếu bạn muốn giờ local
            };

            _db.OrderTrackingEvents.Add(ev);
            await _db.SaveChangesAsync(ct);
        }

        private static string NormalizeStatus(string? status)
        {
            status = (status ?? "").Trim().ToLowerInvariant();

            // map các biến thể thường gặp -> chuẩn hoá
            return status switch
            {
                "new" => "pending",
                "created" => "pending",
                "assign" => "assigned",
                "assigned_driver" => "assigned",
                "in_transit" => "shipping",
                "delivering" => "shipping",
                "completed" => "done",
                "success" => "done",
                "fail" => "failed",
                "canceled" => "cancelled",
                _ => status
            };
        }

        private static string? NormalizeText(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            // gom nhiều khoảng trắng thành 1
            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s;
        }

        private static string DefaultTitle(string status) => status switch
        {
            "pending"   => "Đã tạo đơn",
            "assigned"  => "Đã phân công tài xế",
            "shipping"  => "Đang giao hàng",
            "done"      => "Giao thành công",
            "failed"    => "Giao thất bại",
            "cancelled" => "Đơn đã hủy",
            _           => $"Cập nhật trạng thái: {status}"
        };
    }
}
