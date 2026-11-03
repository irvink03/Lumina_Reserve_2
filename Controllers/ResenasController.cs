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
            ViewBag.Empleados = await _context.Empleados.Include(e => e.Usuario).ToListAsync();
            var resenas = await _context.Resenas
                .Include(r => r.Usuario)
                .Include(r => r.Cita)
                .OrderByDescending(r => r.Fecha)
                .ToListAsync();

            return View(resenas);
        }

        [HttpPost]
        public async Task<IActionResult> GuardarResena(string nombreVisitante, int empleadoId, int calificacion, string comentario)
        {
            var nuevaResena = new Resena
            {
                Calificacion = calificacion,
                Comentario = comentario,
                Fecha = DateTime.Now
            };

            _context.Resenas.Add(nuevaResena);
            await _context.SaveChangesAsync();

            TempData["ExitoResena"] = "¡Gracias por tu opinión!";
            return RedirectToAction("Resenas");
        }
    }
}