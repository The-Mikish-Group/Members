using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Members.Models;
using Microsoft.AspNetCore.Authorization;

namespace Members.Controllers;

public class HomeController(ILogger<HomeController> logger) : Controller
{
    private readonly ILogger<HomeController> _logger = logger;

    public IActionResult Index()
    {
        ViewBag.Message = "Home";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult Notices()
    {
        ViewBag.Message = "Notices";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult MemberDocuments()
    {

        ViewBag.Message = "Member Documents";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult MemberDocumentsStatic()
    {

        ViewBag.Message = "Member Documents Static";
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
