using Microsoft.EntityFrameworkCore;
using SteamAnaliticWorker.Data;
using SteamAnaliticWorker.Services;
using SteamAnaliticWorker.Workers;

var builder = WebApplication.CreateBuilder(args);

// Настройка порта для Render (переменная PORT устанавливается автоматически)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Database
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "steam_analytics.db");
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Services
builder.Services.AddHttpClient<SteamAnalyticsService>();
builder.Services.AddScoped<SteamAnalyticsService>();
builder.Services.AddScoped<DataStorageService>();

// Background Worker
builder.Services.AddHostedService<SteamAnalyticsWorker>();

// CORS для веб-интерфейса
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    using var context = await dbContextFactory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

