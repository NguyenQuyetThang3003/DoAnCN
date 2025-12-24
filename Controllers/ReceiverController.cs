using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WedNightFury.Models;
using System;
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

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var key = keyword.Trim().ToLower();
                list = list.Where(r =>
                    (r.Name ?? "").ToLower().Contains(key) ||
                    (r.Phone ?? "").Contains(keyword.Trim()));
            }

            var receivers = list.OrderByDescending(r => r.Id).ToList();
            ViewBag.Keyword = keyword;
            return View(receivers);
        }

        // GET: /Receiver/Details/5
        [HttpGet]
        public IActionResult Details(int id)
        {
            var receiver = _context.Receivers.FirstOrDefault(r => r.Id == id);
            if (receiver == null) return NotFound();
            return View(receiver);
        }

        // GET: /Receiver/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new Receiver());
        }

        // POST: /Receiver/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Receiver model)
        {
            if (!ModelState.IsValid) return View(model);

            _context.Receivers.Add(model);
            _context.SaveChanges();

            TempData["Message"] = "✅ Thêm người nhận thành công!";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Receiver/Edit/5
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var receiver = _context.Receivers.FirstOrDefault(r => r.Id == id);
            if (receiver == null) return NotFound();
            return View(receiver);
        }

        // POST: /Receiver/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, Receiver model)
        {
            if (id != model.Id) return BadRequest();

            if (!ModelState.IsValid) return View(model);

            var receiver = _context.Receivers.FirstOrDefault(r => r.Id == id);
            if (receiver == null) return NotFound();

            receiver.Name = model.Name;
            receiver.Phone = model.Phone;
            receiver.Address = model.Address;
            receiver.SuccessRate = model.SuccessRate; // nếu có field này

            _context.SaveChanges();

            TempData["Message"] = "✅ Cập nhật người nhận thành công!";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Receiver/Delete/5
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var receiver = _context.Receivers.FirstOrDefault(r => r.Id == id);
            if (receiver == null) return NotFound();
            return View(receiver);
        }

        // POST: /Receiver/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var receiver = _context.Receivers.FirstOrDefault(r => r.Id == id);
            if (receiver == null) return NotFound();

            _context.Receivers.Remove(receiver);
            _context.SaveChanges();

            TempData["Message"] = "🗑️ Đã xóa người nhận!";
            return RedirectToAction(nameof(Index));
        }
    }
}
