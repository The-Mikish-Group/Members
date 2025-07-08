using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; 
using System.Linq; 
using System.Threading.Tasks; 

namespace Members.Controllers
{
    [Authorize(Roles = "Admin,Manager,Member")] 
    public class MembersController(ILogger<MembersController> logger, ApplicationDbContext context) : Controller 
    {
        private readonly ILogger<MembersController> _logger = logger; 
        private readonly ApplicationDbContext _context = context;

        [HttpGet]
        public async Task<IActionResult> ListCategories() 
        {
            _logger.LogInformation("Loading list of Members categories.");

            var membersCategories = await _context.PDFCategories
                                        .Where(c => c.IsAdminOnly != true) 
                                        .OrderBy(c => c.SortOrder)
                                        .ThenBy(c => c.CategoryName)
                                        .ToListAsync(); 

            ViewData["Title"] = "Members PDF Categories"; 
            return View("~/Views/Members/ListCategories.cshtml", membersCategories); 
        }

        [HttpGet]
        public async Task<IActionResult> ListFiles(int categoryId) 
        {
            _logger.LogInformation("Loading list of files for members category ID: {CategoryId}.", categoryId);

            var category = await _context.PDFCategories
                                 .FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == false); 

            if (category == null)
            {
                _logger.LogWarning("Attempted to access non-existent category ID: {CategoryId}", categoryId);
                TempData["ErrorMessage"] = "The selected category is not a members category or does not exist.";
                return RedirectToAction(nameof(ListCategories));
            }

            var files = await _context.CategoryFiles
                                .Where(f => f.CategoryID == categoryId)
                                .OrderBy(f => f.SortOrder)
                                .ThenBy(f => f.FileName)
                                .ToListAsync(); 

            ViewData["Title"] = $"Members Files in {category.CategoryName}"; 
            ViewBag.CategoryName = category.CategoryName; 
            ViewBag.CategoryId = categoryId; 

            return View("~/Views/Members/ListFiles.cshtml", files);
        }
    }
}
