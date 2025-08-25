# More Links Module - Complete Deployment Package

## 📋 Overview
This is a complete "More Links" management system for ASP.NET Core MVC applications with categorized external links, admin/manager control, and responsive public display.

## ✅ Pre-Requirements Check
- ASP.NET Core MVC project with Entity Framework Core
- ASP.NET Core Identity with role-based authorization
- Bootstrap 5.x for UI components
- Roles: "Admin" and "Manager" must exist in the system

## 🗃️ Database Schema

### Step 1: Execute Database Creation Script
```sql
-- Create LinkCategories table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LinkCategories' AND xtype='U')
CREATE TABLE LinkCategories (
    CategoryID int IDENTITY(1,1) PRIMARY KEY,
    CategoryName nvarchar(255) NOT NULL,
    SortOrder int NOT NULL,
    IsAdminOnly bit NOT NULL DEFAULT 0
);

-- Create CategoryLinks table  
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CategoryLinks' AND xtype='U')
CREATE TABLE CategoryLinks (
    LinkID int IDENTITY(1,1) PRIMARY KEY,
    CategoryID int NOT NULL,
    LinkName nvarchar(255) NOT NULL,
    LinkUrl nvarchar(500) NOT NULL,
    SortOrder int NOT NULL,
    FOREIGN KEY (CategoryID) REFERENCES LinkCategories(CategoryID) ON DELETE CASCADE
);

-- Add some initial test data only if tables are empty
IF NOT EXISTS (SELECT * FROM LinkCategories)
BEGIN
    INSERT INTO LinkCategories (CategoryName, SortOrder, IsAdminOnly) VALUES 
    ('Helpful Resources', 1, 0),
    ('Government Links', 2, 0),
    ('Community Services', 3, 0);

    INSERT INTO CategoryLinks (CategoryID, LinkName, LinkUrl, SortOrder) VALUES 
    (1, 'Google', 'https://www.google.com', 1),
    (1, 'Weather.com', 'https://www.weather.com', 2),
    (2, 'IRS', 'https://www.irs.gov', 1),
    (2, 'Social Security', 'https://www.ssa.gov', 2),
    (3, 'Local Library', 'https://example.com/library', 1);
END
```

## 📁 Required Files to Create

### Model Files

#### `Models/LinkCategory.cs`
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class LinkCategory
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        public required string CategoryName { get; set; }

        [Required]
        public int SortOrder { get; set; }

        public bool IsAdminOnly { get; set; } = false;

        public virtual ICollection<CategoryLink> CategoryLinks { get; set; } = [];
    }
}
```

#### `Models/CategoryLink.cs`
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class CategoryLink
    {
        [Key]
        public int LinkID { get; set; }

        [ForeignKey("LinkCategory")]
        public int CategoryID { get; set; }

        [Required]
        public required string LinkName { get; set; }

        [Required]
        [Url]
        public required string LinkUrl { get; set; }

        public int SortOrder { get; set; }

        public virtual LinkCategory? LinkCategory { get; set; }
    }
}
```

### Controller File

#### `Controllers/LinksController.cs`
```csharp
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
            // Ensure database tables exist first (will execute only once)
            await EnsureDatabaseTablesExist();

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

        // GET: /Links/ManageCategories - Admin/Manager only
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ManageCategories()
        {
            var categories = await _context.LinkCategories
                                   .OrderBy(c => c.SortOrder)
                                   .ThenBy(c => c.CategoryName)
                                   .ToListAsync();

            ViewData["Title"] = "Manage Link Categories";
            return View(categories);
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

        // Helper method to ensure database tables exist
        private async Task EnsureDatabaseTablesExist()
        {
            try
            {
                // Check if LinkCategories table exists by trying to count records
                await _context.LinkCategories.CountAsync();
            }
            catch (Exception)
            {
                // Tables don't exist, create them
                string sql = @"
-- Create LinkCategories table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LinkCategories' AND xtype='U')
CREATE TABLE LinkCategories (
    CategoryID int IDENTITY(1,1) PRIMARY KEY,
    CategoryName nvarchar(255) NOT NULL,
    SortOrder int NOT NULL,
    IsAdminOnly bit NOT NULL DEFAULT 0
);

-- Create CategoryLinks table  
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CategoryLinks' AND xtype='U')
CREATE TABLE CategoryLinks (
    LinkID int IDENTITY(1,1) PRIMARY KEY,
    CategoryID int NOT NULL,
    LinkName nvarchar(255) NOT NULL,
    LinkUrl nvarchar(500) NOT NULL,
    SortOrder int NOT NULL,
    FOREIGN KEY (CategoryID) REFERENCES LinkCategories(CategoryID) ON DELETE CASCADE
);

-- Add some initial test data only if tables are empty
IF NOT EXISTS (SELECT * FROM LinkCategories)
BEGIN
    INSERT INTO LinkCategories (CategoryName, SortOrder, IsAdminOnly) VALUES 
    ('Helpful Resources', 1, 0),
    ('Government Links', 2, 0),
    ('Community Services', 3, 0);

    INSERT INTO CategoryLinks (CategoryID, LinkName, LinkUrl, SortOrder) VALUES 
    (1, 'Google', 'https://www.google.com', 1),
    (1, 'Weather.com', 'https://www.weather.com', 2),
    (2, 'IRS', 'https://www.irs.gov', 1),
    (2, 'Social Security', 'https://www.ssa.gov', 2),
    (3, 'Local Library', 'https://example.com/library', 1);
END
";

                await _context.Database.ExecuteSqlRawAsync(sql);
            }
        }
    }
}
```

### View Files

#### `Views/Links/MoreLinks.cshtml`
```html
@model List<Members.Models.LinkCategory>
@{
    ViewData["Title"] = "More Links";
}

<div class="container-fluid mt-4">
    <div class="row">
        <div class="col-12">
            <!-- Page Header -->
            <div class="mb-4">
                <!-- Title Row -->
                <h2>🔗 More Links</h2>
                
                <!-- Description Row -->
                <p>Useful links organized by category</p>
                
                <!-- Admin/Manager Button Row -->
                @if (User.IsInRole("Admin") || User.IsInRole("Manager"))
                {
                    <div class="mt-3">
                        <a href="@Url.Action("ManageCategories", "Links")" class="btn btn-primary btn-sm">
                            <i class="fas fa-cog"></i> Manage Categories
                        </a>
                        <a href="@Url.Action("ManageLinks", "Links")" class="btn btn-secondary btn-sm ms-2">
                            <i class="fas fa-link"></i> Manage Links
                        </a>
                    </div>
                }
            </div>

            @if (TempData["SuccessMessage"] != null)
            {
                <div class="alert alert-success alert-dismissible fade show" role="alert">
                    @TempData["SuccessMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            @if (TempData["ErrorMessage"] != null)
            {
                <div class="alert alert-danger alert-dismissible fade show" role="alert">
                    @TempData["ErrorMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            @if (Model.Any())
            {
                <!-- Links Display in Columns -->
                <div class="row">
                    @foreach (var category in Model)
                    {
                        <div class="col-lg-4 col-md-6 col-sm-12 mb-4">
                            <div class="card h-100 fish-smart-card">
                                <div class="card-header fish-smart-card-header">
                                    <h5 class="mb-0">
                                        <i class="fas fa-folder"></i> @category.CategoryName
                                        @if (category.IsAdminOnly)
                                        {
                                            <small class="badge bg-warning text-dark ms-2">Admin Only</small>
                                        }
                                    </h5>
                                </div>
                                <div class="card-body fish-smart-card-body">
                                    @if (category.CategoryLinks.Any())
                                    {
                                        <ul class="list-unstyled mb-0">
                                            @foreach (var link in category.CategoryLinks)
                                            {
                                                <li class="mb-2">
                                                    <a href="@link.LinkUrl" 
                                                       target="_blank" 
                                                       rel="noopener noreferrer" 
                                                       class="link-item d-flex align-items-center text-decoration-none">
                                                        <i class="bi bi-eye text-primary me-2"></i>
                                                        <span>@link.LinkName</span>
                                                    </a>
                                                </li>
                                            }
                                        </ul>
                                    }
                                    else
                                    {
                                        <p class="mb-0">
                                            <i class="fas fa-info-circle"></i> No links in this category yet.
                                        </p>
                                    }
                                </div>
                            </div>
                        </div>
                    }
                </div>
            }
            else
            {
                <div class="alert alert-info">
                    <h4><i class="fas fa-info-circle"></i> No Link Categories Found</h4>
                    <p class="mb-0">
                        No link categories have been created yet.
                        @if (User.IsInRole("Admin") || User.IsInRole("Manager"))
                        {
                            <text>Use the "Manage Categories" button above to create your first category.</text>
                        }
                        else
                        {
                            <text>Please contact an administrator to set up link categories.</text>
                        }
                    </p>
                </div>
            }
        </div>
    </div>
</div>

<style>
/* Link styling */
.link-item {
    padding: 8px 12px;
    border-radius: 6px;
    transition: all 0.2s ease;
    color: #495057 !important;
    border: 1px solid transparent;
}

.link-item:hover {
    background-color: #f8f9fa;
    border-color: #dee2e6;
    color: #0d6efd !important;
    text-decoration: none;
    transform: translateX(2px);
}

.link-item:hover .bi-eye {
    color: #0d6efd !important;
}

/* Fish-Smart Card styling */
.fish-smart-card {
    border: none;
    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    transition: transform 0.2s ease, box-shadow 0.2s ease;
    background-color: var(--card-bg, #DCEDFF) !important;
}

.fish-smart-card:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0,0,0,0.15);
}

.fish-smart-card-header {
    background-color: var(--card-header-bg, #A3CBF6) !important;
    border-bottom: none;
    font-weight: 500;
    color: #424143 !important; /* Use dark gray/black for good contrast against light blue background */
}

.fish-smart-card-body {
    background-color: var(--card-body-bg, #F8F9FA) !important;
    color: #424143 !important; /* Use dark gray/black for good contrast against light background */
}

/* Responsive adjustments */
@media (max-width: 768px) {
    .link-item {
        padding: 10px 8px;
        font-size: 0.95em;
    }
    
    .card-header h5 {
        font-size: 1.1em;
    }
}

/* Ensure equal height columns */
.card.h-100 {
    height: 100% !important;
    display: flex;
    flex-direction: column;
}

.card-body {
    flex: 1;
}
</style>
```

#### `Views/Links/ManageCategories.cshtml`
```html
@model List<Members.Models.LinkCategory>
@{
    ViewData["Title"] = "Manage Link Categories";
}

<div class="container-fluid mt-4">
    <div class="row">
        <div class="col-12">
            <!-- Page Header -->
            <div class="mb-4">
                <!-- Title Row -->
                <h2><i class="fas fa-cog"></i> Manage Link Categories</h2>
                
                <!-- Description Row -->
                <p>Create and organize link categories for the More Links page</p>
                
                <!-- Button Row -->
                <div class="mt-3">
                    <a href="@Url.Action("MoreLinks", "Links")" class="btn btn-secondary">
                        <i class="fas fa-arrow-left"></i> Back to More Links
                    </a>
                    <a href="@Url.Action("ManageLinks", "Links")" class="btn btn-primary ms-2">
                        <i class="fas fa-link"></i> Manage Links
                    </a>
                </div>
            </div>

            @if (TempData["SuccessMessage"] != null)
            {
                <div class="alert alert-success alert-dismissible fade show" role="alert">
                    @TempData["SuccessMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            @if (TempData["ErrorMessage"] != null)
            {
                <div class="alert alert-danger alert-dismissible fade show" role="alert">
                    @TempData["ErrorMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            <!-- Create New Category Form -->
            <div class="card mb-4">
                <div class="card-header">
                    <h4><i class="fas fa-plus"></i> Create New Category</h4>
                </div>
                <div class="card-body">
                    <form asp-action="CreateCategory" method="post" class="row g-3">
                        @Html.AntiForgeryToken()
                        <div class="col-md-6">
                            <label for="categoryName" class="form-label">Category Name</label>
                            <input type="text" class="form-control" id="categoryName" name="categoryName" 
                                   placeholder="Enter category name" required maxlength="255">
                        </div>
                        <div class="col-md-3">
                            <div class="form-check mt-4 pt-2">
                                <input class="form-check-input" type="checkbox" id="isAdminOnly" name="isAdminOnly" value="true">
                                <input type="hidden" name="isAdminOnly" value="false">
                                <label class="form-check-label" for="isAdminOnly">
                                    Admin Only
                                </label>
                            </div>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label">&nbsp;</label>
                            <button type="submit" class="btn btn-success d-block w-100">
                                <i class="fas fa-plus"></i> Create Category
                            </button>
                        </div>
                    </form>
                </div>
            </div>

            <!-- Existing Categories -->
            @if (Model.Any())
            {
                <div class="card">
                    <div class="card-header">
                        <h4><i class="fas fa-list"></i> Existing Categories</h4>
                    </div>
                    <div class="card-body">
                        <div class="table-responsive">
                            <table class="table table-hover">
                                <thead class="table-dark">
                                    <tr>
                                        <th width="5%">Order</th>
                                        <th width="40%">Category Name</th>
                                        <th width="15%">Type</th>
                                        <th width="15%">Links Count</th>
                                        <th width="25%">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    @for (int i = 0; i < Model.Count; i++)
                                    {
                                        var category = Model[i];
                                        <tr>
                                            <td class="text-center">
                                                <span class="badge bg-secondary">@category.SortOrder</span>
                                            </td>
                                            <td>
                                                <strong>@category.CategoryName</strong>
                                            </td>
                                            <td>
                                                @if (category.IsAdminOnly)
                                                {
                                                    <span class="badge bg-warning text-dark">Admin Only</span>
                                                }
                                                else
                                                {
                                                    <span class="badge bg-info">Public</span>
                                                }
                                            </td>
                                            <td class="text-center">
                                                <a href="@Url.Action("ManageLinks", new { categoryId = category.CategoryID })" 
                                                   class="badge bg-primary text-decoration-none">
                                                    @category.CategoryLinks.Count links
                                                </a>
                                            </td>
                                            <td>
                                                <!-- Sort Order Controls -->
                                                @if (i > 0)
                                                {
                                                    <form asp-action="UpdateCategorySortOrder" method="post" class="d-inline me-1">
                                                        @Html.AntiForgeryToken()
                                                        <input type="hidden" name="categoryId" value="@category.CategoryID">
                                                        <input type="hidden" name="direction" value="up">
                                                        <button type="submit" class="btn btn-sm btn-rename" title="Move Up">
                                                            <i class="bi bi-arrow-up"></i>
                                                        </button>
                                                    </form>
                                                }
                                                else
                                                {
                                                    <button class="btn btn-sm btn-secondary me-1" disabled title="Already at top">
                                                        <i class="bi bi-arrow-up"></i>
                                                    </button>
                                                }

                                                @if (i < Model.Count - 1)
                                                {
                                                    <form asp-action="UpdateCategorySortOrder" method="post" class="d-inline me-1">
                                                        @Html.AntiForgeryToken()
                                                        <input type="hidden" name="categoryId" value="@category.CategoryID">
                                                        <input type="hidden" name="direction" value="down">
                                                        <button type="submit" class="btn btn-sm btn-rename" title="Move Down">
                                                            <i class="bi bi-arrow-down"></i>
                                                        </button>
                                                    </form>
                                                }
                                                else
                                                {
                                                    <button class="btn btn-sm btn-secondary me-1" disabled title="Already at bottom">
                                                        <i class="bi bi-arrow-down"></i>
                                                    </button>
                                                }

                                                <!-- Manage Links Button -->
                                                <a href="@Url.Action("ManageLinks", new { categoryId = category.CategoryID })" 
                                                   class="btn btn-sm btn-billing me-1" title="Manage Links for @category.CategoryName">
                                                    <i class="bi bi-link-45deg"></i>
                                                </a>

                                                <!-- Delete Button -->
                                                <button type="button" class="btn btn-sm btn-delete" 
                                                        onclick="confirmDelete(@category.CategoryID, '@category.CategoryName', @category.CategoryLinks.Count)"
                                                        title="Delete Category: @category.CategoryName">
                                                    <i class="bi bi-trash"></i>
                                                </button>
                                            </td>
                                        </tr>
                                    }
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>
            }
            else
            {
                <div class="alert alert-info">
                    <h4><i class="fas fa-info-circle"></i> No Categories Found</h4>
                    <p class="mb-0">No link categories have been created yet. Use the form above to create your first category.</p>
                </div>
            }
        </div>
    </div>
</div>

<!-- Delete Confirmation Modal -->
<div class="modal fade" id="deleteModal" tabindex="-1">
    <div class="modal-dialog">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">Confirm Delete</h5>
                <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
            </div>
            <div class="modal-body">
                <div class="alert alert-warning">
                    <i class="fas fa-exclamation-triangle"></i>
                    <strong>Warning:</strong> This action cannot be undone.
                </div>
                <p>Are you sure you want to delete the category <strong id="categoryNameToDelete"></strong>?</p>
                <p id="linkCountWarning" class="text-danger" style="display: none;">
                    <i class="fas fa-exclamation-circle"></i>
                    This will also delete <span id="linkCount"></span> associated link(s).
                </p>
            </div>
            <div class="modal-footer">
                <form id="deleteForm" method="post" asp-action="DeleteCategory">
                    @Html.AntiForgeryToken()
                    <input type="hidden" id="categoryIdToDelete" name="categoryId">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="submit" class="btn btn-danger">
                        <i class="fas fa-trash"></i> Delete Category
                    </button>
                </form>
            </div>
        </div>
    </div>
</div>

<script>
function confirmDelete(categoryId, categoryName, linkCount) {
    document.getElementById('categoryIdToDelete').value = categoryId;
    document.getElementById('categoryNameToDelete').textContent = categoryName;
    
    const linkCountWarning = document.getElementById('linkCountWarning');
    const linkCountSpan = document.getElementById('linkCount');
    
    if (linkCount > 0) {
        linkCountSpan.textContent = linkCount;
        linkCountWarning.style.display = 'block';
    } else {
        linkCountWarning.style.display = 'none';
    }
    
    new bootstrap.Modal(document.getElementById('deleteModal')).show();
}
</script>

<style>
/* Table hover effects */
.table tbody tr:hover {
    background-color: rgba(0,0,0,0.02);
}

/* Button group styling */
.btn-group .btn {
    border-radius: 0;
}

.btn-group .btn:first-child {
    border-top-left-radius: 0.25rem;
    border-bottom-left-radius: 0.25rem;
}

.btn-group .btn:last-child {
    border-top-right-radius: 0.25rem;
    border-bottom-right-radius: 0.25rem;
}

/* Form styling */
.form-check {
    padding-top: 0.5rem;
}

/* Badge links */
.badge.text-decoration-none:hover {
    opacity: 0.8;
}
</style>
```

#### `Views/Links/ManageLinks.cshtml`
```html
@model List<Members.Models.CategoryLink>
@{
    ViewData["Title"] = "Manage Category Links";
}

<div class="container-fluid mt-4">
    <div class="row">
        <div class="col-12">
            <!-- Page Header -->
            <div class="mb-4">
                <!-- Title Row -->
                <h2><i class="fas fa-link"></i> Manage Category Links</h2>
                
                <!-- Description Row -->
                <p>Add and organize links within categories</p>
                
                <!-- Button Row -->
                <div class="mt-3">
                    <a href="@Url.Action("MoreLinks", "Links")" class="btn btn-secondary">
                        <i class="fas fa-arrow-left"></i> Back to More Links
                    </a>
                    <a href="@Url.Action("ManageCategories", "Links")" class="btn btn-primary ms-2">
                        <i class="fas fa-cog"></i> Manage Categories
                    </a>
                </div>
            </div>

            @if (TempData["SuccessMessage"] != null)
            {
                <div class="alert alert-success alert-dismissible fade show" role="alert">
                    @TempData["SuccessMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            @if (TempData["ErrorMessage"] != null)
            {
                <div class="alert alert-danger alert-dismissible fade show" role="alert">
                    @TempData["ErrorMessage"]
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>
            }

            <!-- Category Selection -->
            <div class="card mb-4">
                <div class="card-header">
                    <h4><i class="fas fa-folder"></i> Select Category</h4>
                </div>
                <div class="card-body">
                    <form method="get" asp-action="ManageLinks" class="row g-3">
                        <div class="col-md-10">
                            <label for="categoryId" class="form-label">Choose a category to manage its links</label>
                            <select class="form-select" id="categoryId" name="categoryId" onchange="this.form.submit()">
                                <option value="">-- Select a Category --</option>
                                @foreach (var category in (ViewBag.LinkCategories as List<Members.Models.LinkCategory>) ?? new List<Members.Models.LinkCategory>())
                                {
                                    var isSelected = ViewBag.SelectedCategoryId != null && ViewBag.SelectedCategoryId.ToString() == category.CategoryID.ToString();
                                    <option value="@category.CategoryID" selected="@isSelected">
                                        @category.CategoryName@(category.IsAdminOnly ? " (Admin Only)" : "")
                                    </option>
                                }
                            </select>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label">&nbsp;</label>
                            <button type="submit" class="btn btn-outline-primary d-block w-100">
                                <i class="fas fa-search"></i> Load
                            </button>
                        </div>
                    </form>
                </div>
            </div>

            @if (ViewBag.SelectedCategoryId != null)
            {
                <!-- Add New Link Form -->
                <div class="card mb-4">
                    <div class="card-header">
                        <h4><i class="fas fa-plus"></i> Add New Link to "@ViewBag.SelectedCategoryName"</h4>
                    </div>
                    <div class="card-body">
                        <form asp-action="CreateLink" method="post" class="row g-3">
                            @Html.AntiForgeryToken()
                            <input type="hidden" name="categoryId" value="@ViewBag.SelectedCategoryId">
                            
                            <div class="col-md-5">
                                <label for="linkName" class="form-label">Link Name</label>
                                <input type="text" class="form-control" id="linkName" name="linkName" 
                                       placeholder="Enter descriptive link name" required maxlength="255">
                            </div>
                            <div class="col-md-5">
                                <label for="linkUrl" class="form-label">Link URL</label>
                                <input type="url" class="form-control" id="linkUrl" name="linkUrl" 
                                       placeholder="https://example.com" required maxlength="500">
                                <div class="form-text">Must start with http:// or https://</div>
                            </div>
                            <div class="col-md-2">
                                <label class="form-label">&nbsp;</label>
                                <button type="submit" class="btn btn-success d-block w-100">
                                    <i class="fas fa-plus"></i> Add Link
                                </button>
                            </div>
                        </form>
                    </div>
                </div>

                <!-- Existing Links -->
                @if (Model.Any())
                {
                    <div class="card">
                        <div class="card-header">
                            <h4><i class="fas fa-list"></i> Links in "@ViewBag.SelectedCategoryName" (@Model.Count)</h4>
                        </div>
                        <div class="card-body">
                            <div class="table-responsive">
                                <table class="table table-hover">
                                    <thead class="table-dark">
                                        <tr>
                                            <th width="5%">Order</th>
                                            <th width="25%">Link Name</th>
                                            <th width="40%">URL</th>
                                            <th width="10%">Preview</th>
                                            <th width="20%">Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        @for (int i = 0; i < Model.Count; i++)
                                        {
                                            var link = Model[i];
                                            <tr>
                                                <td class="text-center">
                                                    <span class="badge bg-secondary">@link.SortOrder</span>
                                                </td>
                                                <td>
                                                    <strong>@link.LinkName</strong>
                                                </td>
                                                <td>
                                                    <code class="text-break small">@link.LinkUrl</code>
                                                </td>
                                                <td class="text-center">
                                                    <a href="@link.LinkUrl" 
                                                       target="_blank" 
                                                       rel="noopener noreferrer"
                                                       class="btn btn-success btn-sm"
                                                       title="Open link in new tab">
                                                        <i class="fas fa-external-link-alt me-1"></i>Test
                                                    </a>
                                                </td>
                                                <td>
                                                    <!-- Sort Order Controls -->
                                                    @if (i > 0)
                                                    {
                                                        <form asp-action="UpdateLinkSortOrder" method="post" class="d-inline me-1">
                                                            @Html.AntiForgeryToken()
                                                            <input type="hidden" name="linkId" value="@link.LinkID">
                                                            <input type="hidden" name="direction" value="up">
                                                            <button type="submit" class="btn btn-sm btn-rename" title="Move Up">
                                                                <i class="bi bi-arrow-up"></i>
                                                            </button>
                                                        </form>
                                                    }
                                                    else
                                                    {
                                                        <button class="btn btn-sm btn-secondary me-1" disabled title="Already at top">
                                                            <i class="bi bi-arrow-up"></i>
                                                        </button>
                                                    }

                                                    @if (i < Model.Count - 1)
                                                    {
                                                        <form asp-action="UpdateLinkSortOrder" method="post" class="d-inline me-1">
                                                            @Html.AntiForgeryToken()
                                                            <input type="hidden" name="linkId" value="@link.LinkID">
                                                            <input type="hidden" name="direction" value="down">
                                                            <button type="submit" class="btn btn-sm btn-rename" title="Move Down">
                                                                <i class="bi bi-arrow-down"></i>
                                                            </button>
                                                        </form>
                                                    }
                                                    else
                                                    {
                                                        <button class="btn btn-sm btn-secondary me-1" disabled title="Already at bottom">
                                                            <i class="bi bi-arrow-down"></i>
                                                        </button>
                                                    }

                                                    <!-- Delete Button -->
                                                    <button type="button" class="btn btn-sm btn-delete" 
                                                            onclick="confirmDeleteLink(@link.LinkID, '@Html.Raw(Html.Encode(link.LinkName))')"
                                                            title="Delete Link">
                                                        <i class="bi bi-trash"></i>
                                                    </button>
                                                </td>
                                            </tr>
                                        }
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                }
                else
                {
                    <div class="alert alert-info">
                        <h4><i class="fas fa-info-circle"></i> No Links Found</h4>
                        <p class="mb-0">No links have been added to "@ViewBag.SelectedCategoryName" yet. Use the form above to add your first link.</p>
                    </div>
                }
            }
            else
            {
                <div class="alert alert-secondary">
                    <h4><i class="fas fa-hand-point-up"></i> Select a Category</h4>
                    <p class="mb-0">Choose a category from the dropdown above to view and manage its links.</p>
                </div>
            }
        </div>
    </div>
</div>

<!-- Delete Link Confirmation Modal -->
<div class="modal fade" id="deleteLinkModal" tabindex="-1">
    <div class="modal-dialog">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">Confirm Delete</h5>
                <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
            </div>
            <div class="modal-body">
                <div class="alert alert-warning">
                    <i class="fas fa-exclamation-triangle"></i>
                    <strong>Warning:</strong> This action cannot be undone.
                </div>
                <p>Are you sure you want to delete the link <strong id="linkNameToDelete"></strong>?</p>
            </div>
            <div class="modal-footer">
                <form id="deleteLinkForm" method="post" asp-action="DeleteLink">
                    @Html.AntiForgeryToken()
                    <input type="hidden" id="linkIdToDelete" name="linkId">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="submit" class="btn btn-danger">
                        <i class="fas fa-trash"></i> Delete Link
                    </button>
                </form>
            </div>
        </div>
    </div>
</div>

<script>
function confirmDeleteLink(linkId, linkName) {
    document.getElementById('linkIdToDelete').value = linkId;
    document.getElementById('linkNameToDelete').textContent = linkName;
    new bootstrap.Modal(document.getElementById('deleteLinkModal')).show();
}

// Auto-format URL input
document.addEventListener('DOMContentLoaded', function() {
    const urlInput = document.getElementById('linkUrl');
    if (urlInput) {
        urlInput.addEventListener('blur', function() {
            let url = this.value.trim();
            if (url && !url.startsWith('http://') && !url.startsWith('https://')) {
                this.value = 'https://' + url;
            }
        });
    }
});
</script>

<style>
/* Table hover effects */
.table tbody tr:hover {
    background-color: rgba(0,0,0,0.02);
}

/* Button group styling */
.btn-group .btn {
    border-radius: 0;
}

.btn-group .btn:first-child {
    border-top-left-radius: 0.25rem;
    border-bottom-left-radius: 0.25rem;
}

.btn-group .btn:last-child {
    border-top-right-radius: 0.25rem;
    border-bottom-right-radius: 0.25rem;
}

/* URL text styling */
code {
    background-color: #f8f9fa;
    padding: 2px 6px;
    border-radius: 3px;
    word-break: break-all;
}

/* Form styling improvements */
.form-text {
    font-size: 0.875em;
}

/* Select dropdown styling */
.form-select:focus {
    border-color: #86b7fe;
    outline: 0;
    box-shadow: 0 0 0 0.25rem rgba(13, 110, 253, 0.25);
}

/* External link button styling */
.btn-outline-primary:hover .fa-external-link-alt {
    transform: scale(1.1);
}
</style>
```

## 🔧 Configuration Changes

### Step 2: Update ApplicationDbContext.cs
Add these DbSets to your ApplicationDbContext:

```csharp
public DbSet<LinkCategory> LinkCategories { get; set; }
public DbSet<CategoryLink> CategoryLinks { get; set; }
```

### Step 3: Add Navigation Menu Item
Add to your main navigation (typically in `Views/Shared/_Layout.cshtml` or similar):

```html
<li><a class="nav-link" asp-controller="Links" asp-action="MoreLinks">
    <i class="bi bi-link-45deg"></i> More Links
</a></li>
```

## 🎨 CSS Requirements

### Step 4: Button CSS Classes
Your project needs these CSS classes for proper button styling:

```css
/* Button styling - Add these to your main CSS file */
.btn-billing {
    /* Your billing button styling */
}

.btn-rename {
    /* Your rename/edit button styling */
}

.btn-delete {
    /* Your delete button styling */
}
```

### Step 5: Card Theme Integration
If your project uses custom card styling, update these CSS variables:

```css
:root {
    --card-bg: #DCEDFF;         /* Light blue card background */
    --card-header-bg: #A3CBF6;  /* Card header background */
    --card-body-bg: #F8F9FA;    /* Card body background */
}
```

## 🚀 Deployment Checklist

### ✅ Implementation Steps:
1. **Execute Database Script** - Run the SQL script to create tables
2. **Copy Model Files** - Add LinkCategory.cs and CategoryLink.cs to Models/
3. **Copy Controller** - Add LinksController.cs to Controllers/
4. **Copy View Files** - Add all three view files to Views/Links/
5. **Update DbContext** - Add the two DbSets
6. **Add Navigation** - Add More Links menu item
7. **Style Integration** - Ensure button CSS classes exist
8. **Test Access** - Verify Admin/Manager roles can access management pages

### 🧪 Testing Verification:
- [ ] Public page loads at `/Links/MoreLinks`
- [ ] Admin/Manager can access management pages
- [ ] Categories can be created and reordered
- [ ] Links can be added to categories
- [ ] Up/down arrows work for sorting
- [ ] Delete functionality works with confirmation
- [ ] Responsive design works on mobile
- [ ] Eye icons appear before link names

## 📞 Support Notes
- **Database Tables**: Auto-created on first access if missing
- **Initial Data**: Test categories and links created automatically
- **Role Security**: All management functions require Admin or Manager role
- **Bootstrap Icons**: Uses `bi-*` classes (ensure Bootstrap Icons are included)
- **Responsive**: Mobile-first design with Bootstrap responsive classes

This package provides everything needed to implement the complete More Links system in any similar ASP.NET Core MVC project with Entity Framework and Identity.

---

## 🚨 CRITICAL IMPLEMENTATION NOTES 🚨

**These additional steps are REQUIRED and were discovered during real-world deployment:**

### ⚠️ Issue #1: Razor View CSS Constraints
**PROBLEM:** `@media` queries cannot be used in Razor view `<style>` blocks - they cause compilation errors
**SOLUTION:** 
- Extract ALL CSS from view files and move to your main CSS file (usually `site.css`)
- Add a dedicated section like `/* #region More Links System CSS */`
- This includes the responsive `@media (max-width: 768px)` queries

### ⚠️ Issue #2: Database Auto-Creation Causes Crashes  
**PROBLEM:** The `EnsureDatabaseTablesExist()` method in LinksController causes resource locks and crashes
**SOLUTION:**
- **REMOVE the entire `EnsureDatabaseTablesExist()` method from LinksController**
- **ALWAYS run database scripts manually** - never rely on auto-creation
- Add proper try-catch error handling in controller actions instead
- Show helpful error messages when tables don't exist

### ⚠️ Issue #3: Navigation Integration Complexity
**PROBLEM:** Replacing existing static links requires multiple reference updates
**SOLUTION:** Search your entire project for existing MoreLinks references:
- Update header navigation (usually `_Layout.cshtml` or `_PartialHeader.cshtml`)
- Update footer navigation (usually `_PartialFooter.cshtml`) 
- Comment out/remove old controller actions
- Archive old view files as `.OBSOLETE`
- Search for hardcoded `/Info/MoreLinks` paths

### ⚠️ Issue #4: Mobile Responsiveness Needs Enhancement
**PROBLEM:** Admin buttons may wrap awkwardly on mobile devices
**SOLUTION:** Add this CSS to your main CSS file:
```css
/* Admin buttons responsive spacing */
.admin-buttons-container {
    gap: 0.5rem;
}

@media (max-width: 576px) {
    .admin-buttons-container {
        flex-direction: column;
        align-items: center;
    }
    
    .admin-buttons-container .btn {
        width: auto;
        min-width: 150px;
    }
}
```

### ⚠️ Issue #5: Environment-Specific Requirements
**VERIFY YOUR PROJECT HAS:**
- ASP.NET Core MVC with Entity Framework Core
- Bootstrap 5.x with Bootstrap Icons (`bi-*` classes)
- Role-based authorization with "Admin" and "Manager" roles
- Custom button CSS classes: `.btn-billing`, `.btn-rename`, `.btn-delete`
- If using dynamic colors, ensure CSS variable support

---

## 📋 REVISED DEPLOYMENT CHECKLIST

### ✅ Pre-Implementation Steps:
1. **Verify Environment Requirements** (see Issue #5 above)
2. **Search for existing MoreLinks references** in your project
3. **Backup your project** before making changes

### ✅ Implementation Steps:
1. **Execute Database Script** - Run the SQL script to create tables (**MANUALLY ONLY**)
2. **Copy Model Files** - Add LinkCategory.cs and CategoryLink.cs to Models/
3. **Copy Controller** - Add LinksController.cs to Controllers/ (**REMOVE EnsureDatabaseTablesExist method**)
4. **Copy View Files** - Add all three view files to Views/Links/
5. **Update DbContext** - Add the two DbSets to ApplicationDbContext
6. **Extract and Move CSS** - Move ALL CSS from views to your main CSS file (**CRITICAL**)
7. **Update Navigation** - Update header and footer navigation references
8. **Add Mobile CSS** - Add the `admin-buttons-container` CSS class
9. **Clean Up Old References** - Comment out old controller actions, archive old views

### ✅ Testing Verification:
- [ ] Public page loads at `/Links/MoreLinks` without crashes
- [ ] Admin/Manager can access management pages  
- [ ] Database errors show helpful messages (don't crash)
- [ ] Categories can be created and reordered
- [ ] Links can be added to categories
- [ ] Mobile layout centers properly and buttons stack vertically
- [ ] All navigation links point to new system
- [ ] No references to old static MoreLinks remain

### 🚨 CRITICAL: Modified Controller Code
**REMOVE this problematic method entirely from LinksController:**
```csharp
// DELETE THIS ENTIRE METHOD - IT CAUSES CRASHES
private async Task EnsureDatabaseTablesExist() { ... }
```

**REPLACE the MoreLinks action with proper error handling:**
```csharp
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

        foreach (var category in categories)
        {
            category.CategoryLinks = category.CategoryLinks.OrderBy(l => l.SortOrder).ThenBy(l => l.LinkName).ToList();
        }

        ViewData["Title"] = "More Links";
        return View(categories);
    }
    catch (Exception ex)
    {
        ViewData["Title"] = "More Links";
        ViewBag.DatabaseError = "The More Links system is not yet configured. Please run the database setup scripts first.";
        ViewBag.ErrorDetails = ex.Message;
        return View(new List<LinkCategory>());
    }
}
```

---

This package provides everything needed to implement the complete More Links system, including all critical implementation fixes discovered during real-world deployment.