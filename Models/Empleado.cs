using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Empleado
{
    public int EmpleadoId { get; set; }

    public int UsuarioId { get; set; }

    public int SucursalId { get; set; }

    public string? Especialidad { get; set; }

    public TimeOnly? HorarioInicio { get; set; }

    public TimeOnly? HorarioFin { get; set; }

    public bool? Estado { get; set; }

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();

    public virtual Sucursale Sucursal { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
