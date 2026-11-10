using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FitnessCenter.Web.Components;
using FitnessCenter.Web.Components.Account;
using FitnessCenter.Web.Data;
using FitnessCenter.Web.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

if (builder.Configuration["Design:Variant"] == "Palette")
    builder.Services.ConfigureApplicationCookie(options => options.Cookie.Name = ".Pulse.Palette.Auth");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ClubClock>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PublicDataCache>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<CatalogAdminService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddScoped<SiteContentService>();
builder.Services.AddAuthorization();

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/images/catalog/{id:guid}", async (Guid id, IDbContextFactory<ApplicationDbContext> factory,
    HttpContext http, CancellationToken cancellationToken) =>
{
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var image = await db.CatalogImages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (image is null) return Results.NotFound();

    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    http.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.File(image.Content, image.ContentType);
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

await DbSeeder.InitializeAsync(app.Services, app.Environment);

app.Run();
