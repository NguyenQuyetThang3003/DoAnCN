using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // Trang chủ public (cho khách chưa login)
        public IActionResult Index()
        {
            // Nếu đã đăng nhập thì chuyển về Dashboard
            if (HttpContext.Session.GetString("UserName") != null)
            {
                return RedirectToAction("Dashboard", "Customer");
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
