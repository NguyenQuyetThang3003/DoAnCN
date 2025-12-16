using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("hubs")]
    public class Hub
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Code { get; set; }

        [StringLength(255)]
        public string? Address { get; set; }

        public double? Lat { get; set; }
        public double? Lng { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
