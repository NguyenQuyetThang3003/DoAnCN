using System.ComponentModel.DataAnnotations.Schema;

namespace WedNightFury.Models
{
    [Table("users")] // ánh xạ tới bảng users
    public class User
    {
        public int Id { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? CitizenId { get; set; }

        public string? CompanyName { get; set; }
        public virtual ICollection<Order>? Orders { get; set; }


        public string Role { get; set; } = "customer";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
