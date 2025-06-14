using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Members.Areas.Identity.Pages
{
    [Authorize(Roles = "Admin")]
    public class AdminFilesModel : PageModel
    {
        private readonly string _protectedFilesPath;
        private readonly ILogger<AdminFilesModel> _logger;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public List<string> SelectedFiles { get; set; } = []; // Initialize to avoid null

        [BindProperty]
        public IFormFile UploadFile { get; set; } = null!; // Use null-forgiving operator as it will be set later

        //[BindProperty]
        public string? NewFileName { get; set; }

        public List<string?> Files { get; set; } = []; // Initialize to avoid null

        public string? Message { get; set; }

        public string? MessageType { get; set; } = "alert-info";

        public AdminFilesModel(IWebHostEnvironment environment, ILogger<AdminFilesModel> logger)
        {
            _environment = environment;
            _protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            Files = await Task.Run(() => Directory.GetFiles(_protectedFilesPath)
                                                .Select(fileName => (string?)Path.GetFileName(fileName))
                                                .OrderBy(f => f)
                                                .ToList());
            _logger.LogInformation("WorkingAdminPage - _privateFilesPath: {ProtectedFilesPath}", _protectedFilesPath);
        }

        public async Task<IActionResult> OnPostDeleteSingleAsync(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                Message = "Error: File name for deletion is missing.";
                MessageType = "alert-danger";
                return RedirectToPage();
            }

            var filePath = Path.Combine(_protectedFilesPath, fileName);

            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    await Task.Run(() => System.IO.File.Delete(filePath));
                    _logger.LogInformation("Admin deleted file: {FileName}", fileName);
                    Message = $"File '{fileName}' deleted successfully.";
                    MessageType = "alert-success";
                }
                else
                {
                    Message = $"Error: File '{fileName}' not found.";
                    MessageType = "alert-danger";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error deleting file {FileName}: {ErrorMessage}", fileName, ex.Message);
                Message = $"Error deleting file '{fileName}': {ex.Message}";
                MessageType = "alert-danger";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            if (SelectedFiles != null && SelectedFiles.Count != 0)
            {
                foreach (var fileName in SelectedFiles)
                {
                    var filePath = Path.Combine(_protectedFilesPath, fileName);
                    try
                    {
                        if (System.IO.File.Exists(filePath))
                        {
                            await Task.Run(() => System.IO.File.Delete(filePath));
                            _logger.LogInformation("Admin deleted file: {FileName}", fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error deleting file {FileName}: {ErrorMessage}", fileName, ex.Message);
                        Message = $"Error deleting file '{fileName}': {ex.Message}";
                        MessageType = "alert-danger";
                        return Page();
                    }
                }
                Message = "Selected files deleted successfully.";
                MessageType = "alert-success";
            }
            else
            {
                Message = "No files selected for deletion.";
                MessageType = "alert-warning";
            }

            return RedirectToPage();
        }

        // We can remove the OnPostRename method as the initial action is handled by JavaScript

        //public async Task<IActionResult> OnPostUpdateRenameAsync(string oldFileName, string newFileName)
        //{
        //    if (string.IsNullOrEmpty(oldFileName) || string.IsNullOrEmpty(newFileName))
        //    {
        //        return new JsonResult(false); // Indicate failure
        //    }

        //    var oldPath = Path.Combine(_protectedFilesPath, oldFileName);
        //    var fileExtension = ".pdf"; // Assuming .pdf extension

        //    if (!newFileName.ToLower().EndsWith(fileExtension))
        //    {
        //        newFileName += fileExtension;
        //    }

        //    var newPath = Path.Combine(_protectedFilesPath, newFileName);

        //    if (System.IO.File.Exists(oldPath))
        //    {
        //        try
        //        {
        //            await Task.Run(() => System.IO.File.Move(oldPath, newPath));
        //            _logger.LogInformation("File renamed from {OldFileName} to {NewFileName}", oldFileName, newFileName);
        //            return new JsonResult(true); // Indicate success
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError("Error renaming file {OldFileName} to {NewFileName}: {ErrorMessage}", oldFileName, newFileName, ex.Message);
        //            return new JsonResult(false); // Indicate failure
        //        }
        //    }
        //    else
        //    {
        //        _logger.LogWarning("File to rename not found: {OldFileName}", oldFileName);
        //        return new JsonResult(false); // Indicate failure - old file not found
        //    }
        //}

        //public async Task<IActionResult> OnPostUploadAsync()
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        Message = "Please correct the errors in the form.";
        //        MessageType = "alert-warning";
        //        return Page();
        //    }

        //    if (Request.Form.Files.Count > 0)
        //    {
        //        var file = Request.Form.Files[0];
        //        if (file != null && file.Length > 0)
        //        {
        //            try
        //            {
        //                var filePath = Path.Combine(_protectedFilesPath, file.FileName);
        //                using (var fileStream = new FileStream(filePath, FileMode.Create))
        //                {
        //                    await file.CopyToAsync(fileStream);
        //                }

        //                Message = "File uploaded successfully."; 
        //                MessageType = "alert-success";
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogError(ex, "Error uploading file");
        //                Message = "Error uploading file. Please try again."; 
        //                MessageType = "alert-danger";
        //            }
        //        }
        //        else
        //        {
        //            Message = "Please select a file to upload."; 
        //            MessageType = "alert-warning";
        //        }
        //    }
        //    else
        //    {
        //        Message = "No file was selected for upload."; 
        //        MessageType = "alert-warning";
        //    }

        //    return RedirectToPage();
        //}
    }
}