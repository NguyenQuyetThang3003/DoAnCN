using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("promotions")]
    public class Promotion
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Display(Name = "Mã khuyến mãi")]
        public string Code { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        // 'percent' hoặc 'fixed'
        [Required, StringLength(20)]
        [Display(Name = "Loại giảm")]
        public string DiscountType { get; set; } = "percent";

        [Required]
        [Display(Name = "Giá trị giảm")]
        public decimal Value { get; set; }

        [Required]
        [Display(Name = "Ngày bắt đầu")]
        public DateTime StartDate { get; set; }

        [Required]
        [Display(Name = "Ngày kết thúc")]
        public DateTime EndDate { get; set; }

        [Display(Name = "Đơn tối thiểu")]
        public decimal MinOrderValue { get; set; } = 0;

        [Display(Name = "Giảm tối đa")]
        public decimal? MaxDiscountValue { get; set; }

        [Display(Name = "Số lần áp dụng tối đa")]
        public int? MaxUsage { get; set; }

        [Display(Name = "Đã sử dụng")]
        public int UsedCount { get; set; } = 0;

        [Display(Name = "Đang hoạt động")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
