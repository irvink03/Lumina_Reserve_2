using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;

namespace Lumina_Reserve_2.Controllers
{
    public class CitasController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegramService;
        private readonly IConfiguration _config;

        public CitasController(ApplicationDbContext context, TelegramService telegramService, IConfiguration config)
        {
            _context = context;
            _telegramService = telegramService;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> Agendar(string? corte)
        {
            ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
            
            var barberos = await _context.Empleados
                .Include(e => e.Usuario)
                .Where(e => e.Estado == true)
                .ToListAsync();

            ViewBag.BarberosList = barberos;
            ViewBag.CorteSeleccionado = corte;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Agendar(int SucursalId, List<int> ServiciosIds, int? EmpleadoId, DateTime FechaHora, string? MetodoPago, string? NombreClienteExterno, string? TelefonoExterno)
        {
            // --- 1. VALIDACIÓN DE SERVICIOS ---
            if (ServiciosIds == null || !ServiciosIds.Any())
            {
                ViewBag.Error = "Por favor selecciona al menos un servicio o corte para continuar.";
                ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
                ViewBag.BarberosList = await _context.Empleados.Include(e => e.Usuario).Where(e => e.Estado == true).ToListAsync();
                ViewBag.CorteSeleccionado = null;
                return View();
            }

            int barberoIdFinal = 0;

            // --- 2. ASIGNACIÓN INTELIGENTE DE BARBERO ---
            if (!EmpleadoId.HasValue || EmpleadoId.Value <= 0)
            {
                var barberosSucursal = await _context.Empleados
                    .Where(e => e.SucursalId == SucursalId && e.Estado == true)
                    .ToListAsync();

                foreach (var barbero in barberosSucursal)
                {
                    bool ocupado = await _context.Citas.AnyAsync(c =>
                        c.EmpleadoId == barbero.EmpleadoId &&
                        c.FechaHora == FechaHora &&
                        c.Estado != "Cancelada" && 
                        c.Estado != "Completada");

                    if (!ocupado)
                    {
                        barberoIdFinal = barbero.EmpleadoId;
                        break;
                    }
                }

                if (barberoIdFinal == 0)
                {
                    ViewBag.Error = "Lo sentimos, todos los barberos de esta sucursal están ocupados en este horario. Por favor elige otra hora.";
                    ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
                    ViewBag.BarberosList = await _context.Empleados.Include(e => e.Usuario).Where(e => e.Estado == true).ToListAsync();
                    ViewBag.CorteSeleccionado = null;
                    return View();
                }
            }
            else
            {
                var empleadoValido = await _context.Empleados
                    .FirstOrDefaultAsync(e => e.EmpleadoId == EmpleadoId.Value && e.SucursalId == SucursalId && e.Estado == true);

                if (empleadoValido == null)
                {
                    ViewBag.Error = "El barbero seleccionado no es válido para esta sucursal.";
                    ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
                    ViewBag.BarberosList = await _context.Empleados.Include(e => e.Usuario).Where(e => e.Estado == true).ToListAsync();
                    ViewBag.CorteSeleccionado = null;
                    return View();
                }
                barberoIdFinal = EmpleadoId.Value;
            }

            int? usuarioIdActual = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var claimId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(claimId, out int parsedId)) usuarioIdActual = parsedId;
            }

            string serviciosCsv = string.Join(",", ServiciosIds);

            try
            {
                // --- 3. EJECUCIÓN DEL STORED PROCEDURE EN SQL SERVER ---
                var pSucursal = new Microsoft.Data.SqlClient.SqlParameter("@SucursalID", SucursalId);
                var pEmpleado = new Microsoft.Data.SqlClient.SqlParameter("@EmpleadoID", barberoIdFinal);
                var pFechaHora = new Microsoft.Data.SqlClient.SqlParameter("@FechaHora", FechaHora);
                
                var pUsuario = new Microsoft.Data.SqlClient.SqlParameter("@UsuarioID", System.Data.SqlDbType.Int);
                pUsuario.Value = usuarioIdActual.HasValue ? (object)usuarioIdActual.Value : DBNull.Value;

                var pNombreExt = new Microsoft.Data.SqlClient.SqlParameter("@NombreClienteExterno", System.Data.SqlDbType.VarChar, 100);
                pNombreExt.Value = string.IsNullOrWhiteSpace(NombreClienteExterno) ? DBNull.Value : (object)NombreClienteExterno;

                var pTelefonoExt = new Microsoft.Data.SqlClient.SqlParameter("@TelefonoExterno", System.Data.SqlDbType.VarChar, 20);
                pTelefonoExt.Value = string.IsNullOrWhiteSpace(TelefonoExterno) ? DBNull.Value : (object)TelefonoExterno;

                var pCortes = new Microsoft.Data.SqlClient.SqlParameter("@IdsCortesCsv", System.Data.SqlDbType.VarChar);
                pCortes.Value = string.IsNullOrEmpty(serviciosCsv) ? (object)DBNull.Value : serviciosCsv;

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC sp_RegistrarCitaCompleta @SucursalID, @EmpleadoID, @FechaHora, @UsuarioID, @NombreClienteExterno, @TelefonoExterno, @IdsCortesCsv",
                    pSucursal, pEmpleado, pFechaHora, pUsuario, pNombreExt, pTelefonoExt, pCortes
                );
            }
            catch (Exception ex)
            {
                string mensajeLimpio = ex.Message;
                if (mensajeLimpio.Contains("The transaction ended") || mensajeLimpio.Contains("El barbero seleccionado ya"))
                {
                    mensajeLimpio = "El barbero seleccionado ya tiene una cita ocupada en este horario. Por favor selecciona otro.";
                }

                ViewBag.Error = mensajeLimpio;
                ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
                ViewBag.BarberosList = await _context.Empleados.Include(e => e.Usuario).Where(e => e.Estado == true).ToListAsync();
                ViewBag.CorteSeleccionado = null;
                return View();
            }

            // --- 4. NOTIFICACIONES DE TELEGRAM CON NÚMERO DE TELÉFONO ---
            string adminChatId = _config["Telegram:AdminChatId"] ?? string.Empty;
            var sucursal = await _context.Sucursales.FindAsync(SucursalId);
            string nombreSucursalTexto = sucursal?.Nombre ?? "Sucursal General";
            
            string nombreParaNotificar = NombreClienteExterno ?? string.Empty;
            string telefonoParaNotificar = TelefonoExterno ?? "Sin número registrado";

            if (usuarioIdActual.HasValue)
            {
                var usuario = await _context.Usuarios.FindAsync(usuarioIdActual.Value);
                if (usuario != null)
                {
                    string nombreUsuarioReal = usuario.Nombre ?? "Cliente";
                    if (string.IsNullOrEmpty(nombreParaNotificar)) nombreParaNotificar = nombreUsuarioReal;
                    if (!string.IsNullOrEmpty(usuario.Telefono)) telefonoParaNotificar = usuario.Telefono;

                    // Enviar confirmación al Telegram del cliente si tiene cuenta enlazada
                    if (usuario.TelegramChatId.HasValue && usuario.TelegramChatId.Value > 0)
                    {
                        string msjCliente = $"⏳ *Lumina Reserve*\n¡Hola {nombreUsuarioReal}!\n\nTu cita fue agendada con éxito.\n📍 *Sucursal:* {nombreSucursalTexto}\n📅 *Fecha:* {FechaHora:dd/MM/yyyy HH:mm}\n💳 *Pago:* {MetodoPago ?? "Efectivo"}";
                        await _telegramService.EnviarMensajeAsync(usuario.TelegramChatId.Value.ToString(), msjCliente);
                    }
                }
            }

            if (string.IsNullOrEmpty(nombreParaNotificar)) nombreParaNotificar = "Cliente Invitado / Externo";

            // MENSAJE AL ADMIN: Incluye nombre y teléfono para poder contactar por WhatsApp
            string msjAdmin = $"🔔 *NUEVA RESERVA*\n👤 *Cliente:* {nombreParaNotificar}\n📱 *Teléfono:* {telefonoParaNotificar}\n🏢 *Sucursal:* {nombreSucursalTexto}\n📅 *Fecha:* {FechaHora:dd/MM/yyyy HH:mm}\n💳 *Pago:* {MetodoPago ?? "Efectivo"}";

            if (!string.IsNullOrEmpty(adminChatId))
            {
                await _telegramService.EnviarMensajeAsync(adminChatId, msjAdmin);
            }

            ViewBag.Exito = "¡Tu cita ha sido agendada con éxito! Te esperamos.";
            ViewBag.Sucursales = new SelectList(await _context.Sucursales.ToListAsync(), "SucursalId", "Nombre");
            ViewBag.BarberosList = await _context.Empleados.Include(e => e.Usuario).Where(e => e.Estado == true).ToListAsync();
            ViewBag.CorteSeleccionado = null;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHorariosOcupados(int empleadoId, string fecha)
        {
            if (!DateTime.TryParse(fecha, out DateTime fechaSeleccionada))
            {
                return Json(new List<string>());
            }

            IQueryable<Cita> query = _context.Citas.AsNoTracking();

            if (empleadoId > 0)
            {
                query = query.Where(c => c.EmpleadoId == empleadoId);
            }

            var horasOcupadas = await query
                .Where(c => c.FechaHora.Date == fechaSeleccionada.Date 
                         && c.Estado != "Cancelada" 
                         && c.Estado != "Completada")
                .Select(c => c.FechaHora.ToString("HH:mm"))
                .Distinct()
                .ToListAsync();

            return Json(horasOcupadas);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Historial()
        {
            var claimId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claimId, out int usuarioId))
            {
                var citasUsuario = await _context.Citas
                    .Include(c => c.Sucursal)
                    .Include(c => c.Empleado)
                    .ThenInclude(e => e!.Usuario)
                    .Where(c => c.UsuarioId == usuarioId)
                    .OrderByDescending(c => c.FechaHora)
                    .ToListAsync();

                return View(citasUsuario);
            }

            return RedirectToAction("Login", "Cuenta");
        }
    }
}