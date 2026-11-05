namespace Lumina_Reserve_2.Models
{
    // sp_ObtenerBarberosAdmin
    public class BarberoAdminViewModel
    {
        public int EmpleadoId { get; set; }
        public string NombreBarbero { get; set; } = "";
        public string Correo { get; set; } = "";
        public string SucursalAsignada { get; set; } = "";
        public string? Especialidad { get; set; }
        public TimeSpan? HorarioInicio { get; set; }
        public TimeSpan? HorarioFin { get; set; }
        public bool Activo { get; set; } = true;
        public int CitasProximas { get; set; }
        public int CitasCompletadas { get; set; }
        public int SucursalId { get; set; }
        public string? Telefono { get; set; }
    }
}
