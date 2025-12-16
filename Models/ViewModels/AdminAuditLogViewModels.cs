using System;
using System.Collections.Generic;

namespace WedNightFury.Models.ViewModels
{
    public class AdminAuditLogListViewModel
    {
        public List<AuditLog> Items { get; set; } = new();

        public string? Keyword { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To   { get; set; }

        public int PageIndex  { get; set; }
        public int PageSize   { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
    }
}
