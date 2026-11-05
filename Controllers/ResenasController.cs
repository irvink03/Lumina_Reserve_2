using Lumina_Reserve_2.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;

namespace Lumina_Reserve_2.Controllers
{
    public class ResenasController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ResenasController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Resenas()
        {
            var resenas = await _context.Resenas.AsNoTracking()
                .Include(r => r.Usuario)
                .Include(r => r.Cita).ThenInclude(c => c.Empleado).ThenInclude(e => e!.Usuario)
                .OrderByDescending(r => r.Fecha)
                .ToListAsync();

            // En la BD cada reseña debe ligarse a una cita y a un usuario (CitaID y UsuarioID son NOT NULL),
            // así que solo un cliente con sesión puede calificar una cita suya ya completada.
            var citasPorCalificar = new List<Cita>();
            var usuarioId = ObtenerUsuarioId();
            if (usuarioId != null)
            {
                citasPorCalificar = await _context.Citas.AsNoTracking()
                    .Include(c => c.Empleado).ThenInclude(e => e!.Usuario)
                    .Include(c => c.Cortes)
                    .Where(c => c.UsuarioId == usuarioId
                             && c.Estado == "Completada"
                             && !c.Resenas.Any())
                    .OrderByDescending(c => c.FechaHora)
                    .ToListAsync();
            }

            ViewBag.CitasPorCalificar = citasPorCalificar;
            return View(resenas);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarResena(int citaId, int calificacion, string? comentario)
        {
            var usuarioId = ObtenerUsuarioId();
            if (usuarioId == null) return RedirectToAction("Login", "Cuenta");

            if (calificacion < 1 || calificacion > 5)
            {
                TempData["ErrorResena"] = "Selecciona una calificación entre 1 y 5 estrellas.";
                return RedirectToAction(nameof(Resenas));
            }

            var cita = await _context.Citas
                .FirstOrDefaultAsync(c => c.CitaId == citaId && c.UsuarioId == usuarioId);
            if (cita == null || cita.Estado != "Completada")
            {
                TempData["ErrorResena"] = "Solo puedes calificar citas tuyas que ya fueron completadas.";
                return RedirectToAction(nameof(Resenas));
            }

            if (await _context.Resenas.AnyAsync(r => r.CitaId == citaId))
            {
                TempData["ErrorResena"] = "Ya dejaste una reseña para esa cita.";
                return RedirectToAction(nameof(Resenas));
            }

            comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
            if (comentario?.Length > 500) comentario = comentario[..500];

            _context.Resenas.Add(new Resena
            {
                CitaId = cita.CitaId,
                UsuarioId = usuarioId.Value,
                Calificacion = calificacion,
                Comentario = comentario,
                Fecha = Reloj.Ahora
            });
            await _context.SaveChangesAsync();

            TempData["ExitoResena"] = "¡Gracias por tu opinión!";
            return RedirectToAction(nameof(Resenas));
        }

        private int? ObtenerUsuarioId() =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
