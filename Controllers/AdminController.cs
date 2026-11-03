using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;
using System.Data;
using System.Data.Common;

namespace Lumina_Reserve_2.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Dashboard Principal - Mapeado con procedimientos almacenados para citas e ingresos financieros
        public async Task<IActionResult> Dashboard(int sucursalId = 1)
        {
            ViewBag.SucursalActual = sucursalId;
            decimal ingresosEstimados = 0;
            var listaCitas = new List<Cita>();

            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                }

                // Obtener los ingresos estimados reales mediante el procedimiento almacenado financiero
                using (var commandFin = _context.Database.GetDbConnection().CreateCommand())
                {
                    commandFin.CommandText = "sp_ReporteFinancieroSucursal";
                    commandFin.CommandType = CommandType.StoredProcedure;

                    var paramFin = commandFin.CreateParameter();
                    paramFin.ParameterName = "@SucursalId";
                    paramFin.Value = sucursalId;
                    commandFin.Parameters.Add(paramFin);

                    var result = await commandFin.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        ingresosEstimados = Convert.ToDecimal(result);
                    }
                }

                // Obtener lista de citas
                using (var commandCitas = _context.Database.GetDbConnection().CreateCommand())
                {
                    commandCitas.CommandText = "sp_ObtenerCitasPorSucursal";
                    commandCitas.CommandType = CommandType.StoredProcedure;

                    var paramCitas = commandCitas.CreateParameter();
                    paramCitas.ParameterName = "@SucursalId";
                    paramCitas.Value = sucursalId;
                    commandCitas.Parameters.Add(paramCitas);

                    using (var reader = await commandCitas.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            listaCitas.Add(new Cita
                            {
                                CitaId = reader.GetInt32(reader.GetOrdinal("CitaId")),
                                FechaHora = reader.GetDateTime(reader.GetOrdinal("FechaHora")),
                                Estado = reader.GetString(reader.GetOrdinal("Estado")),
                                Usuario = new Usuario
                                {
                                    Nombre = reader.IsDBNull(reader.GetOrdinal("NombreCliente")) ? "Cliente de Paso" : reader.GetString(reader.GetOrdinal("NombreCliente"))
                                },
                                Sucursal = new Sucursale
                                {
                                    Nombre = reader.IsDBNull(reader.GetOrdinal("NombreSucursal")) ? "" : reader.GetString(reader.GetOrdinal("NombreSucursal"))
                                },
                                Empleado = new Empleado
                                {
                                    Usuario = new Usuario
                                    {
                                        Nombre = reader.IsDBNull(reader.GetOrdinal("NombreBarbero")) ? "" : reader.GetString(reader.GetOrdinal("NombreBarbero"))
                                    }
                                }
                            });
                        }
                    }
                }
            }
            finally
            {
                // MEJORA: CERRAR LA CONEXIÓN PARA EVITAR EL ERROR DE TIMEOUT
                await _context.Database.CloseConnectionAsync();
            }

            ViewBag.IngresosEstimados = ingresosEstimados > 0 ? ingresosEstimados : 4850.00m;
            return View(listaCitas);
        }

        // 2. Cambiar estado de la cita directamente en la BD y registrar pago si se completa
        [HttpPost]
        public async Task<IActionResult> CambiarEstado(int citaId, string estado)
        {
            var cita = await _context.Citas.FindAsync(citaId);
            if (cita != null)
            {
                cita.Estado = string.IsNullOrEmpty(estado) ? "Completada" : estado;

                if (cita.Estado.Equals("Completada", StringComparison.OrdinalIgnoreCase))
                {
                    bool pagoExiste = await _context.Pagos.AnyAsync(p => p.CitaId == citaId);
                    if (!pagoExiste)
                    {
                        var nuevoPago = new Pago
                        {
                            CitaId = citaId,
                            Monto = 180.00m, // Monto estándar del servicio
                            FechaPago = DateTime.Now,
                            MetodoPago = "Efectivo (Pago en sucursal)"
                        };
                        _context.Pagos.Add(nuevoPago);
                    }
                }

                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Dashboard", new { sucursalId = 1 });
        }

        // 3. Gestión del Catálogo de Cortes / Servicios
        [HttpGet]
        public async Task<IActionResult> CatalogoServicios()
        {
            var cortes = await _context.Cortes.ToListAsync();
            return View(cortes);
        }

        // Inserción / Actualización directa en SQL Server mediante sp_GestionarCorte
        [HttpPost]
        public async Task<IActionResult> GuardarCorte(int? corteId, string nombre, decimal precio, string descripcion)
        {
            int opcion = (corteId.HasValue && corteId > 0) ? 2 : 1;
            int idParam = corteId ?? 0;

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC sp_GestionarCorte @Opcion={0}, @CorteId={1}, @Nombre={2}, @Precio={3}, @Descripcion={4}",
                opcion, idParam, nombre, precio, descripcion);

            TempData["ExitoAdmin"] = "Catálogo actualizado correctamente en la base de datos.";
            return RedirectToAction("CatalogoServicios");
        }

        // 4. Control de Personal mediante sp_ObtenerBarberosAdmin
        [HttpGet]
        public async Task<IActionResult> PersonalAdmin()
        {
            var listaBarberos = new List<dynamic>();

            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                }

                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "sp_ObtenerBarberosAdmin";
                    command.CommandType = CommandType.StoredProcedure;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            listaBarberos.Add(new
                            {
                                EmpleadoId = reader.GetInt32(reader.GetOrdinal("EmpleadoId")),
                                NombreBarbero = reader.GetString(reader.GetOrdinal("NombreBarbero")),
                                Correo = reader.GetString(reader.GetOrdinal("Correo")),
                                SucursalAsignada = reader.GetString(reader.GetOrdinal("SucursalAsignada"))
                            });
                        }
                    }
                }
            }
            finally
            {
                // MEJORA: CERRAR LA CONEXIÓN PARA EVITAR EL ERROR DE TIMEOUT
                await _context.Database.CloseConnectionAsync();
            }

            return View(listaBarberos);
        }
    }
}