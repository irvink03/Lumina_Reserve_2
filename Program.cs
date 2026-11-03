using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// 1. INYECCIÓN DE LA BASE DE DATOS
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. INYECCIÓN DEL SERVICIO DE TELEGRAM (Actualizado con HttpClient para las peticiones a la API)
builder.Services.AddHttpClient<TelegramService>();

// 2.1. INYECCIÓN DEL SERVICIO EN SEGUNDO PLANO PARA RECORDATORIOS
builder.Services.AddHostedService<RecordatorioCitasService>();

// 2.2. INYECCIÓN DEL SERVICIO EN SEGUNDO PLANO PARA ESCUCHAR EL BOT DE TELEGRAM
builder.Services.AddHostedService<TelegramBotListenerService>();

// 3. CONFIGURACIÓN DE AUTENTICACIÓN
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

// Agregar controladores y vistas
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configuración de tubería HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();