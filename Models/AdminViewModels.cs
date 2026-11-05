namespace Lumina_Reserve_2.Models
{
    // Filtro compartido por las pantallas del panel (sucursal + periodo).
    public class FiltroAdmin
    {
        public int SucursalId { get; set; }
        public string SucursalNombre { get; set; } = "";
        public List<Sucursale> Sucursales { get; set; } = new();
        public string Periodo { get; set; } = "mes";
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }

        public static readonly (string Clave, string Texto)[] Periodos =
        {
            ("hoy", "Hoy"), ("semana", "Esta semana"), ("mes", "Este mes"), ("30d", "Últimos 30 días")
        };

        public string Etiqueta => Desde.Date == Hasta.Date
            ? Desde.ToString("dd/MM/yyyy")
            : $"{Desde:dd/MM/yyyy} – {Hasta:dd/MM/yyyy}";
    }

    // sp_DashboardResumen
    public class ResumenDashboard
    {
        public int CitasTotal { get; set; }
        public int Pendientes { get; set; }
        public int Confirmadas { get; set; }
        public int Completadas { get; set; }
        public int Canceladas { get; set; }
        public decimal Ingresos { get; set; }
        public decimal Gastos { get; set; }
        public decimal Utilidad { get; set; }
        public decimal TicketPromedio { get; set; }
        public int ClientesUnicos { get; set; }
        public decimal? CalificacionPromedio { get; set; }
        public int TotalResenas { get; set; }
    }

    // sp_AgendaAdmin
    public class CitaAgenda
    {
        public int CitaId { get; set; }
        public DateTime FechaHora { get; set; }
        public string Estado { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string? Telefono { get; set; }
        public bool TieneCuenta { get; set; }
        public string Barbero { get; set; } = "";
        public string Servicios { get; set; } = "";
        public decimal Total { get; set; }
        public int Minutos { get; set; }
        public string? MetodoPago { get; set; }
        public string? EstadoPago { get; set; }
        public bool Activa => Estado is "Pendiente" or "Confirmada";
    }

    // sp_RendimientoBarberos
    public class RendimientoBarbero
    {
        public int EmpleadoId { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
        public int Citas { get; set; }
        public int Completadas { get; set; }
        public int Canceladas { get; set; }
        public decimal Ingresos { get; set; }
        public decimal? Calificacion { get; set; }
        public int Resenas { get; set; }
    }

    // sp_ServiciosMasVendidos
    public class ServicioVendido
    {
        public string Nombre { get; set; } = "";
        public int Veces { get; set; }
        public decimal Monto { get; set; }
    }

    // sp_IngresosPorDia (los días sin ingresos se rellenan con 0 en el controlador)
    public class IngresoDia
    {
        public DateTime Dia { get; set; }
        public decimal Ingresos { get; set; }
        public int Citas { get; set; }
    }

    public class DashboardViewModel
    {
        public FiltroAdmin Filtro { get; set; } = new();
        public string? EstadoFiltro { get; set; }
        public ResumenDashboard Resumen { get; set; } = new();
        public List<CitaAgenda> Agenda { get; set; } = new();
        public List<RendimientoBarbero> Barberos { get; set; } = new();
        public List<ServicioVendido> TopServicios { get; set; } = new();
        public List<IngresoDia> IngresosPorDia { get; set; } = new();
        public bool GraficaDisponible { get; set; }
    }

    // sp_ObtenerGastos
    public class GastoItem
    {
        public int GastoId { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
    }

    public class GastosViewModel
    {
        public FiltroAdmin Filtro { get; set; } = new();
        public List<GastoItem> Gastos { get; set; } = new();
        public ResumenDashboard Resumen { get; set; } = new();
    }

    // Datos completos para exportar el reporte (Excel / PDF).
    public class ReporteAdmin
    {
        public FiltroAdmin Filtro { get; set; } = new();
        public ResumenDashboard Resumen { get; set; } = new();
        public List<CitaAgenda> Agenda { get; set; } = new();
        public List<RendimientoBarbero> Barberos { get; set; } = new();
        public List<ServicioVendido> TopServicios { get; set; } = new();
        public List<IngresoDia> IngresosPorDia { get; set; } = new();
        public List<GastoItem> Gastos { get; set; } = new();
        public DateTime Generado { get; set; }
    }

    public class PersonalViewModel
    {
        public List<BarberoAdminViewModel> Barberos { get; set; } = new();
        public List<Sucursale> Sucursales { get; set; } = new();
    }
}
