using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class Notificacione
{
    public int NotificacionId { get; set; }

    public int UsuarioId { get; set; }

    public string Mensaje { get; set; } = null!;

    public string? Tipo { get; set; }

    public DateTime? FechaEnvio { get; set; }

    public bool? Leido { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;
}
