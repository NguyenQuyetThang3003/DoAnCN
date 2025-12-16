using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;
using System.Linq;

namespace WedNightFury.Controllers
{
    public class ReceiverController : Controller
    {
        private readonly AppDbContext _context;

        public ReceiverController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Receiver
        public IActionResult Index(string? keyword)
        {
            var list = _context.Receivers.AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
            {
                keyword = keyword.ToLower();
                list = list.Where(r => 
                    r.Name.ToLower().Contains(keyword) || 
                    r.Phone.Contains(keyword));
            }

            var receivers = list.OrderByDescending(r => r.Id).ToList();
            return View(receivers);
        }

        // GET: /Receiver/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Receiver/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Receiver model)
        {
            if (!ModelState.IsValid)
                return View(model);

            _context.Receivers.Add(model);
            _context.SaveChanges();

            TempData["Message"] = "✅ Thêm người nhận thành công!";
            return RedirectToAction("Index");
        }
    }
}
