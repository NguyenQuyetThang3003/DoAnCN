using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("supporttickets")]
    public class SupportTicket
    {
        [Key]
        public int Id { get; set; }

        public int DriverId { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public string Status { get; set; } = "pending";   // pending | replied

        public string? Reply { get; set; }   // phản hồi từ admin

        public DateTime? RepliedAt { get; set; } // thời điểm admin trả lời

        public DateTime CreatedAt { get; set; }
    }
}
