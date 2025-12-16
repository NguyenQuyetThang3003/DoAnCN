using System;

namespace WedNightFury.Models.ViewModels
{
    public class AdminComplaintThreadViewModel
    {
        public int Id { get; set; }
        public int DriverId { get; set; }

        public string DriverUserName { get; set; } = string.Empty;
        public string? DriverFullName { get; set; }

        // Nội dung ticket ban đầu của tài xế
        public string Message { get; set; } = string.Empty;

        // Toàn bộ log trao đổi (Admin / Driver), dạng text nhiều dòng
        public string? Reply { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime? RepliedAt { get; set; }
    }
}
