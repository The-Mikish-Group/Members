using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Members.Controllers
{
    public class LinksController(ApplicationDbContext context) : Controller
    {
        private readonly ApplicationDbContext _context = context;

        // GET: /Links/MoreLinks - Public view showing all categories and links in columns
        public async Task<IActionResult> MoreLinks()
        {
            try
            {
                var categories = await _context.LinkCategories
                                       .Where(c => !c.IsAdminOnly || (User.IsInRole("Admin") || User.IsInRole("Manager")))
                                       .Include(c => c.CategoryLinks)
                                       .OrderBy(c => c.SortOrder)
                                       .ThenBy(c => c.CategoryName)
                                       .ToListAsync();

                // Sort links within each category
                foreach (var category in categories)
                {
                    category.CategoryLinks = category.CategoryLinks.OrderBy(l => l.SortOrder).ThenBy(l => l.LinkName).ToList();
                }

                ViewData["Title"] = "More Links";
                return View(categories);
            }
            catch (Exception ex)
            {
                // If tables don't exist, show helpful message
                ViewData["Title"] = "More Links";
                ViewBag.DatabaseError = "The More Links system is not yet configured. Please run the database setup scripts first.";
                ViewBag.ErrorDetails = ex.Message;
                return View(new List<LinkCategory>());
            }
        }

        // GET: /Links/ManageCategories - Admin/Manager only
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ManageCategories()
        {
            try
            {
                var categories = await _context.LinkCategories
                                       .OrderBy(c => c.SortOrder)
                                       .ThenBy(c => c.CategoryName)
                                       .ToListAsync();

                ViewData["Title"] = "Manage Link Categories";
                return View(categories);
            }
            catch (Exception ex)
            {
                ViewData["Title"] = "Manage Link Categories";
                TempData["ErrorMessage"] = $"Database error: {ex.Message}";
                return View(new List<LinkCategory>());
            }
        }

        // GET: /Links/ManageLinks/{categoryId} - Admin/Manager only
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ManageLinks(int? categoryId)
        {
            var categories = await _context.LinkCategories
                                   .OrderBy(c => c.SortOrder)
                                   .ThenBy(c => c.CategoryName)
                                   .ToListAsync();
            
            ViewBag.LinkCategories = categories;
            ViewBag.SelectedCategoryId = categoryId;
            ViewData["Title"] = "Manage Category Links";
            
            List<CategoryLink> links = [];
            if (categoryId.HasValue)
            {
                var selectedCategory = await _context.LinkCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId.Value);
                if (selectedCategory == null)
                {
                    return NotFound($"Category with ID {categoryId} not found.");
                }
                
                ViewBag.SelectedCategoryName = selectedCategory.CategoryName;
                
                links = await _context.CategoryLinks
                              .Where(f => f.CategoryID == categoryId.Value)
                              .OrderBy(f => f.SortOrder)
                              .ThenBy(f => f.LinkName)
                              .ToListAsync();
            }
            
            return View(links);
        }

        // POST: /Links/CreateCategory - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCategory(string categoryName, bool isAdminOnly = false)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(ManageCategories));
            }

            // Check if category already exists
            var existingCategory = await _context.LinkCategories
                                         .FirstOrDefaultAsync(c => c.CategoryName == categoryName.Trim());
            
            if (existingCategory != null)
            {
                TempData["ErrorMessage"] = $"A category named '{categoryName}' already exists.";
                return RedirectToAction(nameof(ManageCategories));
            }

            // Get next sort order (add to end)
            var maxSortOrder = await _context.LinkCategories
                                     .MaxAsync(c => (int?)c.SortOrder) ?? 0;

            var category = new LinkCategory
            {
                CategoryName = categoryName.Trim(),
                SortOrder = maxSortOrder + 1,
                IsAdminOnly = isAdminOnly
            };

            _context.LinkCategories.Add(category);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Category '{categoryName}' created successfully.";
            return RedirectToAction(nameof(ManageCategories));
        }

        // POST: /Links/CreateLink - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLink(int categoryId, string linkName, string linkUrl)
        {
            if (string.IsNullOrWhiteSpace(linkName) || string.IsNullOrWhiteSpace(linkUrl))
            {
                TempData["ErrorMessage"] = "Both link name and URL are required.";
                return RedirectToAction(nameof(ManageLinks), new { categoryId });
            }

            // Validate URL format
            if (!Uri.TryCreate(linkUrl, UriKind.Absolute, out _))
            {
                TempData["ErrorMessage"] = "Please enter a valid URL.";
                return RedirectToAction(nameof(ManageLinks), new { categoryId });
            }

            // Verify category exists
            var category = await _context.LinkCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Selected category not found.";
                return RedirectToAction(nameof(ManageCategories));
            }

            // Get next sort order for this category
            var maxSortOrder = await _context.CategoryLinks
                                     .Where(l => l.CategoryID == categoryId)
                                     .MaxAsync(l => (int?)l.SortOrder) ?? 0;

            var link = new CategoryLink
            {
                CategoryID = categoryId,
                LinkName = linkName.Trim(),
                LinkUrl = linkUrl.Trim(),
                SortOrder = maxSortOrder + 1
            };

            _context.CategoryLinks.Add(link);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Link '{linkName}' added successfully.";
            return RedirectToAction(nameof(ManageLinks), new { categoryId });
        }

        // POST: /Links/UpdateCategorySortOrder - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCategorySortOrder(int categoryId, string direction)
        {
            var category = await _context.LinkCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId);
            if (category == null)
            {
                return NotFound();
            }

            var allCategories = await _context.LinkCategories
                                      .OrderBy(c => c.SortOrder)
                                      .ToListAsync();

            var currentIndex = allCategories.FindIndex(c => c.CategoryID == categoryId);
            
            if (direction == "up" && currentIndex > 0)
            {
                // Swap with previous category
                var temp = allCategories[currentIndex - 1].SortOrder;
                allCategories[currentIndex - 1].SortOrder = category.SortOrder;
                category.SortOrder = temp;
            }
            else if (direction == "down" && currentIndex < allCategories.Count - 1)
            {
                // Swap with next category
                var temp = allCategories[currentIndex + 1].SortOrder;
                allCategories[currentIndex + 1].SortOrder = category.SortOrder;
                category.SortOrder = temp;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageCategories));
        }

        // POST: /Links/UpdateLinkSortOrder - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLinkSortOrder(int linkId, string direction)
        {
            var link = await _context.CategoryLinks.FirstOrDefaultAsync(l => l.LinkID == linkId);
            if (link == null)
            {
                return NotFound();
            }

            var categoryLinks = await _context.CategoryLinks
                                      .Where(l => l.CategoryID == link.CategoryID)
                                      .OrderBy(l => l.SortOrder)
                                      .ToListAsync();

            var currentIndex = categoryLinks.FindIndex(l => l.LinkID == linkId);
            
            if (direction == "up" && currentIndex > 0)
            {
                // Swap with previous link
                var temp = categoryLinks[currentIndex - 1].SortOrder;
                categoryLinks[currentIndex - 1].SortOrder = link.SortOrder;
                link.SortOrder = temp;
            }
            else if (direction == "down" && currentIndex < categoryLinks.Count - 1)
            {
                // Swap with next link
                var temp = categoryLinks[currentIndex + 1].SortOrder;
                categoryLinks[currentIndex + 1].SortOrder = link.SortOrder;
                link.SortOrder = temp;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageLinks), new { categoryId = link.CategoryID });
        }

        // POST: /Links/DeleteCategory - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCategory(int categoryId)
        {
            var category = await _context.LinkCategories
                                 .Include(c => c.CategoryLinks)
                                 .FirstOrDefaultAsync(c => c.CategoryID == categoryId);
            
            if (category == null)
            {
                return NotFound();
            }

            _context.LinkCategories.Remove(category); // CASCADE will remove links
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Category '{category.CategoryName}' and all its links deleted successfully.";
            return RedirectToAction(nameof(ManageCategories));
        }

        // POST: /Links/DeleteLink - Admin/Manager only
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLink(int linkId)
        {
            var link = await _context.CategoryLinks.FirstOrDefaultAsync(l => l.LinkID == linkId);
            
            if (link == null)
            {
                return NotFound();
            }

            var categoryId = link.CategoryID;
            _context.CategoryLinks.Remove(link);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Link '{link.LinkName}' deleted successfully.";
            return RedirectToAction(nameof(ManageLinks), new { categoryId });
        }

        // POST: /Links/UpdateCategoriesSortOrder - Batch update for drag-and-drop
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCategoriesSortOrder(int[] categoryIds, int[] sortOrders)
        {
            try
            {
                if (categoryIds == null || sortOrders == null || categoryIds.Length != sortOrders.Length)
                {
                    return Json(new { success = false, message = "Invalid data provided" });
                }

                for (int i = 0; i < categoryIds.Length; i++)
                {
                    var category = await _context.LinkCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryIds[i]);
                    if (category != null)
                    {
                        category.SortOrder = sortOrders[i];
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Categories reordered successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error updating sort order: " + ex.Message });
            }
        }

        // POST: /Links/UpdateLinksSortOrder - Batch update for drag-and-drop  
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLinksSortOrder(int[] linkIds, int[] sortOrders)
        {
            try
            {
                if (linkIds == null || sortOrders == null || linkIds.Length != sortOrders.Length)
                {
                    return Json(new { success = false, message = "Invalid data provided" });
                }

                for (int i = 0; i < linkIds.Length; i++)
                {
                    var link = await _context.CategoryLinks.FirstOrDefaultAsync(l => l.LinkID == linkIds[i]);
                    if (link != null)
                    {
                        link.SortOrder = sortOrders[i];
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Links reordered successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error updating sort order: " + ex.Message });
            }
        }

    }
}