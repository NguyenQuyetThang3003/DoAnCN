using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Net;
using WedNightFury.Models;
using WedNightFury.Services;

var builder = WebApplication.CreateBuilder(args);

// ===================== QuestPDF =====================
QuestPDF.Settings.License = LicenseType.Community;

// ===================== MVC =====================
builder.Services.AddControllersWithViews();

// ===================== DI Services =====================
builder.Services.AddScoped<IOrderTrackingService, OrderTrackingService>();
builder.Services.AddScoped<IGeoCodingService, NominatimGeoCodingService>();

// Cho Filter/View inject IHttpContextAccessor
builder.Services.AddHttpContextAccessor();

// ===================== Anti-forgery =====================
// chạy OK trên http localhost (dev). Production vẫn ok vì SecurePolicy = SameAsRequest.
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "NF.AntiForgery";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ===================== Cookie Policy =====================
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.SameAsRequest;
});

// ===================== Auth (Cookie) =====================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/Denied";
        options.ExpireTimeSpan = TimeSpan.FromDays(3);
        options.SlidingExpiration = true;

        options.Cookie.Name = "NF.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// ===================== Session =====================
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.Name = "NF.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ===================== DbContext (MySQL) =====================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ===================== HttpClient (Nominatim) =====================
builder.Services.AddHttpClient("nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(15);

    client.DefaultRequestHeaders.UserAgent.Clear();
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "WedNightFury/1.0 (contact: hoainam1872004@gmail.com)");

    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
})
.SetHandlerLifetime(TimeSpan.FromMinutes(5));

var app = builder.Build();

// ===================== Pipeline =====================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // production nên bật
}
// Dev: nếu bạn đã cấu hình HTTPS thì có thể bật dòng dưới
// else app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// ✅ CookiePolicy phải trước Session/Auth
app.UseCookiePolicy();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// ===================== Routes =====================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
