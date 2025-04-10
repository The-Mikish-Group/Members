using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class AdminFilesModel : PageModel
    {
        private readonly string _protectedFilesPath;
        private readonly ILogger<AdminFilesModel> _logger;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public List<string> SelectedFiles { get; set; } = [];

        [BindProperty]
        public IFormFile? UploadFile { get; set; }

        [BindProperty]
        public string? NewFileName { get; set; }

        public List<string> Files { get; set; } = [];

        public string? Message { get; set; }

        public string MessageType { get; set; } = "alert-info";

        public AdminFilesModel(IWebHostEnvironment environment, ILogger<AdminFilesModel> logger)
        {
            _environment = environment;
            _protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            Files = await Task.Run(() => Directory.GetFiles(_protectedFilesPath)
                                             .Select(fileName => Path.GetFileName(fileName)!)
                                             .OrderBy(f => f)
                                             .ToList());
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

        public IActionResult OnPostRename()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateRenameAsync(string oldFileName, string newFileName)
        {
            if (string.IsNullOrEmpty(oldFileName) || string.IsNullOrEmpty(newFileName))
            {
                Message = "Both old and new file names must be provided.";
                MessageType = "alert-warning";
                return Page();
            }

            var oldPath = Path.Combine(_protectedFilesPath, oldFileName);
            var fileExtension = ".pdf";

            // Ensure the new file name has the .pdf extension if it doesn't already
            if (!newFileName.ToLower().EndsWith(fileExtension))
            {
                newFileName += fileExtension;
            }

            var newPath = Path.Combine(_protectedFilesPath, newFileName);

            if (System.IO.File.Exists(oldPath))
            {
                try
                {
                    // Use Task.Run to perform the file operation asynchronously
                    await Task.Run(() => System.IO.File.Move(oldPath, newPath));
                    Message = $"File '{oldFileName}' successfully renamed to '{newFileName}'.";
                    MessageType = "alert-success";
                }
                catch (Exception ex)
                {
                    Message = $"Error renaming file: {ex.Message}";
                    MessageType = "alert-danger";
                }
            }
            else
            {
                Message = $"Error: File '{oldFileName}' not found.";
                MessageType = "alert-danger";
            }

            return RedirectToPage("./AdminFiles");
        }
        public async Task<IActionResult> OnPostUploadAsync()
        {
            if (UploadFile != null && UploadFile.Length > 0)
            {
                var fileName = Path.GetFileName(UploadFile.FileName);
                var filePath = Path.Combine(_protectedFilesPath, fileName);

                try
                {
                    await Task.Run(async () =>
                    {
                        using var stream = new FileStream(filePath, FileMode.Create);
                        await UploadFile.CopyToAsync(stream);
                    });
                    _logger.LogInformation("Admin uploaded file: {FileName}", fileName); // Fixed CA2254
                    Message = $"File '{fileName}' uploaded successfully.";
                    MessageType = "alert-success";
                    return RedirectToPage();
                }
                catch (Exception ex)
                {
                    Message = $"Error uploading file '{fileName}': {ex.Message}";
                    MessageType = "alert-danger";
                }
            }
            else
            {
                Message = "Please select a file to upload.";
                MessageType = "alert-warning";
            }

            return Page();
        }
    }
}