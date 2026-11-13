using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PermitDesk.Api.Auth;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Ops;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var connectionString = builder.Configuration.GetConnectionString("PermitDesk")
    ?? throw new InvalidOperationException("ConnectionStrings:PermitDesk is not configured.");

builder.Services.AddDbContext<PermitDeskDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// One-off commands: `dotnet PermitDesk.Api.dll migrate` and `... seed`.
if (args.Contains("migrate") || args.Contains("seed"))
{
    using var tools = builder.Build();
    var log = tools.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PermitDesk.Setup");

    if (args.Contains("migrate"))
    {
        var directory = builder.Configuration["Database:MigrationsPath"] ?? "db/migrations";
        var applied = await Migrator.RunAsync(connectionString, directory, log);
        log.LogInformation("Migrations complete, {Count} applied", applied);
    }

    if (args.Contains("seed"))
    {
        using var scope = tools.Services.CreateScope();
        await SeedData.RunAsync(
            scope.ServiceProvider.GetRequiredService<PermitDeskDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
            log);
    }

    return;
}

var signingKey = TokenService.KeyFrom(builder.Configuration);

builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = TokenService.Issuer,
            ValidAudience = TokenService.Issuer,
            IssuerSigningKey = signingKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("intake", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddSingleton<ServiceMetrics>();

void AddCityService<TClient>(string name) where TClient : class
{
    builder.Services
        .AddHttpClient<TClient>((services, http) =>
        {
            var settings = services.GetRequiredService<IConfiguration>().GetSection($"Services:{name}");
            http.BaseAddress = new Uri(settings["BaseUrl"]
                ?? throw new InvalidOperationException($"Services:{name}:BaseUrl is not configured."));
            http.DefaultRequestHeaders.Add("X-Api-Key", settings["ApiKey"]);
            http.Timeout = TimeSpan.FromSeconds(settings.GetValue("TimeoutSeconds", 10));
        })
        .AddHttpMessageHandler(services =>
            new OutboundMetricsHandler(name.ToLowerInvariant(), services.GetRequiredService<ServiceMetrics>()));
}

AddCityService<ZoningClient>("Zoning");
AddCityService<ReviewClient>("Review");
AddCityService<SchedulingClient>("Scheduling");
AddCityService<CashieringClient>("Cashiering");

builder.Services.AddScoped<ZoningOutcomes>();
builder.Services.AddScoped<FeeQuoteService>();

builder.Services.AddScoped<IJobHandler, ZoningDispatchHandler>();
builder.Services.AddScoped<IJobHandler, ReviewSubmitHandler>();
builder.Services.AddScoped<IJobHandler, FeeQuoteHandler>();
builder.Services.AddScoped<IJobHandler, InspectionConfirmHandler>();
builder.Services.AddScoped<IJobHandler, NotificationHandler>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddHostedService<ZoningReconciler>();
builder.Services.AddHostedService<ReviewPoller>();
builder.Services.AddHostedService<PermitExpirySweeper>();

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

builder.Services.AddControllers();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "permit-desk" }));

app.MapGet("/metrics", async (ServiceMetrics metrics, PermitDeskDbContext db, CancellationToken ct) =>
    Results.Text(await metrics.RenderAsync(db, ct), "text/plain; version=0.0.4"));

app.Run();

public partial class Program
{
}
