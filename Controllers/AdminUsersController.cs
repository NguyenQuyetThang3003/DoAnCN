using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Filters;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    [AdminAuthorize]
    public class AdminUsersController : Controller
    {
        private readonly AppDbContext _db;

        public AdminUsersController(AppDbContext db)
        {
            _db = db;
        }

        // ================== DANH SÁCH NGƯỜI DÙNG ==================

        // GET: /AdminUsers
        // Mặc định role = "all" để hiện tất cả
        public async Task<IActionResult> Index(string role = "all", string? search = null, int page = 1)
        {
            const int PageSize = 20;
            if (page < 1) page = 1;

            var query = _db.Users.AsNoTracking().AsQueryable();

            // Lọc theo vai trò (admin/driver/customer)
            if (!string.IsNullOrWhiteSpace(role) && role.ToLower() != "all")
            {
                query = query.Where(u => u.Role == role);
            }

            // Tìm kiếm theo tên, email, sđt, công ty
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(u =>
                    u.UserName.Contains(keyword) ||
                    u.Email.Contains(keyword) ||
                    u.Phone.Contains(keyword) ||
                    u.CompanyName.Contains(keyword));
            }

            var totalCount = await query.CountAsync();

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(u => new AdminUserListItemViewModel
                {
                    Id          = u.Id,
                    UserName    = u.UserName,
                    Email       = u.Email,
                    Phone       = u.Phone,
                    Role        = u.Role,
                    CompanyName = u.CompanyName,
                    CreatedAt   = u.CreatedAt
                })
                .ToListAsync();

            // Thống kê số lượng theo role
            var roleStats = await _db.Users.AsNoTracking()
                .GroupBy(u => u.Role)
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Role ?? "unknown", x => x.Count);

            var vm = new AdminUserListViewModel
            {
                Users      = users,
                Search     = search,
                RoleFilter = role,
                Page       = page,
                PageSize   = PageSize,
                TotalCount = totalCount,
                RoleStats  = roleStats
            };

            return View(vm);
        }

        // ================== TẠO TÀI KHOẢN TÀI XẾ ==================

        [HttpGet]
        public IActionResult CreateDriver()
        {
            var vm = new CreateDriverViewModel();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDriver(CreateDriverViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Kiểm tra trùng username
            bool existedUser = await _db.Users.AnyAsync(u => u.UserName == model.UserName);
            if (existedUser)
            {
                ModelState.AddModelError(nameof(model.UserName), "Tên đăng nhập đã tồn tại.");
                return View(model);
            }

            // TODO: sau này nên hash password, tạm thời lưu thẳng để bạn test.
            var user = new User
            {
                UserName    = model.UserName.Trim(),
                Password    = model.Password.Trim(),
                Email       = model.Email.Trim(),
                Phone       = model.Phone.Trim(),
                CitizenId   = model.CitizenId?.Trim(),
                CompanyName = model.CompanyName?.Trim(),
                Role        = "driver",
                CreatedAt   = DateTime.Now
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Quay về danh sách mặc định (TẤT CẢ), không ép role = driver nữa
            return RedirectToAction(nameof(Index));
        }
    }

    // ================== VIEW MODELS ==================

    public class AdminUserListViewModel
    {
        public List<AdminUserListItemViewModel> Users { get; set; } = new();

        public string? Search { get; set; }
        public string? RoleFilter { get; set; }

        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public Dictionary<string, int> RoleStats { get; set; } = new();

        public int TotalPages =>
            PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class AdminUserListItemViewModel
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Role { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class CreateDriverViewModel
    {
        [Required, StringLength(100)]
        [Display(Name = "Tên đăng nhập")]
        public string UserName { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 4)]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = "";

        [Required, EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [Required, StringLength(20)]
        [Display(Name = "Số điện thoại")]
        public string Phone { get; set; } = "";

        [StringLength(50)]
        [Display(Name = "CMND/CCCD")]
        public string? CitizenId { get; set; }

        [StringLength(150)]
        [Display(Name = "Tên công ty / đội xe")]
        public string? CompanyName { get; set; }
    }
}
