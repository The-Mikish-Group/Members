using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Members.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    public class PdfCategoryController(IWebHostEnvironment environment, ILogger<PdfCategoryController> logger, ApplicationDbContext context /*, UserManager<IdentityUser> userManager, UserService userService*/) : Controller
    {
        private readonly IWebHostEnvironment _environment = environment;
        private readonly ILogger<PdfCategoryController> _logger = logger;
        private readonly ApplicationDbContext _context = context;

        // Define the base path for protected files as a readonly field for consistency
        private readonly string _protectedFilesBasePath = Path.Combine(environment.ContentRootPath, "ProtectedFiles");

        // Action to display the main Manage Category Files view
        public async Task<IActionResult> ManageCategoryFiles(int? categoryId) // Made async for async database calls
        {
            ViewBag.PDFCategories = await _context.PDFCategories.OrderBy(c => c.SortOrder).ToListAsync(); // Use ToListAsync
            ViewBag.SelectedCategoryId = categoryId;
            ViewData["Title"] = "Manage Files in Category";

            // Using collection expression based on user's provided code, but explicitly List<CategoryFile> for mutability
            List<CategoryFile> files = []; // Use List<CategoryFile> for easier manipulation

            if (categoryId.HasValue)
            {
                // Retrieve files for the selected category from the database
                var filesFromDb = await _context.CategoryFiles // Use await and ToListAsync
                                        .Where(f => f.CategoryID == categoryId.Value)
                                        .OrderBy(f => f.SortOrder) // Order by SortOrder for consistent listing
                                        .ToListAsync();

                var filesToDelete = new List<CategoryFile>();

                // Ensure the protected files directory exists before proceeding
                if (!Directory.Exists(_protectedFilesBasePath)) // Use the readonly field
                {
                    _logger.LogError("Protected files directory not found for cleanup: {Path}", _protectedFilesBasePath);
                }
                else
                {
                    foreach (var file in filesFromDb)
                    {
                        var filePath = Path.Combine(_protectedFilesBasePath, file.FileName); // Use the readonly field
                        if (!System.IO.File.Exists(filePath))
                        {
                            // File does not exist on disk, mark the database entry for deletion
                            filesToDelete.Add(file);
                            _logger.LogWarning("Orphaned file entry found during cleanup: {FileName} in category {CategoryId}. Marked for deletion from database.", file.FileName, categoryId.Value);
                        }
                        else
                        {
                            // Add valid file entries to the 'files' collection to be displayed in the view
                            files.Add(file);
                        }
                    }
                }

                // Delete the orphaned file entries from the database
                if (filesToDelete.Count != 0)
                {
                    try
                    {
                        _context.CategoryFiles.RemoveRange(filesToDelete);
                        await _context.SaveChangesAsync(); // Use SaveChangesAsync
                        _logger.LogInformation("Deleted {Count} orphaned file entries from category {CategoryId} during cleanup.", filesToDelete.Count, categoryId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting orphaned file entries during cleanup for category {CategoryId}.", categoryId.Value);
                    }
                }

                // The 'files' collection now contains the list of valid files for the view after potential deletions.
                // Re-fetch the list of files for the selected category *after* potential deletions.
                // This ensures the view only displays files that have corresponding physical files and database entries.
                // Using collection expression based on user's code, ensuring it's evaluated
                files = [.. await _context.CategoryFiles // Use await and ToListAsync
                                     .Where(f => f.CategoryID == categoryId.Value)
                                     .OrderBy(f => f.SortOrder).ToListAsync()];
            }
            // If no category is selected, the view model will be an empty list (as initialized)
            // ViewBag.SelectedCategoryId is already set to null above

            // Pass TempData messages if any were set by other actions (like PDF creation in a future controller)
            ViewBag.SuccessMessage = TempData["SuccessMessage"];
            ViewBag.ErrorMessage = TempData["ErrorMessage"];

            return View(files); // Pass the potentially filtered/cleaned list to the view
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCategory(PDFCategory category) // Made async for async database calls
        {
            if (ModelState.IsValid)
            {
                _context.PDFCategories.Add(category);
                await _context.SaveChangesAsync(); // Use SaveChangesAsync
                return RedirectToAction(nameof(ManageCategories));
            }
            return View(category);
        }

        // Made async as it calls an async method in ManageCategoryFiles
        public async Task<IActionResult> ManageCategories()
        {
            var categories = await _context.PDFCategories // Use await and ToListAsync
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CategoryName)
                .ToListAsync();

            int nextSortOrder = 1; // Default value if no categories exist

            if (categories.Count != 0)
            {
                // Find the maximum existing SortOrder and suggest the next one
                // Use MaxAsync for safety and consistency if list is large, otherwise Max() is fine on ToList() result
                nextSortOrder = categories.Max(c => c.SortOrder) + 1;
            }

            ViewBag.NextSortOrder = nextSortOrder; // Pass the value to the view

            return View("Categories", categories);
        }

        // POST: DeleteCategoryConfirmed
        [HttpPost]
        public async Task<IActionResult> DeleteCategoryConfirmed(int id)
        {
            var category = await _context.PDFCategories.FindAsync(id); // Use FindAsync
            if (category == null)
            {
                return NotFound();
            }
            _context.PDFCategories.Remove(category);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageCategories));
        }

        private bool CategoryExists(int id)
        {
            return _context.PDFCategories.Any(e => e.CategoryID == id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCategory(int categoryID, string categoryName, int sortOrder)
        {
            if (ModelState.IsValid)
            {
                var categoryToUpdate = await _context.PDFCategories.FindAsync(categoryID); // Use FindAsync

                if (categoryToUpdate == null)
                {
                    return NotFound();
                }

                categoryToUpdate.CategoryName = categoryName;
                categoryToUpdate.SortOrder = sortOrder;

                try
                {
                    _context.Update(categoryToUpdate);
                    await _context.SaveChangesAsync();
                    return Ok(); // Return a success status
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CategoryExists(categoryID))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return BadRequest(); // Return an error status if ModelState is not valid
        }

        [HttpPost("PdfCategory/DeleteFileFromCategory/{id}/{categoryId}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFileFromCategory(int id, int categoryId)
        {
            var categoryFileToDelete = await _context.CategoryFiles.FindAsync(id); // Use FindAsync
            if (categoryFileToDelete != null)
            {
                var fileName = categoryFileToDelete.FileName; // Get the filename of the entry to be deleted

                // Delete the database entry for the file in the current category first
                _context.CategoryFiles.Remove(categoryFileToDelete);
                await _context.SaveChangesAsync(); // Use SaveChangesAsync // Save changes to remove the entry
                _logger.LogInformation("Deleted database entry for file ID {FileId} (FileName: {FileName}) from category {CategoryId}.", id, fileName, categoryId);

                // Now, check if any other database entries still link to this physical file
                var otherCategoryFiles = await _context.CategoryFiles
                    .Where(cf => cf.FileName == fileName) // Find all entries with the same filename
                    .ToListAsync(); // Use ToListAsync // Execute the query to get the list

                // If no other database entries link to this file (meaning this was the last one), delete the physical file
                if (otherCategoryFiles.Count == 0)
                {
                    var protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles"); // Using the readonly field
                    var filePath = Path.Combine(protectedFilesPath, fileName);

                    if (System.IO.File.Exists(filePath))
                    {
                        try
                        {
                            System.IO.File.Delete(filePath);
                            _logger.LogInformation("Physical file deleted from disk as it's no longer linked: {FileName}", filePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting physical file {FileName} after last database entry deletion: {ErrorMessage}", filePath, ex.Message); // Added ex for full error details
                            // Optionally handle error (e.g., add a message to TempData to show on redirect)
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Physical file not found on disk when attempting cleanup after last database entry deletion: {FileName}", filePath);
                        // The file is already missing from disk, no action needed.
                    }
                }
                else
                {
                    _logger.LogInformation("Physical file {FileName} is still linked by {Count} other database entries. Skipping physical file deletion.", fileName, otherCategoryFiles.Count);
                }
            }
            else
            {
                _logger.LogWarning("Database entry for file ID {FileId} not found for deletion.", id);
                // Optionally handle case where database entry is already missing
            }

            // Redirect back to the ManageCategoryFiles page for the current category
            return RedirectToAction("ManageCategoryFiles", new { categoryId });
        }

        [HttpPost]
        public async Task<IActionResult> RenameFileInCategory(int renameFileId, string oldFileName, string newFileName)
        {
            if (string.IsNullOrWhiteSpace(newFileName) || oldFileName.Equals(newFileName, StringComparison.OrdinalIgnoreCase)) // Used IsNullOrWhiteSpace and case-insensitive comparison
            {
                return BadRequest("New file name cannot be empty or the same as the old file name.");
            }
            // Ensure the new file name ends with .pdf (assuming all managed files are PDFs)
            if (!newFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                newFileName += ".pdf";
            }

            var categoryFile = await _context.CategoryFiles.FindAsync(renameFileId);

            if (categoryFile == null)
            {
                return NotFound();
            }

            var protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");
            var oldFilePath = Path.Combine(protectedFilesPath, oldFileName);
            var newFilePath = Path.Combine(protectedFilesPath, newFileName);

            if (System.IO.File.Exists(oldFilePath))
            {
                try
                {
                    // Check if the new file name already exists
                    if (System.IO.File.Exists(newFilePath))
                    {
                        System.IO.File.Delete(newFilePath);
                        _logger.LogWarning("Existing file deleted before rename: {NewFilePath}", newFilePath);
                    }

                    System.IO.File.Move(oldFilePath, newFilePath);
                    _logger.LogInformation("File renamed from {OldFileName} to {NewFileName}", oldFileName, newFileName);

                    // Update the FileName in the CategoryFile entity
                    categoryFile.FileName = Path.GetFileName(newFileName);
                    await _context.SaveChangesAsync();

                    return Ok(); // Or redirect as needed
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error renaming file {OldFileName} to {NewFileName}: {ErrorMessage}", oldFileName, newFileName, ex.Message);
                    return StatusCode(500, "Error renaming file on the server.");
                }
            }
            else
            {
                _logger.LogWarning("File to rename not found: {OldFileName}", oldFileName);
                return NotFound("Original file not found.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadFileToCategory(int categoryId, IFormFile file, int sortOrder)
        {
            if (file != null && file.Length > 0)
            {
                var category = await _context.PDFCategories.FindAsync(categoryId); // Use FindAsync
                if (category == null)
                {
                    _logger.LogWarning("Upload failed: Category with ID {CategoryId} not found.", categoryId);
                    // Use TempData to show a user-friendly message on redirect
                    TempData["ErrorMessage"] = "Error: Category not found.";
                    // Redirect back to the appropriate page, maybe the category list or a general error page
                    return RedirectToAction("Index", "PdfCategory");
                }

                // Ensure the target directory exists before saving
                // Assuming _environment.ContentRootPath and "ProtectedFiles" are correctly defined elsewhere
                var protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");
                if (!Directory.Exists(protectedFilesPath))
                {
                    try
                    {
                        Directory.CreateDirectory(protectedFilesPath);
                        _logger.LogInformation("Created ProtectedFiles directory during upload: {Path}", protectedFilesPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Upload failed: Error creating ProtectedFiles directory: {ErrorMessage}", ex.Message);
                        TempData["ErrorMessage"] = "Error creating necessary directory on the server.";
                        return RedirectToAction("ManageCategoryFiles", new { categoryId }); // Redirect back on error
                    }
                }

                var originalFileName = Path.GetFileName(file.FileName);
                // Sanitize the filename to prevent invalid characters
                var sanitizedFileName = Path.GetInvalidFileNameChars()
                                        .Aggregate(originalFileName, (current, c) => current.Replace(c.ToString(), "_"));
                //sanitizedFileName = sanitizedFileName.Replace(" ", "_"); // Replace spaces with underscores

                // Ensure the sanitized file name ends with .pdf (assuming all uploaded files are PDFs)
                if (!sanitizedFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // Optionally log a warning or error if a non-PDF is uploaded, depending on desired strictness
                    _logger.LogWarning("Upload attempted for non-PDF file: {FileName}", originalFileName);
                    TempData["ErrorMessage"] = "Only PDF files are allowed for upload.";
                    return RedirectToAction("ManageCategoryFiles", new { categoryId }); // Redirect back
                }

                // Construct the full file path
                var filePath = Path.Combine(protectedFilesPath, sanitizedFileName);

                // --- Removed the file existence check to allow overwriting ---
                //if (System.IO.File.Exists(filePath))
                //{
                //    _logger.LogWarning("Upload failed: File with name {FileName} already exists at {FilePath}", sanitizedFileName, filePath);
                //    return BadRequest($"A file named '{sanitizedFileName}' already exists on the server. Please rename your file or delete the existing one.");
                //}
                // --- End Removed Section ---

                try
                {
                    // Save the file to the server using the sanitized name
                    // FileMode.Create will overwrite the file if it already exists
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }
                    _logger.LogInformation("File uploaded and saved (or overwritten) successfully to {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload failed: Error saving file to {FilePath}: {ErrorMessage}", filePath, ex.Message);
                    TempData["ErrorMessage"] = "Error saving file on the server.";
                    return RedirectToAction("ManageCategoryFiles", new { categoryId }); // Redirect back on error
                }

                // Check if a database entry for this file already exists in this category
                var existingCategoryFile = await _context.CategoryFiles
                                                        .FirstOrDefaultAsync(cf => cf.CategoryID == categoryId && cf.FileName == sanitizedFileName);

                if (existingCategoryFile != null)
                {
                    // If entry exists, update its sort order (or other properties if needed)
                    existingCategoryFile.SortOrder = sortOrder;
                    _logger.LogInformation("Database entry updated for existing file {FileName} in category {CategoryId}.", sanitizedFileName, categoryId);
                }
                else
                {
                    // If entry does not exist, create a new one
                    var categoryFile = new CategoryFile
                    {
                        CategoryID = categoryId,
                        FileName = sanitizedFileName, // Store the sanitized filename
                        SortOrder = sortOrder,
                        PDFCategory = category // Assign the fetched category entity
                    };
                    _context.CategoryFiles.Add(categoryFile);
                    _logger.LogInformation("New database entry created for uploaded file {FileName} in category {CategoryId}.", sanitizedFileName, categoryId);
                }

                try
                {
                    await _context.SaveChangesAsync(); // Save changes (either add new or update existing)
                    TempData["SuccessMessage"] = $"File '{sanitizedFileName}' uploaded and saved successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload failed: Error saving database entry for file {FileName}: {ErrorMessage}", sanitizedFileName, ex.Message);
                    // The file was saved, but the DB entry failed. Consider deleting the saved file here.
                    // try { System.IO.File.Delete(filePath); } catch { _logger.LogError("Failed to delete physical file {FilePath} after DB save failure.", filePath); }
                    TempData["ErrorMessage"] = "Error saving file information to database.";
                }

                // Redirect back to the ManageCategoryFiles page for this category
                return RedirectToAction("ManageCategoryFiles", new { categoryId });
            }

            // If no file was selected, or an error occurred before file processing
            _logger.LogWarning("Upload failed: No file selected or file was empty.");
            TempData["WarningMessage"] = "No file selected for upload."; // Add a user-friendly message
                                                                         // Redirect back to the page
            return RedirectToAction("ManageCategoryFiles", new { categoryId });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFileSortOrder(int renameFileId, int newSortOrder)
        {
            var categoryFile = await _context.CategoryFiles.FindAsync(renameFileId); // Use FindAsync

            if (categoryFile == null)
            {
                _logger.LogWarning("Sort order update failed: File entry with ID {FileId} not found.", renameFileId);
                return NotFound();
            }

            categoryFile.SortOrder = newSortOrder;
            try
            {
                await _context.SaveChangesAsync(); // Use SaveChangesAsync
                _logger.LogInformation("File entry ID {FileId} sort order updated to {NewSortOrder}.", renameFileId, newSortOrder);
                return Ok(); // Or you could return a NoContentResult or redirect if needed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sort order update failed for File ID {FileId}: {ErrorMessage}", renameFileId, ex.Message);
                return StatusCode(500, "Error updating sort order.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetNextSortOrder(int categoryId) // Made async
        {
            // Find the maximum existing SortOrder for the given category
            // Use MaxAsync for safety and consistency if list is large, otherwise Max() is fine on ToList() result
            var maxSortOrder = await _context.CategoryFiles
                                    .Where(f => f.CategoryID == categoryId)
                                    .MaxAsync(f => (int?)f.SortOrder); // Use MaxAsync and (int?)

            // If maxSortOrder is null (no files yet), the next sort order is 1. Otherwise, it's the max + 1.
            int nextSortOrder = (maxSortOrder ?? 0) + 1;

            return Json(nextSortOrder);
        }

        // Made async as it calls an async method (implicitly by EF Core internally with ToList)
        public async Task<IActionResult> CategoryFilesPartial(int categoryId)
        {
            var categoryFiles = await _context.CategoryFiles // Use await and ToListAsync
                .Where(cf => cf.CategoryID == categoryId)
                 .OrderBy(cf => cf.SortOrder) // Order by SortOrder for consistent listing
                .ToListAsync();
            ViewBag.CategoryId = categoryId;
            return PartialView("_CategoryFilesPartial", categoryFiles); // Assumes a partial view named _CategoryFilesPartial
        }
    }
}