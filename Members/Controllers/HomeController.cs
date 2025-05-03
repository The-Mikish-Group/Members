using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

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
    public IActionResult Directory()
    {
        ViewBag.Message = "Directory";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult Documents()
    {
        ViewBag.Message = "Documents";
        ViewData["Title"] = "Documents";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult Financials()
    {
        ViewBag.Message = "Financials";
        return View();
    }

    [Authorize(Roles = "Admin,Manager,Member")]
    public IActionResult Minutes()
    {
        ViewBag.Message = "Minutes";
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}