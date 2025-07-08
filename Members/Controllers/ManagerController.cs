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
    [Authorize(Roles = "Admin,Manager")] 
    public class ManagerController(ILogger<ManagerController> logger, ApplicationDbContext context) : Controller 
    {
        private readonly ILogger<ManagerController> _logger = logger; 
        private readonly ApplicationDbContext _context = context;

        [HttpGet]
        public async Task<IActionResult> ManagerListCategories() 
        {
            _logger.LogInformation("Loading list of confidential categories for Admin/Manager.");
            var confidentialCategories = await _context.PDFCategories
                .Where(c => c.IsAdminOnly == true)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CategoryName)
                .ToListAsync();             

            ViewData["Title"] = "Confidential PDF Categories"; 
            return View("~/Views/Manager/ManagerListCategories.cshtml", confidentialCategories); 
        }

        [HttpGet]
        public async Task<IActionResult> ManagerListFiles(int categoryId) 
        {
            _logger.LogInformation("Loading list of files for confidential category ID: {CategoryId} for Admin/Manager.", categoryId);

            var category = await _context.PDFCategories
                                 .FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == true); 

            if (category == null)
            {
                _logger.LogWarning("Admin/Manager attempted to access non-existent or non-confidential category ID: {CategoryId}", categoryId);
                TempData["ErrorMessage"] = "The selected category is not a confidential category or does not exist.";
                return RedirectToAction(nameof(ManagerListCategories));
            }

            var files = await _context.CategoryFiles
                                .Where(f => f.CategoryID == categoryId)
                                .OrderBy(f => f.SortOrder)
                                .ThenBy(f => f.FileName)
                                .ToListAsync(); 

            ViewData["Title"] = $"Confidential Files in {category.CategoryName}"; 
            ViewBag.CategoryName = category.CategoryName; 
            ViewBag.CategoryId = categoryId; 

            return View("~/Views/Manager/ManagerListFiles.cshtml", files);
        }
    }
}
