using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    public class RatingController : Controller
    {
        private readonly AppDbContext _context;

        public RatingController(AppDbContext context)
        {
            _context = context;
        }

        // [GET] /Rating/Create/{orderId}
        [HttpGet]
        public IActionResult Create(int orderId)
        {
            var order = _context.Orders.FirstOrDefault(o => o.Id == orderId);
            if (order == null)
                return NotFound("Không tìm thấy đơn hàng.");

            // Chỉ được đánh giá khi đơn đã hoàn tất
            if (order.Status != "done")
                return BadRequest("Đơn hàng chưa hoàn tất, không thể đánh giá.");

            // Nếu đã có đánh giá rồi -> không cho đánh giá lại
            var existing = _context.Ratings.FirstOrDefault(r => r.OrderId == orderId);
            if (existing != null)
            {
                TempData["Message"] = "Bạn đã đánh giá đơn hàng này rồi!";
                return RedirectToAction("Manage", "Order");
            }

            var model = new Rating
            {
                OrderId = orderId,
                CustomerId = order.CustomerId ?? 0
            };

            ViewBag.Order = order;
            return View(model);
        }

        // [POST] /Rating/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Rating model)
        {
            if (model.Score == null || model.Score < 1 || model.Score > 5)
            {
                ModelState.AddModelError("Score", "Vui lòng chọn số sao từ 1 đến 5.");
            }

            if (ModelState.IsValid)
            {
                model.CreatedAt = DateTime.Now;
                _context.Ratings.Add(model);
                _context.SaveChanges();

                TempData["Message"] = "Cảm ơn bạn đã đánh giá dịch vụ ❤️";
                return RedirectToAction("Manage", "Order");
            }

            var order = _context.Orders.FirstOrDefault(o => o.Id == model.OrderId);
            ViewBag.Order = order;
            return View(model);
        }
    }
}
