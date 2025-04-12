using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Members.Pages.Admin
{
    [Authorize(Roles = "Admin,Manager")]
    public partial class DocumentsModel : PageModel
    {
        private readonly string _protectedFilesPath;
        private readonly ILogger<DocumentsModel> _logger;
        private readonly IWebHostEnvironment _environment;

        public List<DocumentInfo> Documents { get; set; } = [];

        public DocumentsModel(IWebHostEnvironment environment, ILogger<DocumentsModel> logger)
        {
            _environment = environment;
            _protectedFilesPath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation($"DocumentsModel - _privateFilesPath: {_protectedFilesPath}"); // Log the path

            if (!Directory.Exists(_protectedFilesPath))
            {
                _logger.LogError("Private files directory not found: {Path}", _protectedFilesPath);
                return;
            }

            // Use Task.Run to make the file processing asynchronous
            var files = await Task.Run(() =>
            {
                return Directory.GetFiles(_protectedFilesPath)
                    .Select(Path.GetFileName)
                    .Where(fileName => !string.IsNullOrEmpty(fileName))
                    .Where(fileName => DocumentFileRegex().IsMatch(fileName!))
                    .OrderBy(fileName => fileName)
                    .ToList();
            });

            Documents = files.Select(fileName => new DocumentInfo
            {
                FileName = fileName!, // Store the full filename here
                DisplayName = fileName! // Use the full filename for display as well (no truncation)
            }).ToList();

            // Log the contents of the Documents list
            if (Documents != null)
            {
                _logger.LogInformation("DocumentsModel - Found {Count} documents.", Documents.Count);
                foreach (var doc in Documents)
                {
                    _logger.LogInformation($"  FileName: {doc.FileName}, DisplayName: {doc.DisplayName}");
                }
            }
            else
            {
                _logger.LogInformation("DocumentsModel - Documents list is null.");
            }
        }
        public class DocumentInfo
        {
            public string FileName { get; set; } = ""; // Initialize to an empty string to satisfy non-nullable requirement
            public string DisplayName { get; set; } = ""; // Initialize to an empty string
        }

        [GeneratedRegex(@"^\d{3}-.*\.pdf$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex DocumentFileRegex();
    }
}