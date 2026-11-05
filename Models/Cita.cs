using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models
{
    public partial class Cita
    {
        public int CitaId { get; set; }

        // Permitimos que sea anulable por si la cita la hace un visitante o un tercero sin cuenta obligatoria
        public int? UsuarioId { get; set; }

        public int EmpleadoId { get; set; }

        public int SucursalId { get; set; }

        public DateTime FechaHora { get; set; }

        public string? Estado { get; set; } = "Pendiente";

        public DateTime? FechaCreacion { get; set; } = Lumina_Reserve_2.Services.Reloj.Ahora;

        // Propiedad para el control de recordatorios por Telegram
        public bool RecordatorioEnviado { get; set; } = false;

        // Nuevos campos para clientes de paso o cuando se agenda para otra persona
        public string? NombreClienteExterno { get; set; }
        public string? TelefonoExterno { get; set; }

        // Propiedades de navegación
        public virtual Empleado? Empleado { get; set; }
        public virtual Sucursale? Sucursal { get; set; }
        public virtual Usuario? Usuario { get; set; }

        public virtual ICollection<Pago> Pagos { get; set; } = new List<Pago>();
        public virtual ICollection<Resena> Resenas { get; set; } = new List<Resena>();
        public virtual ICollection<Corte> Cortes { get; set; } = new List<Corte>();
    }
}