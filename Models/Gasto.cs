using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Gasto
{
    public int GastoId { get; set; }

    public int SucursalId { get; set; }

    public string Concepto { get; set; } = null!;

    public decimal Monto { get; set; }

    public DateTime? Fecha { get; set; }

    public virtual Sucursale Sucursal { get; set; } = null!;
}
