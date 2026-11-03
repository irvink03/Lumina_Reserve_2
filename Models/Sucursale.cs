using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Sucursale
{
    public int SucursalId { get; set; }

    public string Nombre { get; set; } = null!;

    public string Direccion { get; set; } = null!;

    public string? Telefono { get; set; }

    public bool? Estado { get; set; }

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();

    public virtual ICollection<Empleado> Empleados { get; set; } = new List<Empleado>();

    public virtual ICollection<Gasto> Gastos { get; set; } = new List<Gasto>();
}
