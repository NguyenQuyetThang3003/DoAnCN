using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using WedNightFury.Models;
using WedNightFury.Models.ViewModels;

namespace WedNightFury.Controllers
{
    public class ServiceController : Controller
    {
        // TODO: Nếu bạn dùng DB, inject AppDbContext vào đây và query _context.Services
        // private readonly AppDbContext _context;
        // public ServiceController(AppDbContext context){ _context = context; }

        // DEMO DATA (thay bằng DB khi cần)
        private static readonly List<Service> _services = new()
        {
            new Service
            {
                Id = 1,
                Slug = "noi-tinh-sieu-toc",
                Name = "Giao ngay nội tỉnh",
                ShortDescription = "Giao nhanh trong cùng tỉnh/thành phố, lấy tận nơi và giao theo tuyến tối ưu.",
                BasePrice = 20000,
                EstimateTime = "4–8 giờ",
                DescriptionHtml = @"
                    <p><strong>Giao ngay nội tỉnh</strong> phù hợp cho các đơn hàng cần giao nhanh trong ngày.</p>
                    <ul>
                        <li>Lấy hàng tận nơi theo khung giờ hẹn.</li>
                        <li>Tối ưu tuyến giao để rút ngắn thời gian.</li>
                        <li>Cập nhật trạng thái theo từng mốc vận hành.</li>
                    </ul>
                    <p><strong>Khuyến nghị:</strong> đơn hàng nội thành – bán kính ngắn – yêu cầu giao trong ngày.</p>
                ",
                IsActive = true
            },
            new Service
            {
                Id = 2,
                Slug = "chuyen-phat-nhanh",
                Name = "Chuyển phát nhanh",
                ShortDescription = "Dịch vụ giao nhận nhanh liên tỉnh, thời gian tối ưu, đối soát COD minh bạch.",
                BasePrice = 30000,
                EstimateTime = "1–2 ngày",
                DescriptionHtml = @"
                    <p><strong>Chuyển phát nhanh</strong> dành cho đơn liên tỉnh cần tốc độ và độ tin cậy cao.</p>
                    <ul>
                        <li>Khai thác theo Hub, hạn chế thất lạc.</li>
                        <li>Tracking hành trình minh bạch.</li>
                        <li>Hỗ trợ COD, đối soát theo kỳ.</li>
                    </ul>
                ",
                IsActive = true
            },
            new Service
            {
                Id = 3,
                Slug = "kho-van-fulfillment",
                Name = "Kho vận & Fulfillment",
                ShortDescription = "Lưu kho, đóng gói, xử lý đơn hàng quy mô lớn cho shop/doanh nghiệp.",
                BasePrice = 50000,
                EstimateTime = "Theo SLA",
                DescriptionHtml = @"
                    <p><strong>Kho vận & Fulfillment</strong> giúp doanh nghiệp tối ưu vận hành và chi phí nhân sự.</p>
                    <ul>
                        <li>Nhập kho – kiểm đếm – quản lý tồn.</li>
                        <li>Đóng gói theo quy chuẩn, in tem nhãn.</li>
                        <li>Xử lý đơn hàng hàng loạt, API tích hợp.</li>
                    </ul>
                ",
                IsActive = true
            },
            new Service
            {
                Id = 4,
                Slug = "quoc-te-economy",
                Name = "Vận chuyển quốc tế (Economy)",
                ShortDescription = "Gửi hàng quốc tế tuyến phổ thông, tối ưu chi phí, theo dõi trạng thái.",
                BasePrice = 150000,
                EstimateTime = "5–10 ngày",
                DescriptionHtml = @"
                    <p><strong>Quốc tế (Economy)</strong> tối ưu chi phí cho các tuyến phổ biến.</p>
                    <ul>
                        <li>Hỗ trợ tư vấn khai báo – đóng gói.</li>
                        <li>Tracking theo chặng.</li>
                        <li>Chính sách bồi hoàn theo điều kiện.</li>
                    </ul>
                ",
                IsActive = true
            },
        };

        [HttpGet]
        public IActionResult Index()
        {
            var list = _services.Where(x => x.IsActive).ToList();
            return View(list);
        }

        // /Service/Details/1
        [HttpGet]
        public IActionResult Details(int id)
        {
            var service = _services.FirstOrDefault(x => x.Id == id && x.IsActive);
            if (service == null) return NotFound();

            var vm = new ServiceDetailsVm
            {
                Service = service,
                Related = _services.Where(x => x.IsActive && x.Id != id).Take(3).ToList()
            };

            return View(vm);
        }

        // /Service/Details?slug=noi-tinh-sieu-toc
        [HttpGet]
        public IActionResult Details(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return RedirectToAction(nameof(Index));

            var service = _services.FirstOrDefault(x =>
                x.IsActive && string.Equals(x.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));

            if (service == null) return NotFound();

            var vm = new ServiceDetailsVm
            {
                Service = service,
                Related = _services.Where(x => x.IsActive && x.Id != service.Id).Take(3).ToList()
            };

            return View(vm);
        }
    }
}
