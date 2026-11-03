using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;

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
            var cortes = await _context.Cortes.ToListAsync();
            return View(cortes);
        }
    }
}