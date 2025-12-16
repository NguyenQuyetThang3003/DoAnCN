using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("auditlogs")]
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        // Ai thao tác
        public int AdminId { get; set; }

        [Required, StringLength(150)]
        public string AdminName { get; set; } = string.Empty;

        // Hành động: create / update / delete / login ...
        [Required, StringLength(50)]
        public string Action { get; set; } = string.Empty;

        // Đối tượng tác động: User / Driver / Order / Promotion ...
        [Required, StringLength(50)]
        public string EntityType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        [StringLength(255)]
        public string? Description { get; set; }

        // JSON hoặc text mô tả trước / sau khi sửa
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        [StringLength(50)]
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
