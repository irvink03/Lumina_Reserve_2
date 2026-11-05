using System.Security.Claims;
using System.Security.Cryptography;
using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Lumina_Reserve_2.Services.ReportesAdminService;

namespace Lumina_Reserve_2.Controllers
{
    public class CuentaController : Controller
    {
        private const int MaxIntentos = 5;
        private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(10);

        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegram;
        private readonly PasswordService _passwords;
        private readonly ImagenService _imagenes;
        private readonly IMemoryCache _cache;
        private readonly ReportesAdminService _sp;
        private readonly ILogger<CuentaController> _log;

        public CuentaController(ApplicationDbContext context, TelegramService telegram, PasswordService passwords,
                                ImagenService imagenes, IMemoryCache cache, ReportesAdminService sp, ILogger<CuentaController> log)
        {
            _sp = sp;
            _context = context;
            _telegram = telegram;
            _passwords = passwords;
            _imagenes = imagenes;
            _cache = cache;
            _log = log;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string correo, string password, string codigoVerificacion)
        {
            correo = (correo ?? "").Trim().ToLowerInvariant();
            var claveCache = "login:" + correo;
            ViewBag.Correo = correo;

            if (_cache.TryGetValue(claveCache, out int intentos) && intentos >= MaxIntentos)
            {
                ViewBag.Error = "Demasiados intentos fallidos. Espera 10 minutos e inténtalo de nuevo.";
                return View();
            }

            var usuario = await _context.Usuarios
                .Include(u => u.Rols)
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == correo);

            bool claveValida = false, rehashear = false;
            if (usuario != null && usuario.Estado != false)
                (claveValida, rehashear) = _passwords.Verificar(usuario, password ?? "");

            if (usuario == null || !claveValida)
            {
                RegistrarFallo(claveCache, intentos);
                ViewBag.Error = "Correo o contraseña incorrectos.";
                return View();
            }

            if (string.IsNullOrWhiteSpace(codigoVerificacion))
            {
                ViewBag.Error = "Ingresa el código de verificación que te envió el bot de Telegram.";
                return View();
            }

            if (!await _telegram.ValidarCodigoVerificacionAsync(usuario.UsuarioId, codigoVerificacion))
            {
                RegistrarFallo(claveCache, intentos);
                ViewBag.Error = "El código de verificación es inválido o expiró.";
                return View();
            }

            _cache.Remove(claveCache);

            // Contraseña + código del bot correctos: el Telegram vinculado es del dueño de la cuenta.
            // Se registra para permitir la recuperación de contraseña por Telegram.
            try { await _sp.EjecutarNonQueryAsync("sp_MarcarTelegramVerificado", P("@UsuarioID", usuario.UsuarioId)); }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo marcar el Telegram del usuario {Id} como verificado.", usuario.UsuarioId); }

            if (rehashear)
            {
                usuario.PasswordHash = _passwords.Hash(usuario, password!);
                await _context.SaveChangesAsync();
                _log.LogInformation("Contraseña del usuario {Id} migrada a hash.", usuario.UsuarioId);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.UsuarioId.ToString()),
                new(ClaimTypes.Name, usuario.Nombre),
                new(ClaimTypes.Email, usuario.Correo)
            };

            var roles = usuario.Rols.Select(r => NormalizarRol(r.Nombre)).Distinct().ToList();
            if (roles.Count == 0) roles.Add("Cliente");
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

            return roles.Contains("Admin")
                ? RedirectToAction("Dashboard", "Admin")
                : RedirectToAction("Index", "Home");
        }

        private void RegistrarFallo(string clave, int intentosPrevios) =>
            _cache.Set(clave, intentosPrevios + 1, Bloqueo);

        private static string NormalizarRol(string nombre) =>
            nombre.Trim().Equals("Administrador", StringComparison.OrdinalIgnoreCase) ? "Admin" : nombre.Trim();

        [HttpGet]
        public IActionResult Registro() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registro([Bind("Nombre,Correo,Telefono,PasswordHash")] Usuario modelo)
        {
            var password = modelo.PasswordHash;
            modelo.Nombre = (modelo.Nombre ?? "").Trim();
            modelo.Correo = (modelo.Correo ?? "").Trim().ToLowerInvariant();
            modelo.Telefono = SoloTelefono(modelo.Telefono);

            if (modelo.Nombre.Length < 2 || !modelo.Correo.Contains('@') || string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                ViewBag.Error = "Completa los datos. La contraseña debe tener al menos 8 caracteres.";
                modelo.PasswordHash = "";
                return View(modelo);
            }

            if (await _context.Usuarios.AnyAsync(u => u.Correo.ToLower() == modelo.Correo))
            {
                ViewBag.Error = "El correo ya se encuentra registrado.";
                modelo.PasswordHash = "";
                return View(modelo);
            }

            modelo.PasswordHash = _passwords.Hash(modelo, password);
            modelo.FechaRegistro = Reloj.Ahora;
            modelo.Estado = true;

            var rol = await _context.Roles.FirstOrDefaultAsync(r => r.Nombre == "Cliente" || r.Nombre == "Usuario")
                      ?? await _context.Roles.FindAsync(2);
            if (rol != null) modelo.Rols.Add(rol);

            try
            {
                _context.Usuarios.Add(modelo);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "Error al registrar usuario {Correo}", modelo.Correo);
                ViewBag.Error = "No se pudo crear la cuenta. Intenta de nuevo.";
                modelo.PasswordHash = "";
                return View(modelo);
            }

            TempData["Exito"] = "Cuenta creada correctamente. Inicia sesión.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccesoDenegado() => View();

        // ============================== RECUPERAR CONTRASEÑA ==============================
        // Paso 1: el usuario escribe su correo y el bot le envía un código a su Telegram.
        // Paso 2: escribe el código y la nueva contraseña (sp_RecuperarPassword).

        private const string MensajeGenerico =
            "Si el correo está registrado y tu Telegram está vinculado y verificado, te enviamos un código de 6 dígitos al bot @LuminaReserva_bot. Es válido por 10 minutos.";

        [HttpGet]
        public IActionResult RecuperarPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecuperarPassword(string correo)
        {
            correo = (correo ?? "").Trim().ToLowerInvariant();
            if (correo.Length > 100 || !correo.Contains('@'))
            {
                ViewBag.Error = "Escribe un correo válido.";
                return View();
            }

            // Límite por correo para no saturar el bot (además del límite de 1 por minuto del SP).
            var claveSolicitudes = "recuperar-solicitud:" + correo;
            var solicitudes = _cache.TryGetValue(claveSolicitudes, out int n) ? n : 0;
            if (solicitudes >= MaxIntentos)
            {
                ViewBag.Error = "Demasiadas solicitudes. Espera 10 minutos e inténtalo de nuevo.";
                return View();
            }
            _cache.Set(claveSolicitudes, solicitudes + 1, Bloqueo);

            var codigo = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            try
            {
                var r = (await _sp.EjecutarSpAsync("sp_RecuperarPassword",
                    x => new { Enviar = Bool(x, "Enviar"), Chat = Long(x, "TelegramChatId"), Nombre = Str(x, "Nombre"), Motivo = Str(x, "Motivo") },
                    P("@Opcion", 1), P("@Correo", correo), P("@Codigo", codigo))).FirstOrDefault();

                if (r is { Enviar: true, Chat: > 0 })
                {
                    var enviado = await _telegram.EnviarMensajeAsync(r.Chat.Value.ToString(),
                        $"🔐 *Lumina Reserve*\nHola {r.Nombre}, tu código para restablecer la contraseña es: *{codigo}*\n\n" +
                        "Es válido por 10 minutos. Si no lo solicitaste, ignora este mensaje: tu contraseña no cambiará.");
                    if (!enviado) _log.LogWarning("No se pudo enviar el código de recuperación a {Correo}", correo);
                }
                else
                {
                    _log.LogInformation("Recuperación de contraseña sin envío para {Correo}: {Motivo}", correo, r?.Motivo);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error al solicitar recuperación de contraseña");
                ViewBag.Error = "No se pudo procesar la solicitud. Intenta de nuevo.";
                return View();
            }

            // Mismo mensaje exista o no la cuenta, para no revelar qué correos están registrados.
            TempData["InfoRecuperar"] = MensajeGenerico;
            return RedirectToAction(nameof(RestablecerPassword), new { correo });
        }

        [HttpGet]
        public IActionResult RestablecerPassword(string? correo)
        {
            ViewBag.Correo = (correo ?? "").Trim().ToLowerInvariant();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestablecerPassword(string correo, string codigo, string nuevaPassword, string confirmarPassword)
        {
            correo = (correo ?? "").Trim().ToLowerInvariant();
            codigo = new string((codigo ?? "").Where(char.IsDigit).ToArray());
            ViewBag.Correo = correo;

            var claveIntentos = "recuperar-intentos:" + correo;
            var intentos = _cache.TryGetValue(claveIntentos, out int i) ? i : 0;
            if (intentos >= MaxIntentos)
            {
                ViewBag.Error = "Demasiados intentos fallidos. Espera 10 minutos y solicita un código nuevo.";
                return View();
            }
            if (codigo.Length != 6)
            {
                ViewBag.Error = "El código debe tener 6 dígitos.";
                return View();
            }
            if (string.IsNullOrEmpty(nuevaPassword) || nuevaPassword.Length < 8)
            {
                ViewBag.Error = "La nueva contraseña debe tener al menos 8 caracteres.";
                return View();
            }
            if (nuevaPassword != confirmarPassword)
            {
                ViewBag.Error = "Las contraseñas no coinciden.";
                return View();
            }

            try
            {
                var hash = _passwords.Hash(new Usuario { Correo = correo, Nombre = "" }, nuevaPassword);
                var r = (await _sp.EjecutarSpAsync("sp_RecuperarPassword",
                    x => new { Chat = Long(x, "TelegramChatId"), Nombre = Str(x, "Nombre") },
                    P("@Opcion", 2), P("@Correo", correo), P("@Codigo", codigo), P("@PasswordHash", hash))).FirstOrDefault();

                _cache.Remove(claveIntentos);
                _cache.Remove("login:" + correo); // desbloquea el login si estaba bloqueado por intentos
                _log.LogInformation("Contraseña restablecida para {Correo}", correo);

                if (r is { Chat: > 0 })
                    await _telegram.EnviarMensajeAsync(r.Chat.Value.ToString(),
                        $"✅ *Lumina Reserve*\nHola {r.Nombre}, tu contraseña se cambió correctamente el {Reloj.Ahora:dd/MM/yyyy} a las {Reloj.Ahora:HH:mm}.\n\n" +
                        "Si no fuiste tú, contacta a la barbería de inmediato.");
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Class == 16)
            {
                _cache.Set(claveIntentos, intentos + 1, Bloqueo);
                ViewBag.Error = ex.Message;
                return View();
            }

            TempData["Exito"] = "¡Listo! Tu contraseña se actualizó. Inicia sesión con la nueva contraseña.";
            return RedirectToAction(nameof(Login));
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Perfil()
        {
            if (!TryGetUsuarioId(out var id)) return RedirectToAction(nameof(Login));
            var usuario = await _context.Usuarios.FindAsync(id);
            return usuario == null ? RedirectToAction(nameof(Login)) : View(usuario);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarPerfil(string nombre, string? telefono, string? nuevaPassword, IFormFile? fotoPerfil)
        {
            if (!TryGetUsuarioId(out var id)) return RedirectToAction(nameof(Login));
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            nombre = (nombre ?? "").Trim();
            if (nombre.Length < 2)
            {
                TempData["ErrorPerfil"] = "El nombre no puede estar vacío.";
                return RedirectToAction(nameof(Perfil));
            }

            if (!string.IsNullOrEmpty(nuevaPassword) && nuevaPassword.Length < 8)
            {
                TempData["ErrorPerfil"] = "La nueva contraseña debe tener al menos 8 caracteres.";
                return RedirectToAction(nameof(Perfil));
            }

            if (fotoPerfil is { Length: > 0 })
            {
                var (ok, error) = await _imagenes.GuardarPerfilAsync(id, fotoPerfil);
                if (!ok)
                {
                    TempData["ErrorPerfil"] = error;
                    return RedirectToAction(nameof(Perfil));
                }
            }

            usuario.Nombre = nombre;
            usuario.Telefono = SoloTelefono(telefono);
            if (!string.IsNullOrEmpty(nuevaPassword))
                usuario.PasswordHash = _passwords.Hash(usuario, nuevaPassword);

            await _context.SaveChangesAsync();

            var claims = User.Claims.Where(c => c.Type != ClaimTypes.Name).ToList();
            claims.Add(new Claim(ClaimTypes.Name, usuario.Nombre));
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

            TempData["ExitoPerfil"] = "¡Perfil actualizado con éxito!";
            return RedirectToAction(nameof(Perfil));
        }

        private bool TryGetUsuarioId(out int id)
        {
            id = 0;
            return int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out id);
        }

        private static string? SoloTelefono(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var limpio = new string(t.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return limpio.Length is >= 7 and <= 15 ? limpio : null;
        }
    }
}