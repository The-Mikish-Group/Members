using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    public class ManagerPdfCategoryController(IWebHostEnvironment environment, ILogger<ManagerPdfCategoryController> logger, ApplicationDbContext context) : Controller
    {
        private readonly IWebHostEnvironment _environment = environment;
        private readonly ILogger<ManagerPdfCategoryController> _logger = logger;
        private readonly ApplicationDbContext _context = context;
        private readonly string _protectedFilesBasePath = Path.Combine(environment.ContentRootPath, "ProtectedFiles");

        public async Task<IActionResult> ManagerManageCategoryFiles(int? categoryId)
        {
            var adminOnlyCategories = await _context.PDFCategories
                                            .Where(c => c.IsAdminOnly == true)
                                            .OrderBy(c => c.SortOrder)
                                            .ThenBy(c => c.CategoryName)
                                            .ToListAsync();
            ViewBag.PDFCategories = adminOnlyCategories;
            ViewBag.SelectedCategoryId = categoryId;
            ViewData["Title"] = "Manage Confidential Category Files";
            
            List<CategoryFile> files = [];
            if (categoryId.HasValue)
            {
                var selectedCategory = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId.Value && c.IsAdminOnly == true);
                if (selectedCategory == null)
                {
                    TempData["ErrorMessage"] = "Selected category is not a confidential category or does not exist.";
                    return RedirectToAction(nameof(ManagerCategories));
                }
                
                var filesFromDb = await _context.CategoryFiles
                                        .Where(f => f.CategoryID == categoryId.Value)
                                        .OrderBy(f => f.SortOrder).ThenBy(f => f.FileName)
                                        .ToListAsync();
                var validFiles = new List<CategoryFile>();
                var orphanedDbEntries = new List<CategoryFile>();

                if (Directory.Exists(_protectedFilesBasePath))
                {
                    foreach (var dbFile in filesFromDb)
                    {
                        if (System.IO.File.Exists(Path.Combine(_protectedFilesBasePath, dbFile.FileName)))
                            validFiles.Add(dbFile);
                        else
                            orphanedDbEntries.Add(dbFile);
                    }
                } else { 
                    _logger.LogWarning("ProtectedFiles directory not found at {_protectedFilesBasePath} while listing files for category {categoryId.Value}.", _protectedFilesBasePath, categoryId.Value);
                    orphanedDbEntries.AddRange(filesFromDb); 
                }

                if (orphanedDbEntries.Count != 0)
                {
                    _context.CategoryFiles.RemoveRange(orphanedDbEntries);
                    await _context.SaveChangesAsync();
                    _logger.LogWarning("Removed {orphanedDbEntries.Count} orphaned DB file entries for confidential category ID {categoryId.Value}.", orphanedDbEntries.Count, categoryId.Value);
                }
                files = validFiles;
            }
            ViewBag.SuccessMessage = TempData["SuccessMessage"];
            ViewBag.ErrorMessage = TempData["ErrorMessage"];
            return View("~/Views/ManagerPdfCategory/ManagerManageCategoryFiles.cshtml", files);
        }

        public async Task<IActionResult> ManagerCategories()
        {
            var categories = await _context.PDFCategories
                .Where(c => c.IsAdminOnly == true) 
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CategoryName)
                .ToListAsync();

            int nextSortOrder = 1;
            if (categories.Count != 0)
            {
                nextSortOrder = categories.Max(c => c.SortOrder) + 1;
            }
            ViewBag.NextSortOrder = nextSortOrder;
            ViewData["Title"] = "Manage Confidential Categories";
            return View("~/Views/ManagerPdfCategory/ManagerCategories.cshtml", categories);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManagerCategory(string categoryName, int sortOrder) 
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                ModelState.AddModelError("CategoryName", "Category name is required.");
            }
            if (sortOrder < 1) {
                ModelState.AddModelError("SortOrder", "Sort order must be at least 1.");
            }

            if (ModelState.IsValid)
            {
                PDFCategory newCategory = new()
                {
                    CategoryName = categoryName,
                    SortOrder = sortOrder,
                    IsAdminOnly = true 
                };
                _context.PDFCategories.Add(newCategory);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Confidential category '{newCategory.CategoryName}' created successfully.";
                return RedirectToAction(nameof(ManagerCategories));
            }
            
            var categories = await _context.PDFCategories
                .Where(c => c.IsAdminOnly == true)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.CategoryName).ToListAsync();
            ViewBag.NextSortOrder = (categories.Count != 0 ? categories.Max(c => c.SortOrder) : 0) + 1;
            ViewData["CurrentCategoryName"] = categoryName; 
            ViewData["CurrentSortOrder"] = sortOrder;
            return View("~/Views/ManagerPdfCategory/ManagerCategories.cshtml", categories);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditManagerCategory(int categoryID, string categoryName, int sortOrder)
        {
            var categoryToUpdate = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryID && c.IsAdminOnly == true);
            if (categoryToUpdate == null)
            {
                return NotFound("Confidential category not found.");
            }
            if (string.IsNullOrWhiteSpace(categoryName)) return BadRequest("Category name cannot be empty.");
            if (sortOrder < 1) return BadRequest("Sort order must be at least 1.");

            categoryToUpdate.CategoryName = categoryName;
            categoryToUpdate.SortOrder = sortOrder;

            try
            {
                _context.Update(categoryToUpdate);
                await _context.SaveChangesAsync();
                return Ok(); 
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.PDFCategories.Any(e => e.CategoryID == categoryID && e.IsAdminOnly == true)) { return NotFound(); } else { throw; }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteManagerCategoryConfirmed(int id)
        {
            var category = await _context.PDFCategories.Include(c => c.CategoryFiles).FirstOrDefaultAsync(c => c.CategoryID == id && c.IsAdminOnly == true);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Confidential category not found.";
                return RedirectToAction(nameof(ManagerCategories));
            }

            foreach (var fileEntry in category.CategoryFiles.ToList())
            {
                await DeletePhysicalFileIfNotLinked(fileEntry.FileName, category.CategoryID, true); 
                _context.CategoryFiles.Remove(fileEntry);
            }
            _context.PDFCategories.Remove(category);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Confidential category '{category.CategoryName}' and its associated file entries have been deleted.";
            return RedirectToAction(nameof(ManagerCategories));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadFileToManagerCategory(int categoryId, IFormFile file, int sortOrder)
        {
            var category = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == true);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Cannot upload file: Selected category is not a valid confidential category.";
                return RedirectToAction(nameof(ManagerManageCategoryFiles));
            }

            if (file == null || file.Length == 0) { TempData["ErrorMessage"] = "No file selected or file is empty."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { TempData["ErrorMessage"] = "Only PDF files are allowed."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}
            if (sortOrder < 1) { TempData["ErrorMessage"] = "Sort order must be at least 1."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}

            var originalFileName = Path.GetFileName(file.FileName);
            var sanitizedFileName = Path.GetInvalidFileNameChars().Aggregate(originalFileName, (current, c) => current.Replace(c.ToString(), "_"));
            
            var filePath = Path.Combine(_protectedFilesBasePath, sanitizedFileName);

            try
            {
                if (!Directory.Exists(_protectedFilesBasePath)) Directory.CreateDirectory(_protectedFilesBasePath);
                using var fileStream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(fileStream);
            }
            catch (Exception ex) { _logger.LogError(ex, "UploadFileToManagerCategory: Error saving physical file {FileName}", sanitizedFileName); TempData["ErrorMessage"] = "Error saving file to server."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}

            var existingCategoryFile = await _context.CategoryFiles.FirstOrDefaultAsync(cf => cf.CategoryID == categoryId && cf.FileName == sanitizedFileName);
            if (existingCategoryFile != null) { existingCategoryFile.SortOrder = sortOrder; }
            else { _context.CategoryFiles.Add(new CategoryFile { CategoryID = categoryId, FileName = sanitizedFileName, SortOrder = sortOrder, PDFCategory = category }); }

            try { await _context.SaveChangesAsync(); TempData["SuccessMessage"] = $"File '{sanitizedFileName}' uploaded to '{category.CategoryName}'."; }
            catch(Exception ex) { _logger.LogError(ex, "UploadFileToManagerCategory: Error saving DB entry for {FileName}", sanitizedFileName); TempData["ErrorMessage"] = "Error saving file information to database."; if(System.IO.File.Exists(filePath)) try { System.IO.File.Delete(filePath); } catch {/*ignore*/} }
            return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });
        }

        [HttpPost("ManagerPdfCategory/DeleteFileFromManagerCategory/{id}/{categoryId}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFileFromManagerCategory(int id, int categoryId)
        {
            var parentCategory = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == true);
            if (parentCategory == null) { TempData["ErrorMessage"] = "Parent category is not a valid confidential category."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId }); }

            var categoryFileToDelete = await _context.CategoryFiles.FirstOrDefaultAsync(cf => cf.FileID == id && cf.CategoryID == categoryId);
            if (categoryFileToDelete != null)
            {
                string fileName = categoryFileToDelete.FileName;
                _context.CategoryFiles.Remove(categoryFileToDelete);
                await _context.SaveChangesAsync(); 
                await DeletePhysicalFileIfNotLinked(fileName, categoryId, false); 
                TempData["SuccessMessage"] = $"File '{fileName}' deleted successfully.";
            } else { TempData["ErrorMessage"] = "File not found for deletion."; }
            return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameFileInManagerCategory(int renameFileId, int categoryId, string oldFileName, string newFileName, int newSortOrder)
        {
            var parentCategory = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == true);
            if (parentCategory == null) { TempData["ErrorMessage"] = "Invalid confidential category."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId }); }

            if (string.IsNullOrWhiteSpace(newFileName) || !newFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { TempData["ErrorMessage"] = "New file name must be valid and end with .pdf."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}
            if (newSortOrder < 1) { TempData["ErrorMessage"] = "Sort order must be at least 1."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });}

            var categoryFile = await _context.CategoryFiles.FirstOrDefaultAsync(cf => cf.FileID == renameFileId && cf.CategoryID == categoryId);
            if (categoryFile == null) { TempData["ErrorMessage"] = "File to update not found."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId }); }

            bool nameChanged = !oldFileName.Equals(newFileName, StringComparison.OrdinalIgnoreCase);
            bool sortChanged = categoryFile.SortOrder != newSortOrder;

            if (nameChanged)
            {
                var oldFilePath = Path.Combine(_protectedFilesBasePath, oldFileName);
                var newFilePath = Path.Combine(_protectedFilesBasePath, newFileName);

                if (System.IO.File.Exists(newFilePath)) { TempData["ErrorMessage"] = $"A file named '{newFileName}' already exists."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId }); }
                if (!System.IO.File.Exists(oldFilePath)) { TempData["ErrorMessage"] = $"Original file '{oldFileName}' not found on disk."; categoryFile.FileName = newFileName; }
                else { try { System.IO.File.Move(oldFilePath, newFilePath); categoryFile.FileName = newFileName; } catch (Exception ex) { _logger.LogError(ex, "Error renaming physical file from {Old} to {New}", oldFileName, newFileName); TempData["ErrorMessage"] = "Error renaming physical file."; return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId }); } }
            }
            
            if (nameChanged || sortChanged) {
                categoryFile.SortOrder = newSortOrder;
                try { await _context.SaveChangesAsync(); TempData["SuccessMessage"] = "File details updated successfully."; }
                catch (Exception ex) { _logger.LogError(ex, "Error saving file detail DB changes for FileID {FileID}", renameFileId); TempData["ErrorMessage"] = "Error saving DB changes."; }
            } else { TempData["SuccessMessage"] = "No changes detected to file details."; } 
            
            return RedirectToAction(nameof(ManagerManageCategoryFiles), new { categoryId });
        }
        
        [HttpGet]
        public async Task<IActionResult> GetNextSortOrder(int categoryId)
        {
            var category = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryID == categoryId && c.IsAdminOnly == true);
            if (category == null) { return NotFound("Confidential category not found."); }
            var maxSortOrder = await _context.CategoryFiles.Where(f => f.CategoryID == categoryId).MaxAsync(f => (int?)f.SortOrder);
            return Json((maxSortOrder ?? 0) + 1);
        }
        
        private async Task DeletePhysicalFileIfNotLinked(string fileName, int currentCategoryIdToIgnore, bool isCategoryBeingDeleted)
        {
            bool isLinkedElsewhere = false;
            if (!isCategoryBeingDeleted) // If category is NOT being deleted, check if file is linked in OTHER categories
            {
                isLinkedElsewhere = await _context.CategoryFiles
                    .AnyAsync(cf => cf.FileName == fileName && cf.CategoryID != currentCategoryIdToIgnore);
            }
            // If category IS being deleted (specificCategoryCheck=true was a misinterpretation), then we only care if it's linked
            // outside the category being deleted. If isCategoryBeingDeleted is true, we assume all links within this
            // category are going away. So if not linked elsewhere, it's safe to delete.

            if (!isLinkedElsewhere)
            {
                var filePath = Path.Combine(_protectedFilesBasePath, fileName);
                if (System.IO.File.Exists(filePath))
                {
                    try 
                    { 
                        System.IO.File.Delete(filePath);
                        _logger.LogInformation("Physical file deleted (no other links found, or containing category deleted): {FileName}", filePath);
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogError(ex, "Error deleting physical file {FileName}", filePath); 
                    }
                }
            } else {
                 _logger.LogInformation("Physical file NOT deleted (still linked elsewhere): {FileName}", fileName);
            }
        }
    }
}
