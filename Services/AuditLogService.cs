using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WedNightFury.Models;

namespace WedNightFury.Services
{
    public class AuditLogService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditLogService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task WriteAsync(
            int adminId,
            string adminName,
            string action,
            string entityType,
            int? entityId,
            string? description,
            string? oldValue = null,
            string? newValue = null)
        {
            var ip = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

            var log = new AuditLog
            {
                AdminId = adminId,
                AdminName = adminName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                OldValue = oldValue,
                NewValue = newValue,
                IpAddress = ip,
                CreatedAt = DateTime.Now
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
