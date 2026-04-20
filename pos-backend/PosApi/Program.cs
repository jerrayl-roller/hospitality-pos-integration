using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Hubs;
using PosApi.Middleware;
using PosApi.Services;
using PosApi.Services.Roller;

var builder = WebApplication.CreateBuilder(args);

// Load .env credentials into configuration
LoadDotEnv(builder.Configuration);

// Roller config — override with environment variables
builder.Configuration["Roller:ClientId"] = Environment.GetEnvironmentVariable("ROLLER_CLIENT_ID")
    ?? builder.Configuration["Roller:ClientId"];
builder.Configuration["Roller:ClientSecret"] = Environment.GetEnvironmentVariable("ROLLER_CLIENT_SECRET")
    ?? builder.Configuration["Roller:ClientSecret"];

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

builder.Services.AddDbContext<PosDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Pos")));

builder.Services.AddHttpClient("Roller", client =>
    client.BaseAddress = new Uri(builder.Configuration["Roller:BaseUrl"]!));

builder.Services.AddSingleton<RollerTokenService>();
builder.Services.AddScoped<IRollerApiClient, RollerApiClient>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPaymentLockService, PaymentLockService>();
builder.Services.AddScoped<TabService>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TabHub>("/hubs/tab");

app.Run();

static void LoadDotEnv(IConfigurationBuilder config)
{
    var envFile = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
    if (!File.Exists(envFile)) return;

    foreach (var line in File.ReadAllLines(envFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
        var idx = line.IndexOf('=');
        if (idx < 0) continue;
        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();
        Environment.SetEnvironmentVariable(key, value);
    }
}
