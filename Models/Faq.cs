using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("faqs")]
    public class Faq
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Category { get; set; } = string.Empty;

        [Required, MaxLength(300)]
        public string Question { get; set; } = string.Empty;

        [Required]
        public string Answer { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
