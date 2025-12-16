using Microsoft.AspNetCore.Mvc;
using WedNightFury.Models;
using System.Linq;

namespace WedNightFury.Controllers
{
    public class FaqController : Controller
    {
        private readonly AppDbContext _context;

        public FaqController(AppDbContext context)
        {
            _context = context;
        }

        // ========== [GET] /Faq ==========
        [HttpGet("/Faq")]
        public IActionResult Index(string category)
        {
            var faqs = _context.Faqs.AsQueryable();

            if (!string.IsNullOrEmpty(category))
                faqs = faqs.Where(f => f.Category == category);

            var categories = _context.Faqs
                .Select(f => f.Category)
                .Distinct()
                .ToList();

            ViewBag.Categories = categories;
            ViewBag.CurrentCategory = category;

            return View(faqs.ToList());
        }
    }
}
