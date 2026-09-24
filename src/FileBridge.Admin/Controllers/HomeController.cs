using Microsoft.AspNetCore.Mvc;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

public class HomeController : Controller
{
    private readonly FileBridgeDbContext _db;

    public HomeController(FileBridgeDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["ActiveNav"] = "dashboard";
        return View();
    }
}
