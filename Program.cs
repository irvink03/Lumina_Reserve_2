using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var cadena = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection en appsettings.json o User Secrets.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(cadena));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<ImagenService>();
builder.Services.AddHttpClient<TelegramService>();
builder.Services.AddHostedService<RecordatorioCitasService>();
builder.Services.AddHostedService<TelegramBotListenerService>();
builder.Services.AddScoped<ReportesAdminService>();
builder.Services.AddSingleton<ExportadorReportes>();
builder.Services.AddHostedService<ResumenDiarioService>();

// QuestPDF (exportación del reporte a PDF): licencia Community, gratuita para
// proyectos y empresas con ingresos anuales menores a 1 millón de dólares.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddControllersWithViews();

// Las claves que cifran la cookie de sesión y los tokens antifalsificación se guardan en
// disco (App_Data/keys). En un hosting compartido (Somee/IIS) la app se reinicia seguido;
// sin esto, cada reinicio cerraría la sesión de todos y los formularios fallarían.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("LuminaReserve");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Seguridad:ForzarHttps -> redirige http:// a https:// (activar cuando el certificado SSL ya funcione).
// Seguridad:Hsts -> además obliga al navegador a usar siempre https. Déjalo en false mientras el
// certificado se renueve a mano (Somee gratis): si caduca con HSTS activo, el sitio queda bloqueado.
if (app.Configuration.GetValue("Seguridad:ForzarHttps", app.Environment.IsDevelopment()))
{
    if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Seguridad:Hsts", false)) app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();