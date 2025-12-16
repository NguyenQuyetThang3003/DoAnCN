using Microsoft.AspNetCore.Http;

namespace WedNightFury.Models.ViewModels
{
    public class DeliveredViewModel
    {
        public int OrderId { get; set; }

        public string? Code { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverAddress { get; set; }

        public IFormFile? PodImage { get; set; }
        public string? Note { get; set; }

        // NEW: thông tin COD + ký tên
        public decimal CodAmount { get; set; }      // số tiền COD phải thu
        public decimal CollectedCod { get; set; }   // số tiền tài xế nhập là đã thu
        public string? DriverSignature { get; set; } // tên tài xế ký xác nhận
    }
}
