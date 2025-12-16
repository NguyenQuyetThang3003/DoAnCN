using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    [Route("User")]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _context;

        public ProfileController(AppDbContext context)
        {
            _context = context;
        }

        // Chọn layout theo role
        private string GetLayoutForRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return "~/Views/Shared/_LayoutCustomer.cshtml";

            switch (role.ToLower())
            {
                case "driver":
                    return "~/Views/Shared/_LayoutDriver.cshtml";

                // nếu sau này có role admin muốn dùng layout riêng thì thêm case
                // case "admin":
                //     return "~/Views/Shared/_LayoutAdmin.cshtml";

                default:
                    return "~/Views/Shared/_LayoutCustomer.cshtml";
            }
        }

        // ========== [GET] /User/Profile ==========
        [HttpGet("Profile")]
        public IActionResult Index()
        {
            // Lấy username trong session
            var username = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(username))
                return RedirectToAction("Login", "Auth"); // chưa login

            // Lấy thông tin user
            var user = _context.Users.FirstOrDefault(u => u.UserName == username);
            if (user == null)
                return NotFound("Không tìm thấy người dùng.");

            // Tìm profile tương ứng
            var profile = _context.Profiles.FirstOrDefault(p => p.UserId == user.Id);
            if (profile == null)
            {
                // Nếu chưa có profile thì tạo mới theo thông tin user
                profile = new Profile
                {
                    UserId      = user.Id,
                    FullName    = user.CompanyName ?? user.UserName,
                    Email       = user.Email,
                    Phone       = user.Phone,
                    Address     = "",
                    City        = "",
                    District    = "",
                    Ward        = "",
                    CompanyName = user.CompanyName
                };

                _context.Profiles.Add(profile);
                _context.SaveChanges();
            }

            ViewBag.Layout = GetLayoutForRole(user.Role);
            return View("Index", profile);
        }

        // ========== [POST] /User/Profile ==========
        [HttpPost("Profile")]
        [ValidateAntiForgeryToken]
        public IActionResult Index(Profile model)
        {
            // Lấy user hiện tại (dùng UserId từ model)
            var user = _context.Users.FirstOrDefault(u => u.Id == model.UserId);
            ViewBag.Layout = GetLayoutForRole(user?.Role);

            if (!ModelState.IsValid)
            {
                // Trả lại view với lỗi + đúng layout
                return View("Index", model);
            }

            var profile = _context.Profiles.FirstOrDefault(p => p.Id == model.Id);
            if (profile == null)
                return NotFound();

            // Cập nhật dữ liệu profile
            profile.FullName    = model.FullName;
            profile.Email       = model.Email;
            profile.Phone       = model.Phone;
            profile.BirthDate   = model.BirthDate;
            profile.TaxCode     = model.TaxCode;
            profile.Address     = model.Address;
            profile.City        = model.City;
            profile.District    = model.District;
            profile.Ward        = model.Ward;
            profile.CompanyName = model.CompanyName;

            _context.SaveChanges();

            ViewBag.Message = "✅ Cập nhật thông tin thành công!";
            return View("Index", profile);
        }
    }
}
