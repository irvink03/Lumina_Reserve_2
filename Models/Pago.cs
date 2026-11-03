using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Pago
{
    public int PagoId { get; set; }

    public int CitaId { get; set; }

    public decimal Monto { get; set; }

    public string MetodoPago { get; set; } = null!;

    public DateTime? FechaPago { get; set; }

    public string? EstadoPago { get; set; }

    public virtual Cita Cita { get; set; } = null!;
}
