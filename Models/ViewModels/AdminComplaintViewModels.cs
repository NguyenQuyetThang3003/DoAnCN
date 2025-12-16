using System;

namespace WedNightFury.Models.ViewModels
{
    public class AdminComplaintListItemViewModel
    {
        public int Id { get; set; }
        public int DriverId { get; set; }
        public string DriverUserName { get; set; } = string.Empty;
        public string? DriverFullName { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Reply { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RepliedAt { get; set; }
    }

    public class AdminComplaintReplyViewModel
    {
        public int Id { get; set; }
        public int DriverId { get; set; }

        public string DriverUserName { get; set; } = string.Empty;
        public string? DriverFullName { get; set; }

        public string Message { get; set; } = string.Empty;

        // nội dung trả lời admin nhập
        public string? Reply { get; set; }

        public string Status { get; set; } = "pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? RepliedAt { get; set; }
    }
}
