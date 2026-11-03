using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Resena
{
    public int ResenaId { get; set; }

    public int CitaId { get; set; }

    public int UsuarioId { get; set; }

    public int? Calificacion { get; set; }

    public string? Comentario { get; set; }

    public DateTime? Fecha { get; set; }

    public virtual Cita Cita { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
