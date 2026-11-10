using FitnessCenter.Lite.Components;
using System.Globalization;

// Use the assets next to the executable, independently of the working directory.
var binaryDirectory = AppContext.BaseDirectory;
var contentDirectory = Directory.Exists(Path.Combine(binaryDirectory, "wwwroot"))
    ? binaryDirectory
    : Directory.GetCurrentDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentDirectory,
    WebRootPath = Path.Combine(contentDirectory, "wwwroot")
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync("<html lang='ru'><meta charset='utf-8'><title>Пульс</title><p>Не удалось загрузить страницу. Попробуйте позже.</p><a href='/'>На главную</a></html>");
    }));
}
app.UseStatusCodePages(async context =>
{
    context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
    await context.HttpContext.Response.WriteAsync("<html lang='ru'><meta charset='utf-8'><title>Страница не найдена · Пульс</title><h1>Страница не найдена</h1><a href='/'>На главную</a></html>");
});
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

