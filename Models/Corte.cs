using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Corte
{
    public int CorteId { get; set; }

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    public decimal Precio { get; set; }

    public int DuracionMinutos { get; set; }

    public bool? Estado { get; set; }

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();
}
