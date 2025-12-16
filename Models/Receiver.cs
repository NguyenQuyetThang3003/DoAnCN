using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("receivers")]
    public class Receiver
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Tên người nhận")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Số điện thoại")]
        public string? Phone { get; set; }

        [Display(Name = "Địa chỉ")]
        public string? Address { get; set; }

        [Display(Name = "Tỷ lệ giao thành công")]
        public string? SuccessRate { get; set; }

        [Display(Name = "Ghi chú")]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
