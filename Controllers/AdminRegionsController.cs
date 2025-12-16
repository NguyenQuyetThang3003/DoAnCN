using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
// using WedNightFury.Filters; // nếu bạn có AdminAuthorize

namespace WedNightFury.Controllers
{
    // [AdminAuthorize] // nếu bạn có filter cho admin thì bật dòng này
    public class AdminRegionsController : Controller
    {
        private readonly AppDbContext _context;

        public AdminRegionsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /AdminRegions
        public async Task<IActionResult> Index()
        {
            var regions = await _context.Regions
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(regions);
        }

        // GET: /AdminRegions/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /AdminRegions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Region model)
        {
            if (!ModelState.IsValid)
                return View(model);

            _context.Regions.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: /AdminRegions/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var region = await _context.Regions.FindAsync(id);
            if (region == null) return NotFound();

            return View(region);
        }

        // POST: /AdminRegions/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Region model)
        {
            if (id != model.Id) return NotFound();

            if (!ModelState.IsValid)
                return View(model);

            _context.Update(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // POST: /AdminRegions/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var region = await _context.Regions.FindAsync(id);
            if (region == null) return NotFound();

            _context.Regions.Remove(region);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
