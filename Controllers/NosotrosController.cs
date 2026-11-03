using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;

namespace Lumina_Reserve_2.Controllers
{
    public class NosotrosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public NosotrosController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> NuestroEquipo()
        {
            var empleados = await _context.Empleados
                .Include(e => e.Usuario)
                .Include(e => e.Sucursal)
                .ToListAsync();

            return View(empleados);
        }

        public async Task<IActionResult> Ubicacion()
        {
            var sucursales = await _context.Sucursales.ToListAsync();
            return View(sucursales);
        }
    }
}