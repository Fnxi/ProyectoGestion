using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Endpoints;
using WebApp.Models;
using WebApp.Observability;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);
var observability = builder.AddWebAppObservability();

var dbPath = builder.Configuration["DatabasePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "webapp.db");
var connectionString = $"Data Source={dbPath};Foreign Keys=True;Pooling=False";
var dataProtectionKeysPath = builder.Configuration["DataProtectionKeysPath"]
    ?? Path.Combine(Path.GetDirectoryName(dbPath) ?? builder.Environment.ContentRootPath, "data-protection-keys");
var bootstrapAdmin = builder.Configuration
    .GetSection(BootstrapAdminOptions.SectionName)
    .Get<BootstrapAdminOptions>()
    ?? new BootstrapAdminOptions();
var bootstrapOperator = builder.Configuration
    .GetSection(BootstrapOperatorOptions.SectionName)
    .Get<BootstrapOperatorOptions>()
    ?? new BootstrapOperatorOptions();

LegacySchemaInitializer.Initialize(connectionString);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddRazorPages();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("WebApp");
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "WebApp.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.Cookie.Name = "WebApp.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole(IdentitySeedData.AdminRole));
    options.AddPolicy(AuthorizationPolicies.ProductManagement,
        policy => policy.RequireRole(IdentitySeedData.AdminRole, IdentitySeedData.OperatorRole));
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

app.Logger.LogInformation("Application starting. {OtlpExporterConfigured}", observability.IsOtlpConfigured);
app.Lifetime.ApplicationStopping.Register(() =>
    app.Logger.LogInformation("Application stopping."));

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeedData.EnsureRolesAndBootstrapAdminAsync(services, bootstrapAdmin, bootstrapOperator);
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("WebApp.Errors")
        .LogError("Unhandled request failure.");
    await Results.Problem(statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
}));
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<ObservabilityMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapSystemEndpoints(dbPath);
app.MapTaskEndpoints();
app.MapCategoryEndpoints();
app.MapProductEndpoints();
app.MapUserAdministrationEndpoints();
app.MapAuthenticationEndpoints();
app.MapAuthorizationEndpoints();
app.MapRazorPages();

app.Run();

public partial class Program;
