using Lumina_Reserve_2.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lumina_Reserve_2.Controllers
{
    public class PortafolioController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PortafolioController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Portafolio()
        {
            var cortes = await _context.Cortes
                .AsNoTracking()
                .Where(c => c.Estado != false)
                .OrderBy(c => c.CorteId)
                .ToListAsync();
            return View(cortes);
        }
    }
}