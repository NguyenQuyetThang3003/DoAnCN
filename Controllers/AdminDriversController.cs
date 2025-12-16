using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using WedNightFury.Models.ViewModels;

namespace WedNightFury.Controllers
{
    public class AdminDriversController : Controller
    {
        private readonly AppDbContext _context;

        public AdminDriversController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /AdminDrivers
        public async Task<IActionResult> Index(string? search)
        {
            var query =
                from u in _context.Users
                where u.Role == "driver"
                join p in _context.Profiles on u.Id equals p.UserId into up
                from p in up.DefaultIfEmpty()
                select new AdminDriverListItemViewModel
                {
                    UserId    = u.Id,
                    ProfileId = p != null ? p.Id : (int?)null,
                    UserName  = u.UserName,
                    FullName  = p != null ? p.FullName : null,
                    Phone     = p != null ? p.Phone : u.Phone,
                    Email     = (p != null && !string.IsNullOrEmpty(p.Email))
                                    ? p.Email!
                                    : u.Email,
                    CitizenId = u.CitizenId,
                    Role      = u.Role,
                    CreatedAt = u.CreatedAt,
                    City      = p != null ? p.City : null,
                    District  = p != null ? p.District : null
                };

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(d =>
                    d.UserName.Contains(search) ||
                    (d.FullName ?? "").Contains(search) ||
                    (d.Phone ?? "").Contains(search));
            }

            var list = await query
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            ViewBag.Search = search;
            return View(list);
        }

        // GET: /AdminDrivers/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var vm = await
                (from u in _context.Users
                 where u.Id == id && u.Role == "driver"
                 join p in _context.Profiles on u.Id equals p.UserId into up
                 from p in up.DefaultIfEmpty()
                 select new AdminDriverEditViewModel
                 {
                     UserId      = u.Id,
                     ProfileId   = p != null ? p.Id : (int?)null,
                     UserName    = u.UserName,
                     FullName    = p != null ? p.FullName : null,
                     Email       = p != null && p.Email != null ? p.Email : u.Email,
                     Phone       = p != null ? p.Phone : u.Phone,
                     CitizenId   = u.CitizenId,
                     Address     = p != null ? p.Address : null,
                     City        = p != null ? p.City : null,
                     District    = p != null ? p.District : null,
                     Ward        = p != null ? p.Ward : null,
                     CompanyName = p != null ? p.CompanyName : u.CompanyName
                 }).FirstOrDefaultAsync();

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        // GET: /AdminDrivers/Create
        public IActionResult Create()
        {
            var vm = new AdminDriverEditViewModel();
            return View(vm);
        }

        // POST: /AdminDrivers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AdminDriverEditViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // 1. Tạo user với role = driver
            var user = new User
            {
                UserName    = model.UserName,
                Password    = model.Password ?? "123456", // TODO: hash password
                Email       = model.Email ?? string.Empty,
                Phone       = model.Phone,
                CitizenId   = model.CitizenId,
                CompanyName = model.CompanyName,
                Role        = "driver",
                CreatedAt   = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 2. Tạo profile
            var profile = new Profile
            {
                UserId      = user.Id,
                FullName    = model.FullName ?? model.UserName,
                Email       = model.Email,
                Phone       = model.Phone,
                Address     = model.Address,
                City        = model.City,
                District    = model.District,
                Ward        = model.Ward,
                CompanyName = model.CompanyName,
                BirthDate   = null,
                TaxCode     = null
            };

            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: /AdminDrivers/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == "driver");

            if (user == null)
                return NotFound();

            var profile = await _context.Profiles
                .FirstOrDefaultAsync(p => p.UserId == id);

            var vm = new AdminDriverEditViewModel
            {
                UserId      = user.Id,
                ProfileId   = profile?.Id,
                UserName    = user.UserName,
                FullName    = profile?.FullName,
                Email       = profile?.Email ?? user.Email,
                Phone       = profile?.Phone ?? user.Phone,
                CitizenId   = user.CitizenId,
                Address     = profile?.Address,
                City        = profile?.City,
                District    = profile?.District,
                Ward        = profile?.Ward,
                CompanyName = profile?.CompanyName ?? user.CompanyName
            };

            return View(vm);
        }

        // POST: /AdminDrivers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AdminDriverEditViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == model.UserId && u.Role == "driver");

            if (user == null)
                return NotFound();

            user.UserName    = model.UserName;
            user.Email       = model.Email ?? user.Email;
            user.Phone       = model.Phone;
            user.CitizenId   = model.CitizenId;
            user.CompanyName = model.CompanyName;

            var profile = await _context.Profiles
                .FirstOrDefaultAsync(p => p.UserId == model.UserId);

            if (profile == null)
            {
                profile = new Profile { UserId = model.UserId };
                _context.Profiles.Add(profile);
            }

            profile.FullName    = model.FullName ?? model.UserName;
            profile.Email       = model.Email;
            profile.Phone       = model.Phone;
            profile.Address     = model.Address;
            profile.City        = model.City;
            profile.District    = model.District;
            profile.Ward        = model.Ward;
            profile.CompanyName = model.CompanyName;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET: /AdminDrivers/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var vm = await
                (from u in _context.Users
                 where u.Id == id && u.Role == "driver"
                 join p in _context.Profiles on u.Id equals p.UserId into up
                 from p in up.DefaultIfEmpty()
                 select new AdminDriverEditViewModel
                 {
                     UserId      = u.Id,
                     ProfileId   = p != null ? p.Id : (int?)null,
                     UserName    = u.UserName,
                     FullName    = p != null ? p.FullName : null,
                     Email       = p != null && p.Email != null ? p.Email : u.Email,
                     Phone       = p != null ? p.Phone : u.Phone,
                     CitizenId   = u.CitizenId,
                     Address     = p != null ? p.Address : null,
                     City        = p != null ? p.City : null,
                     District    = p != null ? p.District : null,
                     Ward        = p != null ? p.Ward : null,
                     CompanyName = p != null ? p.CompanyName : u.CompanyName
                 }).FirstOrDefaultAsync();

            if (vm == null)
                return NotFound();

            return View(vm); // model: AdminDriverEditViewModel
        }

        // POST: /AdminDrivers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == "driver");

            if (user != null)
            {
                // Xóa profile liên quan
                var profiles = _context.Profiles.Where(p => p.UserId == id);
                _context.Profiles.RemoveRange(profiles);

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
