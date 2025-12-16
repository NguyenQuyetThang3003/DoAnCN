using Microsoft.AspNetCore.Http;

namespace WedNightFury.Models.ViewModels
{
    public class FailedDeliveryViewModel
    {
        public int OrderId { get; set; }

        public string Code { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverAddress { get; set; } = string.Empty;

        public string FailedReason { get; set; } = string.Empty;

        public IFormFile? EvidenceImage { get; set; }
    }
}
