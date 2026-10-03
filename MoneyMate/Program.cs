using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using MoneyMate.Data;
using MoneyMate.Models;
using MoneyMate.Security;
using MoneyMate.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
        .AddUserSecrets<MoneyMateDbContext>(optional: true)
        .AddEnvironmentVariables();
}
builder.Services.AddRazorPages();
builder.Services.AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddOpenApi();
builder.Services.AddDbContext<MoneyMateDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MoneyMate")));
builder.Services.AddScoped<DatabaseStatusService>();
builder.Services.AddSingleton<KoreanClock>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<MoneyMateDbContext>().AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, MoneyMateClaimsPrincipalFactory>();
builder.Services.AddScoped<AccountService>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MoneyMate.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "MoneyMate.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "요청이 너무 많습니다.", status = 429,
            detail = "잠시 후 다시 시도해주세요.", code = "rate_limited"
        }, cancellationToken);
    };
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/api/development/database-status", async (
        DatabaseStatusService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.CheckAsync(cancellationToken)));
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
// Schema changes are applied explicitly through EF migrations, never on web startup.
app.Run();

