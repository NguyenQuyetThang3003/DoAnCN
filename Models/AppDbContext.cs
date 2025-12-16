using Microsoft.EntityFrameworkCore;

namespace WedNightFury.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderTrackingEvent> OrderTrackingEvents { get; set; }   // ✅ THÊM

        public DbSet<Payment> Payments { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Faq> Faqs { get; set; }
        public DbSet<Receiver> Receivers { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<Hub> Hubs { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<DriverLocation> DriverLocations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<OrderTrackingEvent>(e =>
            {
                e.ToTable("order_tracking_events");
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.OrderId, x.CreatedAt });

                e.Property(x => x.Status).HasMaxLength(30).IsRequired();
                e.Property(x => x.Title).HasMaxLength(200);
                e.Property(x => x.Note).HasMaxLength(500);
                e.Property(x => x.Location).HasMaxLength(255);

                e.HasOne(x => x.Order)
                 .WithMany(o => o.TrackingEvents)
                 .HasForeignKey(x => x.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
