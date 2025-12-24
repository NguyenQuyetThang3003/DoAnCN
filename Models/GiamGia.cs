using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("giamgia")] // ✅ map đúng tên bảng trong MySQL
    public class GiamGia
    {
        [Key]
        [Column("Magiamgia")] // ✅ map đúng tên cột (đúng như DB)
        public int Magiamgia { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        public int Giatriphantram { get; set; }

        public DateTime Ngaybatdau { get; set; }
        public DateTime Ngayketthuc { get; set; }

        public bool Trangthai { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
