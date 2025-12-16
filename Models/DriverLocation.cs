public class DriverLocation
{
    public int Id { get; set; }
    public int DriverId { get; set; }        // UserId tài xế
    public int? OrderId { get; set; }        // optional: đang giao đơn nào
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? Accuracy { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.Now;
}
