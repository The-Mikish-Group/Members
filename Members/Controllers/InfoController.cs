using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Members.Models;
using System;
using System.Threading.Tasks; // Add this using statement
using Members.Services; // Add this using statement

namespace Members.Controllers
{
    public class InfoController : Controller
    {
        private readonly EmailService _emailService; // Add this field

        public InfoController(EmailService emailService) // Add constructor
        {
            _emailService = emailService;
        }

        public IActionResult Index()
        {
            ViewBag.Message = "Home";
            ViewData["ViewName"] = "Oaks-Village";
            return View();
        }

        public IActionResult About()
        {
            ViewBag.Message = "About Us";
            ViewData["ViewName"] = "About Us";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendEmail(string Name, string Email, string Subject, string Message, string Comment)
        {
            if (!string.IsNullOrEmpty(Comment))
            {
                // Likely a bot, ignore.
                return View("Index");
            }

            try
            {
                // Use EmailService to send the email
                await _emailService.SendEmailAsync(
                    "OaksVillage@oaks-village.com", // To address
                    $"Contact Form: {Subject}", // Subject
                    $"Name: {Name}\nEmail: {Email}\nMessage: {Message}\nReply to: {Email}" // Body
                );

                ViewBag.Message = "Your email has been sent successfully!";
                return View("Index");
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"There was an error sending your message: {ex.Message}";
                return View("Index");
            }
        }

        public IActionResult Contact()
        {
            ViewBag.Message = "Contact Us";
            ViewData["ViewName"] = ViewBag.Message;
            return View();
        }

        public IActionResult TOS()
        {
            ViewBag.Message = "TOS";
            ViewData["ViewName"] = ViewBag.Message;
            return View();
        }

        public IActionResult Privacy()
        {
            ViewBag.Message = "Privacy";
            ViewData["ViewName"] = ViewBag.Message;
            return View();
        }

        public IActionResult Facilities()
        {
            ViewBag.Message = "Facilities";
            ViewData["ViewName"] = ViewBag.Message;
            return View();
        }

        public IActionResult MoreLinks()
        {
            ViewBag.Message = "More Links";
            ViewData["ViewName"] = ViewBag.Message;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}