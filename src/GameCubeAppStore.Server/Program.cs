using GameCubeAppStore.Server.Data;
using GameCubeAppStore.Server.Endpoints;
using GameCubeAppStore.Server.Models;
using GameCubeAppStore.Server.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuredRoot = builder.Configuration["Storage:Root"] ?? "data";
var dataRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
    ? configuredRoot : Path.Combine(builder.Environment.ContentRootPath, configuredRoot));
Directory.CreateDirectory(dataRoot);

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 70 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 65 * 1024 * 1024);
builder.Services.AddDbContext<StoreDbContext>(o =>
    o.UseSqlite("Data Source=" + Path.Combine(dataRoot, "catalog.db")));
builder.Services.AddSingleton(new AssetStorage(Path.Combine(dataRoot, "assets")));
builder.Services.AddIdentityApiEndpoints<DeveloperUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<StoreDbContext>();
builder.Services.AddAuthorization();

var app = builder.Build();
// The database is intentionally persistent. Log its location so developers
// can diagnose accounts seemingly disappearing when using another working
// directory, IDE launch configuration or Storage__Root override.
app.Logger.LogInformation("GameCube App Store storage root: {DataRoot}", dataRoot);
app.Logger.LogInformation("GameCube App Store Identity database: {DatabasePath}",
    Path.Combine(dataRoot, "catalog.db"));
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ASP.NET Identity owns /account/register, /account/login and token refresh endpoints.
// Developer write APIs explicitly require bearer authentication, never ambient cookies.
app.MapGroup("/account").MapIdentityApi<DeveloperUser>();
app.MapPublicEndpoints();
app.MapDeveloperEndpoints();
app.MapGet("/api/health", () => Results.Ok(new
{
    name = "GameCube App Store API",
    catalog = "/api/catalog",
    developer = "/api/developer/software",
    identity = "/account/login"
}));

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<StoreDbContext>().Database.EnsureCreatedAsync();

await app.RunAsync();
