using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Lumina_Reserve_2.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lumina_Reserve_2.Services
{
    // Acceso a los procedimientos almacenados del panel de administración.
    // Lo usan el AdminController, la exportación a Excel/PDF y el resumen diario por Telegram.
    public class ReportesAdminService
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegram;
        private readonly IConfiguration _config;
        private readonly ILogger<ReportesAdminService> _log;

        public ReportesAdminService(ApplicationDbContext context, TelegramService telegram,
                                    IConfiguration config, ILogger<ReportesAdminService> log)
        {
            _context = context;
            _telegram = telegram;
            _config = config;
            _log = log;
        }

        // ============================== FILTRO ==============================

        public async Task<FiltroAdmin> ConstruirFiltroAsync(int? sucursalId, string? periodo, DateTime? desde, DateTime? hasta)
        {
            var sucursales = await _context.Sucursales.AsNoTracking()
                .Where(s => s.Estado != false).OrderBy(s => s.SucursalId).ToListAsync();
            var sucursal = sucursales.FirstOrDefault(s => s.SucursalId == sucursalId) ?? sucursales.FirstOrDefault();

            var hoy = Reloj.Hoy;
            DateTime ini, fin;
            var clave = (periodo ?? "mes").ToLowerInvariant();
            switch (clave)
            {
                case "hoy":
                    ini = fin = hoy; break;
                case "semana":
                    var offset = ((int)hoy.DayOfWeek + 6) % 7; // lunes = 0
                    ini = hoy.AddDays(-offset); fin = ini.AddDays(6); break;
                case "30d":
                    ini = hoy.AddDays(-29); fin = hoy; break;
                case "rango" when desde.HasValue && hasta.HasValue:
                    ini = desde.Value.Date; fin = hasta.Value.Date;
                    if (fin < ini) (ini, fin) = (fin, ini);
                    if ((fin - ini).TotalDays > 366) ini = fin.AddDays(-366);
                    break;
                default:
                    clave = "mes";
                    ini = new DateTime(hoy.Year, hoy.Month, 1); fin = ini.AddMonths(1).AddDays(-1); break;
            }

            return new FiltroAdmin
            {
                SucursalId = sucursal?.SucursalId ?? 0,
                SucursalNombre = sucursal?.Nombre ?? "",
                Sucursales = sucursales,
                Periodo = clave,
                Desde = ini,
                Hasta = fin
            };
        }

        // ============================== CONSULTAS ==============================

        public async Task<ResumenDashboard> ObtenerResumenAsync(FiltroAdmin f) =>
            (await EjecutarSpAsync("sp_DashboardResumen", r => new ResumenDashboard
            {
                CitasTotal = Int(r, "CitasTotal"),
                Pendientes = Int(r, "Pendientes"),
                Confirmadas = Int(r, "Confirmadas"),
                Completadas = Int(r, "Completadas"),
                Canceladas = Int(r, "Canceladas"),
                Ingresos = Dec(r, "Ingresos"),
                Gastos = Dec(r, "Gastos"),
                Utilidad = Dec(r, "Utilidad"),
                TicketPromedio = Dec(r, "TicketPromedio"),
                ClientesUnicos = Int(r, "ClientesUnicos"),
                CalificacionPromedio = DecNull(r, "CalificacionPromedio"),
                TotalResenas = Int(r, "TotalResenas")
            }, ParametrosRango(f))).FirstOrDefault() ?? new ResumenDashboard();

        public Task<List<CitaAgenda>> ObtenerAgendaAsync(FiltroAdmin f, string? estado) =>
            EjecutarSpAsync("sp_AgendaAdmin", r => new CitaAgenda
            {
                CitaId = Int(r, "CitaID"),
                FechaHora = r.GetDateTime(r.GetOrdinal("FechaHora")),
                Estado = Str(r, "Estado") ?? "Pendiente",
                Cliente = Str(r, "Cliente") ?? "Cliente de paso",
                Telefono = Str(r, "Telefono"),
                TieneCuenta = Bool(r, "TieneCuenta"),
                Barbero = Str(r, "Barbero") ?? "",
                Servicios = Str(r, "Servicios") ?? "",
                Total = Dec(r, "Total"),
                Minutos = Int(r, "Minutos"),
                MetodoPago = Str(r, "MetodoPago"),
                EstadoPago = Str(r, "EstadoPago")
            }, ParametrosRango(f).Append(P("@Estado", estado)).ToArray());

        public Task<List<RendimientoBarbero>> ObtenerRendimientoAsync(FiltroAdmin f) =>
            EjecutarSpAsync("sp_RendimientoBarberos", r => new RendimientoBarbero
            {
                EmpleadoId = Int(r, "EmpleadoID"),
                Nombre = Str(r, "Nombre") ?? "",
                Activo = Bool(r, "Activo"),
                Citas = Int(r, "Citas"),
                Completadas = Int(r, "Completadas"),
                Canceladas = Int(r, "Canceladas"),
                Ingresos = Dec(r, "Ingresos"),
                Calificacion = DecNull(r, "Calificacion"),
                Resenas = Int(r, "Resenas")
            }, ParametrosRango(f));

        public Task<List<ServicioVendido>> ObtenerTopServiciosAsync(FiltroAdmin f, int top) =>
            EjecutarSpAsync("sp_ServiciosMasVendidos", r => new ServicioVendido
            {
                Nombre = Str(r, "Nombre") ?? "",
                Veces = Int(r, "Veces"),
                Monto = Dec(r, "Monto")
            }, ParametrosRango(f).Append(P("@Top", top)).ToArray());

        // El SP solo devuelve días con ingresos; aquí se completan los demás con 0.
        public async Task<List<IngresoDia>> ObtenerIngresosPorDiaAsync(FiltroAdmin f)
        {
            var porDia = (await EjecutarSpAsync("sp_IngresosPorDia", r => new IngresoDia
            {
                Dia = r.GetDateTime(r.GetOrdinal("Dia")),
                Ingresos = Dec(r, "Ingresos"),
                Citas = Int(r, "Citas")
            }, ParametrosRango(f))).ToDictionary(x => x.Dia.Date);

            var dias = (f.Hasta.Date - f.Desde.Date).Days + 1;
            return Enumerable.Range(0, dias)
                .Select(i => f.Desde.Date.AddDays(i))
                .Select(d => porDia.TryGetValue(d, out var v) ? v : new IngresoDia { Dia = d })
                .ToList();
        }

        public Task<List<GastoItem>> ObtenerGastosAsync(FiltroAdmin f) =>
            EjecutarSpAsync("sp_ObtenerGastos", r => new GastoItem
            {
                GastoId = Int(r, "GastoID"),
                Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
                Concepto = Str(r, "Concepto") ?? "",
                Monto = Dec(r, "Monto")
            }, ParametrosRango(f));

        public Task<List<BarberoAdminViewModel>> ObtenerBarberosAsync() =>
            EjecutarSpAsync("sp_ObtenerBarberosAdmin", r => new BarberoAdminViewModel
            {
                EmpleadoId = Int(r, "EmpleadoId"),
                NombreBarbero = Str(r, "NombreBarbero") ?? "",
                Correo = Str(r, "Correo") ?? "",
                SucursalAsignada = Str(r, "SucursalAsignada") ?? "",
                Especialidad = Str(r, "Especialidad"),
                HorarioInicio = Hora(r, "HorarioInicio"),
                HorarioFin = Hora(r, "HorarioFin"),
                Activo = Bool(r, "Activo"),
                CitasProximas = Int(r, "CitasProximas"),
                CitasCompletadas = Int(r, "CitasCompletadas"),
                SucursalId = Int(r, "SucursalID"),
                Telefono = Str(r, "Telefono")
            });

        // Todo lo necesario para el reporte exportable (Excel / PDF).
        public async Task<ReporteAdmin> ObtenerReporteAsync(FiltroAdmin f) => new()
        {
            Filtro = f,
            Resumen = await ObtenerResumenAsync(f),
            Agenda = await ObtenerAgendaAsync(f, null),
            Barberos = await ObtenerRendimientoAsync(f),
            TopServicios = await ObtenerTopServiciosAsync(f, 10),
            IngresosPorDia = await ObtenerIngresosPorDiaAsync(f),
            Gastos = await ObtenerGastosAsync(f),
            Generado = Reloj.Ahora
        };

        // ============================== RESUMEN DIARIO (TELEGRAM) ==============================

        public enum ResultadoResumen { Enviado, YaEnviado, SinDestino, ErrorTelegram }

        public async Task<ResultadoResumen> EnviarResumenDiarioAsync(DateTime fecha, bool forzar, CancellationToken ct = default)
        {
            var chat = _config["Telegram:AdminChatId"];
            if (string.IsNullOrWhiteSpace(chat)) return ResultadoResumen.SinDestino;
            long.TryParse(chat, out var chatId);

            var dia = new SqlParameter("@Fecha", SqlDbType.Date) { Value = fecha.Date };
            if (!forzar)
            {
                var yaEnviado = (await EjecutarSpAsync("sp_ControlResumenDiario", r => Bool(r, "YaEnviado"),
                    P("@Opcion", 1), new SqlParameter("@Fecha", SqlDbType.Date) { Value = fecha.Date })).FirstOrDefault();
                if (yaEnviado) return ResultadoResumen.YaEnviado;
            }

            var (sucursales, citas) = await LeerResumenDiarioAsync(fecha.Date, ct);
            var mensaje = ArmarMensajeResumen(fecha.Date, sucursales, citas);

            if (!await _telegram.EnviarMensajeAsync(chat, mensaje)) return ResultadoResumen.ErrorTelegram;

            var totalHoy = sucursales.Sum(s => s.CitasHoy);
            var ingresosAyer = sucursales.Sum(s => s.IngresosAyer);
            try
            {
                await EjecutarNonQueryAsync("sp_ControlResumenDiario",
                    P("@Opcion", 2), dia, P("@ChatId", chatId == 0 ? null : chatId),
                    P("@Mensaje", $"Resumen diario {fecha:dd/MM/yyyy}: {totalHoy} citas hoy, ingresos de ayer {ingresosAyer:C}"));
            }
            catch (SqlException ex)
            {
                _log.LogWarning(ex, "El resumen se envió pero no se pudo registrar en Notificaciones.");
            }
            return ResultadoResumen.Enviado;
        }

        private record SucursalDia(string Nombre, int CitasHoy, int PorAtenderHoy, decimal IngresosAyer,
                                   int CompletadasAyer, int CanceladasAyer, decimal GastosAyer);
        private record CitaDia(string Sucursal, DateTime FechaHora, string Estado, string Cliente, string Barbero, string Servicios);

        private async Task<(List<SucursalDia>, List<CitaDia>)> LeerResumenDiarioAsync(DateTime fecha, CancellationToken ct)
        {
            var sucursales = new List<SucursalDia>();
            var citas = new List<CitaDia>();
            await ConConexionAsync(async conexion =>
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = "sp_ResumenDiarioAdmin";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@Fecha", SqlDbType.Date) { Value = fecha });

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    sucursales.Add(new SucursalDia(Str(r, "Nombre") ?? "", Int(r, "CitasHoy"), Int(r, "PorAtenderHoy"),
                        Dec(r, "IngresosAyer"), Int(r, "CompletadasAyer"), Int(r, "CanceladasAyer"), Dec(r, "GastosAyer")));
                if (await r.NextResultAsync(ct))
                    while (await r.ReadAsync(ct))
                        citas.Add(new CitaDia(Str(r, "Sucursal") ?? "", r.GetDateTime(r.GetOrdinal("FechaHora")), Str(r, "Estado") ?? "",
                            Str(r, "Cliente") ?? "", Str(r, "Barbero") ?? "", Str(r, "Servicios") ?? ""));
            });
            return (sucursales, citas);
        }

        private static string ArmarMensajeResumen(DateTime fecha, List<SucursalDia> sucursales, List<CitaDia> citas)
        {
            var mx = CultureInfo.CurrentCulture;
            var es = new CultureInfo("es-MX");
            var sb = new StringBuilder();
            sb.AppendLine("📊 *Lumina Reserve · Resumen diario*");
            sb.AppendLine(Limpiar(fecha.ToString("dddd dd 'de' MMMM, yyyy", es)));
            sb.AppendLine();

            sb.AppendLine($"💰 *Ayer* ({fecha.AddDays(-1):dd/MM})");
            foreach (var s in sucursales)
            {
                var linea = $"• {Limpiar(s.Nombre)}: {s.IngresosAyer.ToString("C", mx)} · {s.CompletadasAyer} completadas";
                if (s.CanceladasAyer > 0) linea += $" · {s.CanceladasAyer} canceladas";
                if (s.GastosAyer > 0) linea += $" · gastos {s.GastosAyer.ToString("C", mx)}";
                sb.AppendLine(linea);
            }
            if (sucursales.Count > 1)
                sb.AppendLine($"*Total ayer: {sucursales.Sum(s => s.IngresosAyer).ToString("C", mx)}*");
            sb.AppendLine();

            sb.AppendLine($"📅 *Hoy*: {sucursales.Sum(s => s.CitasHoy)} citas ({sucursales.Sum(s => s.PorAtenderHoy)} por atender)");
            if (citas.Count == 0)
            {
                sb.AppendLine("Sin citas agendadas para hoy.");
            }
            else
            {
                const int maximo = 30; // Telegram admite ~4096 caracteres por mensaje
                foreach (var grupo in citas.Take(maximo).GroupBy(c => c.Sucursal))
                {
                    sb.AppendLine();
                    sb.AppendLine($"🏢 *{Limpiar(grupo.Key)}*");
                    foreach (var c in grupo)
                    {
                        var servicios = string.IsNullOrWhiteSpace(c.Servicios) ? "" : $" · {Limpiar(c.Servicios)}";
                        var estado = c.Estado is "Pendiente" or "Confirmada" ? "" : $" ({c.Estado})";
                        sb.AppendLine($"🕒 {c.FechaHora:HH:mm} {Limpiar(c.Cliente)} con {Limpiar(c.Barbero)}{servicios}{estado}");
                    }
                }
                if (citas.Count > maximo) sb.AppendLine($"\n…y {citas.Count - maximo} citas más. Revisa el panel.");
            }
            return sb.ToString();
        }

        // Quita los caracteres que Telegram interpreta como formato Markdown.
        private static string Limpiar(string t) => t.Replace("*", "").Replace("_", " ").Replace("`", "").Replace("[", "(").Replace("]", ")");

        // ============================== EJECUCIÓN DE SP ==============================

        public static SqlParameter[] ParametrosRango(FiltroAdmin f) => new[]
        {
            P("@SucursalID", f.SucursalId),
            new SqlParameter("@Desde", SqlDbType.Date) { Value = f.Desde.Date },
            new SqlParameter("@Hasta", SqlDbType.Date) { Value = f.Hasta.Date }
        };

        public static SqlParameter P(string nombre, object? valor) => new(nombre, valor ?? DBNull.Value);

        public async Task<List<T>> EjecutarSpAsync<T>(string sp, Func<DbDataReader, T> map, params SqlParameter[] parametros)
        {
            var lista = new List<T>();
            await ConConexionAsync(async conexion =>
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = sp;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddRange(parametros);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) lista.Add(map(reader));
            });
            return lista;
        }

        public Task EjecutarNonQueryAsync(string sp, params SqlParameter[] parametros) =>
            ConConexionAsync(async conexion =>
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = sp;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddRange(parametros);
                await cmd.ExecuteNonQueryAsync();
            });

        private async Task ConConexionAsync(Func<DbConnection, Task> accion)
        {
            var conexion = _context.Database.GetDbConnection();
            var abrir = conexion.State != ConnectionState.Open;
            if (abrir) await conexion.OpenAsync();
            try { await accion(conexion); }
            finally { if (abrir) await conexion.CloseAsync(); }
        }

        public static int Int(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i)); }
        public static decimal Dec(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i)); }
        public static decimal? DecNull(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i)); }
        public static bool Bool(DbDataReader r, string c) { var i = r.GetOrdinal(c); return !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i)); }
        public static string? Str(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetValue(i).ToString(); }
        public static long? Long(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i)); }
        public static TimeSpan? Hora(DbDataReader r, string c) { var i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : (TimeSpan)r.GetValue(i); }
    }
}
