using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace WedNightFury.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        // ================= LOGIN =====================
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Vui lòng nhập tên đăng nhập và mật khẩu!";
                return View();
            }

            var user = _context.Users
                .FirstOrDefault(u => u.UserName.ToLower() == username.ToLower()
                                  && u.Password == password);

            if (user == null)
            {
                ViewBag.Error = "Sai tên đăng nhập hoặc mật khẩu!";
                return View();
            }

            // ================= COOKIE AUTH =================
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("UserId", user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Lưu Session (SỬA LỖI: PHẢI CÓ UserId)
            HttpContext.Session.SetString("UserName", user.UserName);
            HttpContext.Session.SetString("Role", user.Role);
            HttpContext.Session.SetInt32("UserId", user.Id); // ⭐ FIX CHÍNH

            // =============== REDIRECT THEO ROLE ====================
            switch (user.Role.ToLower())
            {
                case "customer":
                    return RedirectToAction("Dashboard", "Customer");

                case "admin":
                    return RedirectToAction("Index", "Admin");

                case "employee":
                    return RedirectToAction("Index", "Employee");

                case "driver":
                case "taixe":
                    return RedirectToAction("Dashboard", "Taixe");

                default:
                    return RedirectToAction("Index", "Home");
            }
        }

        // ================= REGISTER =====================
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(User user)
        {
            if (ModelState.IsValid)
            {
                if (_context.Users.Any(u => u.UserName == user.UserName || u.Email == user.Email))
                {
                    ViewBag.Error = "Tên đăng nhập hoặc Email đã tồn tại!";
                    return View(user);
                }

                user.Role = "customer";
                user.CreatedAt = DateTime.Now;

                _context.Users.Add(user);
                _context.SaveChanges();

                return RedirectToAction("Login", "Auth");
            }

            return View(user);
        }

        // ================= ACCESS DENIED =====================
        public IActionResult Denied(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // ================= LOGOUT =====================
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login", "Auth");
        }
    }
}
