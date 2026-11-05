using System.Security.Claims;
using Lumina_Reserve_2.Models;
using Lumina_Reserve_2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lumina_Reserve_2.Controllers
{
    public class CitasController : Controller
    {
        private const string PagoEfectivo = "Efectivo (Pago en sucursal)";
        private const string PagoTarjeta = "Tarjeta (Anticipo 50%)";
        private static readonly TimeSpan Apertura = new(10, 0, 0);
        private static readonly TimeSpan Cierre = new(20, 0, 0);
        private static readonly string[] EstadosQueLiberan = { "Cancelada", "Completada" };

        private static readonly SemaphoreSlim _bloqueoReservas = new(1, 1);

        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegram;
        private readonly IConfiguration _config;
        private readonly ILogger<CitasController> _log;

        public CitasController(ApplicationDbContext context, TelegramService telegram,
                               IConfiguration config, ILogger<CitasController> log)
        {
            _context = context;
            _telegram = telegram;
            _config = config;
            _log = log;
        }

        [HttpGet]
        public async Task<IActionResult> Agendar(string? corte)
        {
            await CargarListasAsync(corte);
            ViewBag.Exito = TempData["Exito"];
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Agendar(int SucursalId, List<int>? ServiciosIds, int? EmpleadoId, DateTime FechaHora,
                                                 string? MetodoPago, string? NombreClienteExterno, string? TelefonoExterno)
        {
            async Task<IActionResult> Falla(string mensaje)
            {
                ViewBag.Error = mensaje;
                await CargarListasAsync(null);
                return View();
            }

            var ids = (ServiciosIds ?? new()).Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return await Falla("Selecciona al menos un servicio o corte.");

            var cortes = await _context.Cortes.Where(c => ids.Contains(c.CorteId) && c.Estado != false).ToListAsync();
            if (cortes.Count != ids.Count) return await Falla("Alguno de los servicios seleccionados ya no está disponible.");

            decimal total = cortes.Sum(c => c.Precio);
            int duracion = Math.Max(15, cortes.Sum(c => c.DuracionMinutos));

            var ahora = Reloj.Ahora;
            FechaHora = new DateTime(FechaHora.Year, FechaHora.Month, FechaHora.Day, FechaHora.Hour, FechaHora.Minute, 0);
            if (FechaHora <= ahora) return await Falla("Elige una fecha y hora posteriores a este momento.");
            if (FechaHora > ahora.AddDays(90)) return await Falla("Solo se puede reservar con un máximo de 90 días de anticipación.");
            if (FechaHora.TimeOfDay < Apertura || FechaHora.TimeOfDay > Cierre)
                return await Falla("El horario de atención es de 10:00 a 20:00.");

            var sucursal = await _context.Sucursales.FindAsync(SucursalId);
            if (sucursal == null || sucursal.Estado == false) return await Falla("La sucursal seleccionada no es válida.");

            int? usuarioId = ObtenerUsuarioId();
            NombreClienteExterno = LimpiarTexto(NombreClienteExterno, 100);
            TelefonoExterno = SoloTelefono(TelefonoExterno);

            if (usuarioId == null && (NombreClienteExterno == null || TelefonoExterno == null))
                return await Falla("Como visitante, escribe tu nombre y un teléfono válido para poder contactarte.");

            var metodo = MetodoPago == PagoTarjeta ? PagoTarjeta : PagoEfectivo;
            decimal anticipo = metodo == PagoTarjeta ? Math.Round(total / 2m, 2) : 0m;

            await _bloqueoReservas.WaitAsync();
            try
            {
                int barberoId;
                if (EmpleadoId is > 0)
                {
                    var elegido = await _context.Empleados.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.EmpleadoId == EmpleadoId && e.SucursalId == SucursalId && e.Estado == true);
                    if (elegido == null) return await Falla("El barbero seleccionado no pertenece a esta sucursal.");
                    if (!await EstaLibreAsync(elegido, FechaHora, duracion))
                        return await Falla("Ese barbero ya tiene una cita que se cruza con ese horario. Elige otra hora u otro barbero.");
                    barberoId = elegido.EmpleadoId;
                }
                else
                {
                    var candidatos = await _context.Empleados.AsNoTracking()
                        .Where(e => e.SucursalId == SucursalId && e.Estado == true).ToListAsync();
                    barberoId = 0;
                    foreach (var b in candidatos)
                    {
                        if (await EstaLibreAsync(b, FechaHora, duracion)) { barberoId = b.EmpleadoId; break; }
                    }
                    if (barberoId == 0)
                        return await Falla("Todos los barberos de esta sucursal están ocupados a esa hora. Elige otro horario.");
                }

                try
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "EXEC sp_RegistrarCitaCompleta @SucursalID, @EmpleadoID, @FechaHora, @UsuarioID, @NombreClienteExterno, @TelefonoExterno, @IdsCortesCsv",
                        new SqlParameter("@SucursalID", SucursalId),
                        new SqlParameter("@EmpleadoID", barberoId),
                        new SqlParameter("@FechaHora", FechaHora),
                        new SqlParameter("@UsuarioID", (object?)usuarioId ?? DBNull.Value),
                        new SqlParameter("@NombreClienteExterno", (object?)NombreClienteExterno ?? DBNull.Value),
                        new SqlParameter("@TelefonoExterno", (object?)TelefonoExterno ?? DBNull.Value),
                        new SqlParameter("@IdsCortesCsv", string.Join(",", ids)));
                }
                catch (SqlException ex)
                {
                    _log.LogError(ex, "sp_RegistrarCitaCompleta falló");
                    return await Falla("No se pudo registrar la cita (el horario pudo ocuparse). Intenta con otra hora.");
                }

                var cita = await _context.Citas.AsNoTracking()
                    .Where(c => c.EmpleadoId == barberoId && c.SucursalId == SucursalId && c.FechaHora == FechaHora)
                    .OrderByDescending(c => c.CitaId)
                    .FirstOrDefaultAsync();

                if (cita != null)
                {
                    var pago = await _context.Pagos.FirstOrDefaultAsync(p => p.CitaId == cita.CitaId);
                    if (pago == null)
                    {
                        _context.Pagos.Add(new Pago
                        {
                            CitaId = cita.CitaId,
                            Monto = metodo == PagoTarjeta ? anticipo : total,
                            MetodoPago = metodo,
                            FechaPago = Reloj.Ahora,
                            EstadoPago = "Pendiente"
                        });
                    }
                    else
                    {
                        pago.MetodoPago = metodo;
                    }
                    await _context.SaveChangesAsync();
                }
            }
            finally
            {
                _bloqueoReservas.Release();
            }

            await NotificarAsync(usuarioId, NombreClienteExterno, TelefonoExterno, sucursal.Nombre,
                                 FechaHora, cortes, total, anticipo, metodo);

            TempData["Exito"] = metodo == PagoTarjeta
                ? $"¡Cita agendada! Anticipo del 50%: {anticipo:C} de {total:C}. Te esperamos."
                : $"¡Cita agendada! Total a pagar en sucursal: {total:C}. Te esperamos.";
            return RedirectToAction(nameof(Agendar));
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHorariosOcupados(int empleadoId, string fecha, int sucursalId = 0)
        {
            if (!DateTime.TryParse(fecha, out var dia)) return Json(new List<string>());
            dia = dia.Date;

            var barberos = _context.Empleados.AsNoTracking().Where(e => e.Estado == true);
            if (empleadoId > 0) barberos = barberos.Where(e => e.EmpleadoId == empleadoId);
            else if (sucursalId > 0) barberos = barberos.Where(e => e.SucursalId == sucursalId);

            var idsBarberos = await barberos.Select(e => e.EmpleadoId).ToListAsync();
            if (idsBarberos.Count == 0) return Json(new List<string>());

            var inicioDia = dia;
            var finDia = dia.AddDays(1);

            var citas = await _context.Citas.AsNoTracking()
                .Include(c => c.Cortes)
                .Where(c => idsBarberos.Contains(c.EmpleadoId)
                         && c.FechaHora >= inicioDia && c.FechaHora < finDia
                         && !EstadosQueLiberan.Contains(c.Estado!))
                .ToListAsync();

            var ocupadas = new List<string>();
            for (var t = Apertura; t <= Cierre; t = t.Add(TimeSpan.FromMinutes(15)))
            {
                var slot = dia + t;
                var barberosOcupados = idsBarberos.Count(id => citas.Any(c =>
                    c.EmpleadoId == id && Solapa(c.FechaHora, DuracionDe(c), slot, 15)));
                if (barberosOcupados >= idsBarberos.Count) ocupadas.Add($"{(int)t.TotalHours:00}:{t.Minutes:00}");
            }
            return Json(ocupadas);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Historial()
        {
            var usuarioId = ObtenerUsuarioId();
            if (usuarioId == null) return RedirectToAction("Login", "Cuenta");

            var citas = await _context.Citas.AsNoTracking()
                .Include(c => c.Sucursal)
                .Include(c => c.Empleado).ThenInclude(e => e!.Usuario)
                .Include(c => c.Cortes)
                .Include(c => c.Pagos)
                .Include(c => c.Resenas)
                .AsSplitQuery()
                .Where(c => c.UsuarioId == usuarioId)
                .OrderByDescending(c => c.FechaHora)
                .ToListAsync();

            return View(citas);
        }

        private async Task CargarListasAsync(string? corte)
        {
            ViewBag.SucursalesList = await _context.Sucursales.AsNoTracking()
                .Where(s => s.Estado != false).OrderBy(s => s.SucursalId).ToListAsync();
            ViewBag.BarberosList = await _context.Empleados.AsNoTracking()
                .Include(e => e.Usuario).Where(e => e.Estado == true)
                .OrderBy(e => e.SucursalId).ThenBy(e => e.EmpleadoId).ToListAsync();
            ViewBag.CortesList = await _context.Cortes.AsNoTracking()
                .Where(c => c.Estado != false).OrderBy(c => c.CorteId).ToListAsync();
            ViewBag.CorteSeleccionado = corte;
        }

        private int? ObtenerUsuarioId() =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

        private static string? LimpiarTexto(string? t, int max)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            t = t.Trim();
            return t.Length > max ? t[..max] : t;
        }

        private static string? SoloTelefono(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var limpio = new string(t.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return limpio.Length is >= 7 and <= 15 ? limpio : null;
        }

        private static int DuracionDe(Cita c) =>
            c.Cortes.Count == 0 ? 30 : Math.Max(15, c.Cortes.Sum(x => x.DuracionMinutos));

        private static bool Solapa(DateTime inicioA, int minA, DateTime inicioB, int minB) =>
            inicioA < inicioB.AddMinutes(minB) && inicioB < inicioA.AddMinutes(minA);

        private async Task<bool> EstaLibreAsync(Empleado barbero, DateTime inicio, int minutos)
        {
            var dia = inicio.Date;
            var delDia = await _context.Citas.AsNoTracking()
                .Include(c => c.Cortes)
                .Where(c => c.EmpleadoId == barbero.EmpleadoId
                         && c.FechaHora >= dia && c.FechaHora < dia.AddDays(1)
                         && !EstadosQueLiberan.Contains(c.Estado!))
                .ToListAsync();

            return !delDia.Any(c => Solapa(c.FechaHora, DuracionDe(c), inicio, minutos));
        }

        private async Task NotificarAsync(int? usuarioId, string? nombreExterno, string? telefonoExterno, string sucursal,
                                          DateTime fecha, List<Corte> cortes, decimal total, decimal anticipo, string metodo)
        {
            var nombre = nombreExterno;
            var telefono = telefonoExterno;
            long? chatCliente = null;

            if (usuarioId.HasValue)
            {
                var u = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.UsuarioId == usuarioId.Value);
                if (u != null)
                {
                    nombre ??= u.Nombre;
                    telefono ??= u.Telefono;
                    chatCliente = u.TelegramChatId;
                }
            }
            nombre ??= "Cliente invitado";
            telefono ??= "Sin teléfono";

            var servicios = string.Join(", ", cortes.Select(c => c.Nombre));
            var pagoTxt = anticipo > 0
                ? $"{metodo}\n💰 *Anticipo:* {anticipo:C} de {total:C}"
                : $"{metodo}\n💰 *Total a pagar:* {total:C}";

            if (chatCliente is > 0)
            {
                await _telegram.EnviarMensajeAsync(chatCliente.Value.ToString(),
                    $"✅ *Lumina Reserve*\n¡Hola {nombre}! Tu cita fue agendada.\n\n" +
                    $"📍 *Sucursal:* {sucursal}\n📅 *Fecha:* {fecha:dd/MM/yyyy}\n🕒 *Hora:* {fecha:HH:mm}\n" +
                    $"✂️ *Servicios:* {servicios}\n💳 *Pago:* {pagoTxt}\n\nTe recomendamos llegar 10 minutos antes.");
            }

            var admin = _config["Telegram:AdminChatId"];
            if (!string.IsNullOrWhiteSpace(admin))
            {
                await _telegram.EnviarMensajeAsync(admin,
                    $"🔔 *NUEVA RESERVA*\n👤 *Cliente:* {nombre}\n📱 *Teléfono:* {telefono}\n🏢 *Sucursal:* {sucursal}\n" +
                    $"📅 *Fecha:* {fecha:dd/MM/yyyy HH:mm}\n✂️ *Servicios:* {servicios}\n💳 *Pago:* {pagoTxt}");
            }
        }
    }
}