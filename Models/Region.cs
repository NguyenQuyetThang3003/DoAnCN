using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("regions")]
    public class Region
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(150)]
        public string Name { get; set; } = string.Empty;

        // inner / outer
        [Required, StringLength(20)]
        public string AreaType { get; set; } = "inner";

        // standard / fast / express
        [Required, StringLength(20)]
        public string ServiceLevel { get; set; } = "standard";

        [ForeignKey(nameof(Hub))]
        public int? HubId { get; set; }
        public virtual Hub? Hub { get; set; }

        public double CenterLat { get; set; }
        public double CenterLng { get; set; }
        public double RadiusKm { get; set; }

        [Column(TypeName = "decimal(15,2)")]
        public decimal BaseShipFee { get; set; }

        [Column(TypeName = "decimal(15,2)")]
        public decimal ExtraPerKg { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
