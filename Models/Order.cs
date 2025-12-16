using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("orders")]
    public class Order
    {
        [Key]
        public int Id { get; set; }

        // ============================
        // 👤 KHÁCH HÀNG
        // ============================
        public int? CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual User? User { get; set; }

        // ============================
        // 🔖 MÃ ĐƠN
        // ============================
        [StringLength(50)]
        public string? Code { get; set; }

        // ============================
        // 📦 NGƯỜI GỬI
        // ============================
        [StringLength(100)]
        public string? SenderName { get; set; }

        [StringLength(20)]
        public string? SenderPhone { get; set; }

        [StringLength(200)]
        public string? SenderAddress { get; set; }

        // ============================
        // 🎁 NGƯỜI NHẬN
        // ============================
        [StringLength(100)]
        public string? ReceiverName { get; set; }

        [StringLength(20)]
        public string? ReceiverPhone { get; set; }

        [StringLength(255)]
        public string? ReceiverAddress { get; set; }

        [StringLength(100)]
        public string? Province { get; set; }

        // ============================
        // 📦 HÀNG HÓA
        // ============================
        [StringLength(200)]
        public string? ProductName { get; set; }

        [StringLength(50)]
        public string? GoodsType { get; set; }

        // DB của bạn là decimal(10,2) (ảnh), nhưng bạn để 10,3 vẫn chạy.
        // Nếu muốn khớp tuyệt đối DB, đổi lại 10,2.
        [Column(TypeName = "decimal(10,2)")]
        public decimal Weight { get; set; } = 0;

        [Column(TypeName = "decimal(15,2)")]
        public decimal Value { get; set; } = 0;

        [StringLength(200)]
        public string? Note { get; set; }

        // ✅ COD cũ (cột "Cod" trong DB)
        [Column(TypeName = "decimal(15,2)")]
        public decimal Cod { get; set; } = 0;

        // ============================
        // ⚙ CẤU HÌNH GIAO HÀNG
        // ============================
        [StringLength(20)]
        public string? AreaType { get; set; }

        [StringLength(20)]
        public string? PickupMethod { get; set; }

        // ✅ CŨ: tên hub dạng text (cột DropoffHub)
        [StringLength(100)]
        public string? DropoffHub { get; set; }

        // ✅ MỚI: hub theo Id
        public int? HandlingHubId { get; set; }
        public int? DropoffHubId { get; set; }

        [ForeignKey(nameof(HandlingHubId))]
        public virtual Hub? HandlingHub { get; set; }

        [ForeignKey(nameof(DropoffHubId))]
        public virtual Hub? DropoffHubRef { get; set; }

        [StringLength(20)]
        public string? ServiceLevel { get; set; }

        [StringLength(20)]
        public string? ShipPayer { get; set; }

        // ============================
        // 📌 TRẠNG THÁI HIỆN TẠI
        // ============================
        [StringLength(30)]
        public string? Status { get; set; } = "pending";

        // ✅ ĐỔI THÀNH NULLABLE để khớp DB (DATETIME Allow NULL)
        // DB có default CURRENT_TIMESTAMP, nên khi insert có thể tự set.
        public DateTime? CreatedAt { get; set; }

        // ============================
        // 🚚 TÀI XẾ
        // ============================
        public int? DriverId { get; set; }
        public DateTime? AssignedAt { get; set; }

        // DB là DATE, nhưng để DateTime? vẫn OK (EF sẽ lưu phần ngày).
        public DateTime? DeliveryDate { get; set; }

        public int? Sequence { get; set; }

        // ============================
        // MAP – VỊ TRÍ (Lat/Lng)
        // ============================
        public double? Lat { get; set; }
        public double? Lng { get; set; }

        // ============================
        // 📷 POD – GIAO THÀNH CÔNG
        // ============================
        [StringLength(255)]
        public string? PodImagePath { get; set; }

        [StringLength(255)]
        public string? DeliveredNote { get; set; }

        public DateTime? DeliveredAt { get; set; }

        // ============================
        // ❌ GIAO THẤT BẠI
        // ============================
        [StringLength(255)]
        public string? FailedReason { get; set; }

        [StringLength(255)]
        public string? FailedImagePath { get; set; }

        public DateTime? FailedAt { get; set; }

        // ============================
        // 🚛 PHÍ VẬN CHUYỂN
        // ============================
        [Column(TypeName = "decimal(15,2)")]
        public decimal ShipFee { get; set; } = 0;

        // ============================
        // 💰 COD – TIỀN THU HỘ (MỚI)
        // ============================
        [Column(TypeName = "decimal(15,2)")]
        public decimal CodAmount { get; set; } = 0;

        // DB là TINYINT/boolean
        public bool IsCodPaid { get; set; } = false;

        public DateTime? CodPaidAt { get; set; }

        // ============================
        // 🎫 KHUYẾN MÃI
        // ============================
        [StringLength(50)]
        public string? DiscountCode { get; set; }

        [Column(TypeName = "decimal(15,2)")]
        public decimal DiscountAmount { get; set; } = 0;

        // ============================
        // ✅ TIMELINE / TRACKING EVENTS
        // ============================
        public virtual ICollection<OrderTrackingEvent> TrackingEvents { get; set; }
            = new List<OrderTrackingEvent>();

        // ============================
        // ✅ Helper: COD thực tế (ưu tiên CodAmount)
        // ============================
        [NotMapped]
        public decimal CodEffective => (CodAmount > 0 ? CodAmount : Cod);
    }
}
