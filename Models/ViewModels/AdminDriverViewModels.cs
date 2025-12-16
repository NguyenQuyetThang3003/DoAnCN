using System;
using System.ComponentModel.DataAnnotations;

namespace WedNightFury.Models.ViewModels
{
    public class AdminDriverListItemViewModel
    {
        public int UserId { get; set; }
        public int? ProfileId { get; set; }

        public string UserName { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? CitizenId { get; set; }

        public string Role { get; set; } = "driver";
        public DateTime CreatedAt { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
    }

    public class AdminDriverEditViewModel
    {
        public int? ProfileId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required, StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [StringLength(150)]
        public string? FullName { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        [Phone]
        public string? Phone { get; set; }

        public string? CitizenId { get; set; }

        // Chỉ dùng khi tạo mới
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [StringLength(255)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? District { get; set; }

        [StringLength(100)]
        public string? Ward { get; set; }

        [StringLength(150)]
        public string? CompanyName { get; set; }
    }
}
