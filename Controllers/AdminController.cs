using System.Data.Common;
using System.Globalization;
using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static Lumina_Reserve_2.Services.ReportesAdminService;

namespace Lumina_Reserve_2.Controllers
{
    // Todas las consultas y operaciones del panel pasan por procedimientos almacenados
    // (Database/sp_PanelAdmin.sql, sp_PersonalYResumen.sql y sp_GestionarCorte.sql).
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private static readonly string[] EstadosPermitidos = { "Pendiente", "Confirmada", "Completada", "Cancelada" };
        private const int MaxDiasGrafica = 62;

        private readonly ApplicationDbContext _context;
        private readonly ReportesAdminService _reportes;
        private readonly ExportadorReportes _exportador;
        private readonly TelegramService _telegram;
        private readonly PasswordService _passwords;
        private readonly ILogger<AdminController> _log;

        public AdminController(ApplicationDbContext context, ReportesAdminService reportes, ExportadorReportes exportador,
                               TelegramService telegram, PasswordService passwords, ILogger<AdminController> log)
        {
            _context = context;
            _reportes = reportes;
            _exportador = exportador;
            _telegram = telegram;
            _passwords = passwords;
            _log = log;
        }

        // ============================== DASHBOARD ==============================

        public async Task<IActionResult> Dashboard(int? sucursalId, string? periodo, DateTime? desde, DateTime? hasta, string? estado)
        {
            var filtro = await _reportes.ConstruirFiltroAsync(sucursalId, periodo, desde, hasta);
            var estadoFiltro = EstadosPermitidos.FirstOrDefault(e => e.Equals(estado, StringComparison.OrdinalIgnoreCase));
            var vm = new DashboardViewModel { Filtro = filtro, EstadoFiltro = estadoFiltro };

            try
            {
                vm.Resumen = await _reportes.ObtenerResumenAsync(filtro);
                vm.Agenda = await _reportes.ObtenerAgendaAsync(filtro, estadoFiltro);
                vm.Barberos = await _reportes.ObtenerRendimientoAsync(filtro);
                vm.TopServicios = await _reportes.ObtenerTopServiciosAsync(filtro, 5);
                vm.GraficaDisponible = (filtro.Hasta.Date - filtro.Desde.Date).Days + 1 <= MaxDiasGrafica;
                if (vm.GraficaDisponible) vm.IngresosPorDia = await _reportes.ObtenerIngresosPorDiaAsync(filtro);
            }
            catch (DbException ex)
            {
                _log.LogError(ex, "Error al consultar el dashboard (sucursal {Id})", filtro.SucursalId);
                ViewBag.ErrorAdmin = "No se pudieron cargar los datos del panel. Revisa que los procedimientos de la carpeta Database estén instalados.";
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int citaId, string estado, string? returnUrl)
        {
            var nuevoEstado = EstadosPermitidos.FirstOrDefault(e => e.Equals(estado, StringComparison.OrdinalIgnoreCase));
            if (nuevoEstado == null) return BadRequest("Estado no válido.");

            try
            {
                var resultado = (await _reportes.EjecutarSpAsync("sp_CambiarEstadoCita",
                    r => new { ChatId = Long(r, "TelegramChatId"), Mensaje = Str(r, "Mensaje") },
                    P("@CitaID", citaId), P("@Estado", nuevoEstado))).FirstOrDefault();

                TempData["ExitoAdmin"] = $"Cita #{citaId} marcada como {nuevoEstado}.";

                if (resultado is { ChatId: > 0, Mensaje: not null })
                {
                    var enviado = await _telegram.EnviarMensajeAsync(resultado.ChatId.Value.ToString(),
                        "✂️ *Lumina Reserve*\n" + resultado.Mensaje);
                    if (enviado) TempData["ExitoAdmin"] += " Se notificó al cliente por Telegram.";
                }
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }

            return RedirigirLocal(returnUrl, nameof(Dashboard));
        }

        // ============================== EXPORTAR ==============================

        [HttpGet]
        public async Task<IActionResult> ExportarExcel(int? sucursalId, string? periodo, DateTime? desde, DateTime? hasta)
        {
            var reporte = await _reportes.ObtenerReporteAsync(await _reportes.ConstruirFiltroAsync(sucursalId, periodo, desde, hasta));
            return File(_exportador.GenerarExcel(reporte),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExportadorReportes.NombreArchivo(reporte, "xlsx"));
        }

        [HttpGet]
        public async Task<IActionResult> ExportarPdf(int? sucursalId, string? periodo, DateTime? desde, DateTime? hasta)
        {
            var reporte = await _reportes.ObtenerReporteAsync(await _reportes.ConstruirFiltroAsync(sucursalId, periodo, desde, hasta));
            return File(_exportador.GenerarPdf(reporte), "application/pdf", ExportadorReportes.NombreArchivo(reporte, "pdf"));
        }

        // ============================== RESUMEN DIARIO ==============================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarResumenAhora(string? returnUrl)
        {
            var resultado = await _reportes.EnviarResumenDiarioAsync(Reloj.Hoy, forzar: true);
            switch (resultado)
            {
                case ResultadoResumen.Enviado:
                    TempData["ExitoAdmin"] = "Resumen diario enviado a tu Telegram."; break;
                case ResultadoResumen.SinDestino:
                    TempData["ErrorAdmin"] = "Falta configurar Telegram:AdminChatId en appsettings."; break;
                default:
                    TempData["ErrorAdmin"] = "No se pudo enviar el resumen por Telegram. Revisa el token del bot."; break;
            }
            return RedirigirLocal(returnUrl, nameof(Dashboard));
        }

        // ============================== GASTOS ==============================

        public async Task<IActionResult> Gastos(int? sucursalId, string? periodo, DateTime? desde, DateTime? hasta)
        {
            var filtro = await _reportes.ConstruirFiltroAsync(sucursalId, periodo, desde, hasta);
            var vm = new GastosViewModel { Filtro = filtro };
            try
            {
                vm.Gastos = await _reportes.ObtenerGastosAsync(filtro);
                vm.Resumen = await _reportes.ObtenerResumenAsync(filtro);
            }
            catch (DbException ex)
            {
                _log.LogError(ex, "Error al consultar gastos");
                ViewBag.ErrorAdmin = "No se pudieron cargar los gastos.";
            }
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarGasto(int sucursalId, string concepto, decimal monto, DateTime? fecha, string? returnUrl)
        {
            concepto = (concepto ?? "").Trim();
            if (concepto.Length > 150) concepto = concepto[..150];
            try
            {
                await _reportes.EjecutarSpAsync("sp_GestionarGasto", r => Int(r, "GastoID"),
                    P("@Opcion", 1), P("@SucursalID", sucursalId), P("@Concepto", concepto),
                    P("@Monto", monto), P("@Fecha", fecha?.Date ?? Reloj.Ahora));
                TempData["ExitoAdmin"] = $"Gasto \"{concepto}\" registrado por {monto.ToString("C", CultureInfo.CurrentCulture)}.";
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }
            return RedirigirLocal(returnUrl, nameof(Gastos));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarGasto(int gastoId, string? returnUrl)
        {
            try
            {
                await _reportes.EjecutarNonQueryAsync("sp_GestionarGasto", P("@Opcion", 2), P("@GastoID", gastoId));
                TempData["ExitoAdmin"] = "Gasto eliminado.";
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }
            return RedirigirLocal(returnUrl, nameof(Gastos));
        }

        // ============================== CATÁLOGO ==============================

        [HttpGet]
        public async Task<IActionResult> CatalogoServicios()
        {
            var cortes = await _context.Cortes.AsNoTracking().OrderBy(c => c.CorteId).ToListAsync();
            return View(cortes);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCorte(int? corteId, string nombre, decimal precio, string? descripcion, int? duracionMinutos)
        {
            nombre = (nombre ?? "").Trim();
            if (nombre.Length < 2 || nombre.Length > 100 || precio <= 0 || precio > 100000)
            {
                TempData["ErrorAdmin"] = "Revisa los datos: nombre (2-100 caracteres) y precio mayor a 0.";
                return RedirectToAction(nameof(CatalogoServicios));
            }
            descripcion = string.IsNullOrWhiteSpace(descripcion) ? "" : descripcion.Trim();
            if (descripcion.Length > 255) descripcion = descripcion[..255];

            // Al editar se conservan el estado y, si no se envió, la duración actual
            // para que los valores por defecto del SP (30 min, activo) no los sobrescriban.
            int duracion = 30;
            bool estado = true;
            if (corteId is > 0)
            {
                var actual = await _context.Cortes.AsNoTracking()
                    .Where(c => c.CorteId == corteId.Value)
                    .Select(c => new { c.DuracionMinutos, c.Estado })
                    .FirstOrDefaultAsync();
                if (actual == null)
                {
                    TempData["ErrorAdmin"] = "El servicio que intentas editar ya no existe.";
                    return RedirectToAction(nameof(CatalogoServicios));
                }
                duracion = actual.DuracionMinutos;
                estado = actual.Estado != false;
            }
            if (duracionMinutos is >= 5 and <= 300) duracion = duracionMinutos.Value;

            try
            {
                await _reportes.EjecutarNonQueryAsync("sp_GestionarCorte",
                    P("@Opcion", 1), P("@Nombre", nombre), P("@Descripcion", descripcion), P("@Precio", precio),
                    P("@DuracionMinutos", duracion), P("@Estado", estado), P("@CorteId", corteId is > 0 ? corteId : null));
            }
            catch (SqlException ex)
            {
                _log.LogError(ex, "sp_GestionarCorte falló para {Nombre}", nombre);
                TempData["ErrorAdmin"] = ex.Class == 16 ? ex.Message : "No se pudo guardar el servicio. Intenta de nuevo.";
                return RedirectToAction(nameof(CatalogoServicios));
            }

            TempData["ExitoAdmin"] = "Catálogo actualizado correctamente.";
            return RedirectToAction(nameof(CatalogoServicios));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoCorte(int corteId, bool activo)
        {
            try
            {
                await _reportes.EjecutarNonQueryAsync("sp_CambiarEstadoCorte", P("@CorteID", corteId), P("@Estado", activo));
                TempData["ExitoAdmin"] = activo
                    ? "Servicio activado: ya aparece al agendar."
                    : "Servicio desactivado: ya no aparece al agendar ni en el portafolio.";
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }
            return RedirectToAction(nameof(CatalogoServicios));
        }

        // ============================== PERSONAL ==============================

        [HttpGet]
        public async Task<IActionResult> PersonalAdmin()
        {
            var vm = new PersonalViewModel
            {
                Barberos = await _reportes.ObtenerBarberosAsync(),
                Sucursales = await _context.Sucursales.AsNoTracking().Where(s => s.Estado != false).OrderBy(s => s.SucursalId).ToListAsync()
            };
            return View(vm);
        }

        // Alta (empleadoId vacío) o edición de un barbero con sp_GestionarEmpleado (opciones 3 y 2).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarBarbero(int? empleadoId, string nombre, string? correo, string? telefono, string? password,
                                                        int sucursalId, string? especialidad, TimeSpan? horarioInicio, TimeSpan? horarioFin)
        {
            nombre = (nombre ?? "").Trim();
            if (nombre.Length > 100) nombre = nombre[..100];
            especialidad = especialidad?.Trim();
            if (especialidad?.Length > 100) especialidad = especialidad[..100];
            telefono = SoloTelefono(telefono);
            var inicio = horarioInicio ?? new TimeSpan(10, 0, 0);
            var fin = horarioFin ?? new TimeSpan(20, 0, 0);

            try
            {
                if (empleadoId is > 0)
                {
                    await _reportes.EjecutarSpAsync("sp_GestionarEmpleado", r => Int(r, "EmpleadoID"),
                        P("@Opcion", 2), P("@EmpleadoID", empleadoId), P("@Nombre", nombre), P("@Telefono", telefono),
                        P("@SucursalID", sucursalId), P("@Especialidad", especialidad),
                        P("@HorarioInicio", inicio), P("@HorarioFin", fin));
                    TempData["ExitoAdmin"] = $"Datos de {nombre} actualizados.";
                }
                else
                {
                    correo = (correo ?? "").Trim().ToLowerInvariant();
                    string? hash = null;
                    if (!string.IsNullOrEmpty(password))
                    {
                        if (password.Length < 8)
                        {
                            TempData["ErrorAdmin"] = "La contraseña inicial debe tener al menos 8 caracteres.";
                            return RedirectToAction(nameof(PersonalAdmin));
                        }
                        hash = _passwords.Hash(new Usuario { Correo = correo, Nombre = nombre }, password);
                    }

                    var creado = (await _reportes.EjecutarSpAsync("sp_GestionarEmpleado", r => Bool(r, "UsuarioCreado"),
                        P("@Opcion", 3), P("@Nombre", nombre), P("@Correo", correo), P("@Telefono", telefono),
                        P("@PasswordHash", hash), P("@SucursalID", sucursalId), P("@Especialidad", especialidad),
                        P("@HorarioInicio", inicio), P("@HorarioFin", fin))).FirstOrDefault();

                    TempData["ExitoAdmin"] = creado
                        ? $"Barbero {nombre} registrado. Puede iniciar sesión con {correo} y la contraseña inicial."
                        : $"Barbero {nombre} registrado usando la cuenta existente de {correo}.";
                }
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }
            return RedirectToAction(nameof(PersonalAdmin));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoEmpleado(int empleadoId, bool activo)
        {
            try
            {
                var proximas = (await _reportes.EjecutarSpAsync("sp_CambiarEstadoEmpleado", r => Int(r, "CitasProximas"),
                    P("@EmpleadoID", empleadoId), P("@Estado", activo))).FirstOrDefault();

                TempData["ExitoAdmin"] = activo ? "Barbero activado: ya se le pueden agendar citas." : "Barbero desactivado.";
                if (!activo && proximas > 0)
                    TempData["ErrorAdmin"] = $"Atención: este barbero tiene {proximas} cita(s) próxima(s) pendiente(s). Reasígnalas o cancélalas desde el dashboard.";
            }
            catch (SqlException ex) when (ex.Class == 16)
            {
                TempData["ErrorAdmin"] = ex.Message;
            }
            return RedirectToAction(nameof(PersonalAdmin));
        }

        // ============================== APOYO ==============================

        private IActionResult RedirigirLocal(string? returnUrl, string accion) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction(accion);

        private static string? SoloTelefono(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var limpio = new string(t.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return limpio.Length is >= 7 and <= 15 ? limpio : null;
        }
    }
}
