using Microsoft.AspNetCore.Mvc;

namespace LuminaReserve.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}