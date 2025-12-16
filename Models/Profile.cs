using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("profiles")]
    public class Profile
    {
        [Key]
        public int Id { get; set; }

        // Liên kết với bảng users (nếu cần)
        public int UserId { get; set; }

        [Required, StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        [Phone]
        public string? Phone { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [StringLength(50)]
        public string? TaxCode { get; set; }

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
