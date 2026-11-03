using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using System.IO;
using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Lumina_Reserve_2.Controllers
{
    public class CuentaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegramService;
        private readonly IWebHostEnvironment _hostEnvironment;

        public CuentaController(ApplicationDbContext context, TelegramService telegramService, IWebHostEnvironment hostEnvironment)
        {
            _context = context;
            _telegramService = telegramService;
            _hostEnvironment = hostEnvironment;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string correo, string password, string codigoVerificacion)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
            
            // 1. Validar Credenciales básicas
            if (usuario == null || usuario.PasswordHash != password)
            {
                ViewBag.Error = "Correo o contraseña incorrectos.";
                return View();
            }

            // 2. EXIGIR OBLIGATORIAMENTE EL CÓDIGO 2FA DE TELEGRAM
            if (string.IsNullOrWhiteSpace(codigoVerificacion))
            {
                ViewBag.Error = "Por favor ingresa el código de verificación enviado a Telegram.";
                return View();
            }

            // 3. Validar el código de verificación con el servicio de Telegram
            bool codigoValido = await _telegramService.ValidarCodigoVerificacionAsync(usuario.UsuarioId, codigoVerificacion);
            if (!codigoValido)
            {
                ViewBag.Error = "El código de verificación de Telegram es inválido o expiró.";
                return View();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioId.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nombre),
                new Claim(ClaimTypes.Email, usuario.Correo)
            };

            string rolAsignado = "Cliente"; 
            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                }

                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = @"
                        SELECT r.Nombre 
                        FROM UsuarioRoles ur
                        INNER JOIN Roles r ON ur.RolID = r.RolID
                        WHERE ur.UsuarioID = @UsuarioId";
                        
                    var param = command.CreateParameter();
                    param.ParameterName = "@UsuarioId";
                    param.Value = usuario.UsuarioId;
                    command.Parameters.Add(param);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            rolAsignado = reader.GetString(0);
                        }
                    }
                }
            }
            finally
            {
                // CERRAR CONEXIÓN PARA PREVENIR TIMEOUTS EN SQL SERVER
                await _context.Database.CloseConnectionAsync();
            }

            claims.Add(new Claim(ClaimTypes.Role, rolAsignado));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Registro() => View();

        [HttpPost]
        public async Task<IActionResult> Registro(Usuario modelo, string password, string passwordConfirm)
        {
            // 1. Validar si el correo ya existe en la base de datos
            if (await _context.Usuarios.AnyAsync(u => u.Correo == modelo.Correo))
            {
                ViewBag.Error = "El correo ya se encuentra registrado.";
                return View(modelo);
            }

            // 2. Mapear explícitamente la contraseña del formulario a PasswordHash
            string claveFinal = !string.IsNullOrEmpty(modelo.PasswordHash) 
                ? modelo.PasswordHash 
                : (!string.IsNullOrEmpty(password) ? password : (passwordConfirm ?? ""));

            if (string.IsNullOrWhiteSpace(claveFinal))
            {
                ViewBag.Error = "Por favor ingresa una contraseña válida.";
                return View(modelo);
            }

            modelo.PasswordHash = claveFinal;
            modelo.FechaRegistro = DateTime.Now;
            modelo.Estado = true; // Activo por defecto

            try
            {
                _context.Usuarios.Add(modelo);
                await _context.SaveChangesAsync();

                // 3. Asignar el RolID = 2 (Cliente) en UsuarioRoles mediante SQL estricto
                var paramUsuarioId = new Microsoft.Data.SqlClient.SqlParameter("@UsuarioID", modelo.UsuarioId);
                var paramRolId = new Microsoft.Data.SqlClient.SqlParameter("@RolID", 2);

                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO UsuarioRoles (UsuarioID, RolID) VALUES (@UsuarioID, @RolID)",
                    paramUsuarioId, paramRolId);

                TempData["Exito"] = "Cuenta creada correctamente. Inicia sesión.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Ocurrió un error al registrar la cuenta: " + ex.Message;
                return View(modelo);
            }
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Perfil()
        {
            var claimId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claimId, out int usuarioId))
            {
                var usuario = await _context.Usuarios.FindAsync(usuarioId);
                if (usuario != null)
                {
                    return View(usuario);
                }
            }
            return RedirectToAction("Login");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ActualizarPerfil(string nombre, string telefono, string? nuevaPassword, IFormFile? fotoPerfil)
        {
            var claimId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(claimId, out int usuarioId)) return RedirectToAction("Login");

            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null) return NotFound();

            usuario.Nombre = nombre;
            usuario.Telefono = telefono;

            if (!string.IsNullOrEmpty(nuevaPassword))
            {
                usuario.PasswordHash = nuevaPassword;
            }

            if (fotoPerfil != null && fotoPerfil.Length > 0)
            {
                string uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, "images", "perfiles");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                
                string filePath = Path.Combine(uploadsFolder, $"{usuarioId}.jpg");
                
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await fotoPerfil.CopyToAsync(fileStream);
                }
            }

            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();

            TempData["ExitoPerfil"] = "¡Perfil actualizado con éxito!";
            return RedirectToAction("Perfil");
        }
    }
}