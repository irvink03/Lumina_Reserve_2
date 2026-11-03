using System;
using System.Collections.Generic;

namespace Lumina_Reserve_2.Models;

public partial class CodigosVerificacionBot
{
    public int CodigoId { get; set; }

    public int UsuarioId { get; set; }

    public string Codigo { get; set; } = null!;

    public DateTime FechaExpiracion { get; set; }

    public bool? Usado { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;
}
