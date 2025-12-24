using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;

namespace WedNightFury.Controllers
{
    public class AdminPromotionsController : Controller
    {
        private readonly AppDbContext _db;

        public AdminPromotionsController(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(string? q, bool? active)
        {
            var query = _db.GiamGias.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(x => x.Code.Contains(q));

            if (active.HasValue)
                query = query.Where(x => x.Trangthai == active.Value);

            var data = await query
                .OrderByDescending(x => x.Magiamgia)
                .ToListAsync();

            ViewBag.Q = q;
            ViewBag.Active = active;
            return View(data);
        }

        public IActionResult Create()
        {
            var model = new GiamGia
            {
                Ngaybatdau = DateTime.Today,
                Ngayketthuc = DateTime.Today.AddDays(7),
                Trangthai = true
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(GiamGia model)
        {
            if (model.Ngayketthuc < model.Ngaybatdau)
                ModelState.AddModelError("", "Ngày kết thúc không được trước ngày bắt đầu.");

            var code = (model.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
                ModelState.AddModelError(nameof(model.Code), "Vui lòng nhập mã.");

            var exists = await _db.GiamGias.AnyAsync(x => x.Code == code);
            if (exists)
                ModelState.AddModelError(nameof(model.Code), "Mã đã tồn tại.");

            if (!ModelState.IsValid) return View(model);

            model.Code = code.ToUpper();
            model.CreatedAt = DateTime.Now;

            _db.GiamGias.Add(model);
            await _db.SaveChangesAsync();

            TempData["ok"] = "Tạo mã giảm giá thành công.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var item = await _db.GiamGias.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, GiamGia model)
        {
            if (id != model.Magiamgia) return BadRequest();

            if (model.Ngayketthuc < model.Ngaybatdau)
                ModelState.AddModelError("", "Ngày kết thúc không được trước ngày bắt đầu.");

            var code = (model.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
                ModelState.AddModelError(nameof(model.Code), "Vui lòng nhập mã.");

            var exists = await _db.GiamGias.AnyAsync(x => x.Code == code && x.Magiamgia != id);
            if (exists)
                ModelState.AddModelError(nameof(model.Code), "Mã đã tồn tại.");

            if (!ModelState.IsValid) return View(model);

            var item = await _db.GiamGias.FindAsync(id);
            if (item == null) return NotFound();

            item.Code = code.ToUpper();
            item.Giatriphantram = model.Giatriphantram;
            item.Ngaybatdau = model.Ngaybatdau;
            item.Ngayketthuc = model.Ngayketthuc;
            item.Trangthai = model.Trangthai;
            item.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["ok"] = "Cập nhật mã giảm giá thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var item = await _db.GiamGias.FindAsync(id);
            if (item == null) return NotFound();

            item.Trangthai = !item.Trangthai;
            item.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();
            TempData["ok"] = item.Trangthai ? "Đã mở khóa mã." : "Đã khóa mã.";
            return RedirectToAction(nameof(Index));
        }
    }
}
