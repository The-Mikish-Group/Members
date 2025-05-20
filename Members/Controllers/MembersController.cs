using Members.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Members.Controllers
{
    // Authorize for Members, Managers, and Admins
    [Authorize(Roles = "Member,Manager,Admin")]
    public class MembersController(ILogger<MembersController> logger, ApplicationDbContext context) : Controller // No need for IWebHostEnvironment here unless file paths are used
    {
        private readonly ILogger<MembersController> _logger = logger;
        private readonly ApplicationDbContext _context = context;

        // Action for the first view: List Categories available to members
        // This will list all categories that have at least one file
        [HttpGet] // Use HttpGet for displaying a view
        public IActionResult ListCategories()
        {
            _logger.LogInformation("Loading list of categories for members.");

            // Fetch PDFCategories that have at least one CategoryFile
            var nonEmptyCategories = _context.PDFCategories
                                        .Where(c => c.CategoryFiles.Any()) // Filter to include only categories with files
                                        .OrderBy(c => c.SortOrder) // Order categories
                                        .ThenBy(c => c.CategoryName)
                                        .ToList();

            ViewData["Title"] = "Available PDF Categories"; // Set a title for the view

            // Pass the list of categories to the ListCategories view (in Views/Members folder)
            return View("ListCategories", nonEmptyCategories);
        }

        // Action for the second view: List Files for a specific Category
        // It takes the categoryId from the first view
        [HttpGet] // Use HttpGet for displaying a view
        public IActionResult ListFiles(int categoryId) // Parameter name matches what will be passed
        {
            _logger.LogInformation("Loading list of files for category ID: {CategoryId}", categoryId);

            // Find the category to display its name in the view
            var category = _context.PDFCategories.Find(categoryId);

            // If the category doesn't exist, return NotFound or redirect
            if (category == null)
            {
                _logger.LogWarning("Attempted to access non-existent category ID: {CategoryId}", categoryId);
                // Optionally redirect back to the ListCategories page with an error message
                // TempData["ErrorMessage"] = "Category not found.";
                // return RedirectToAction(nameof(ListCategories));
                return NotFound($"Category with ID {categoryId} not found.");
            }

            // Fetch the files for the selected category
            // Include PDFCategory if you need category details in the view (e.g., category name in heading)
            var files = _context.CategoryFiles
                                .Where(f => f.CategoryID == categoryId)
                                .Include(f => f.PDFCategory) // Include the related category data (useful for accessing category name in view)
                                .OrderBy(f => f.SortOrder) // Order files within the category
                                .ThenBy(f => f.FileName)
                                .ToList();

            ViewData["Title"] = $"Files in {category.CategoryName}"; // Set a title for the view
            ViewBag.CategoryName = category.CategoryName; // Pass the category name to the view
            ViewBag.CategoryId = categoryId; // Pass the category ID back, useful for breadcrumbs or other links

            // Pass the list of files to the ListFiles view (in Views/Members folder)
            return View("ListFiles", files);
        }

        // You might add other member-facing actions here later
    }
}