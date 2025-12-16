using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    public class OrderTrackingEvent
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        // pending / picked_up / at_hub / shipping / done / failed / cancelled ...
        [Required, MaxLength(30)]
        public string Status { get; set; } = "pending";

        [MaxLength(200)]
        public string? Title { get; set; }  // VD: "Đã lấy hàng"

        [MaxLength(500)]
        public string? Note { get; set; }   // VD: "Shipper A123 đã lấy hàng"

        [MaxLength(255)]
        public string? Location { get; set; } // VD: "Hub Thủ Đức"

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // navigation
        public Order? Order { get; set; }
    }
}
