using System;

namespace WedNightFury.Models
{
    public class Rating
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public int? Score { get; set; }
        public string? Comment { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
