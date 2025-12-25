namespace WedNightFury.Models
{
    public class Service
    {
        public int Id { get; set; }
        public string Slug { get; set; } = "";     // vd: "noi-tinh-sieu-toc"
        public string Name { get; set; } = "";     // vd: "Giao ngay nội tỉnh"
        public string ShortDescription { get; set; } = "";
        public string DescriptionHtml { get; set; } = ""; // nội dung chi tiết (HTML)
        public decimal BasePrice { get; set; }     // giá từ...
        public string EstimateTime { get; set; } = "";    // vd: "4–8 giờ"
        public bool IsActive { get; set; } = true;
    }
}
