using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;
using Members.Models;

namespace Members.Controllers
{
    public class FileController : Controller
    {
        private readonly string _protectedFilesPath;
        private readonly ILogger<FileController> _logger;

        public FileController(IWebHostEnvironment env, ILogger<FileController> logger)
        {
            _protectedFilesPath = Path.Combine(env.ContentRootPath, "ProtectedFiles");
            _logger = logger;
            _logger.LogInformation("ProtectedFilesPath: {ProtectedFilesPath}", _protectedFilesPath);
        }

        private string GetFilePath(string fileName)
        {
            return Path.Combine(_protectedFilesPath, fileName);
        }

        public IActionResult DownloadPdf(string fileName)
        {
            var filePath = GetFilePath(fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound($"File not found: {fileName}");
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, "application/pdf", fileName);
        }

        public IActionResult ViewPdf(string fileName)
        {
            var filePath = GetFilePath(fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound($"File not found: {fileName}");
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, "application/pdf");
        }

        // Existing action for Budget and Financial Files
        public IActionResult GetBudgetFinancialFiles()
        {
            if (!Directory.Exists(_protectedFilesPath))
            {
                _logger.LogError("Protected files directory not found: {Path}", _protectedFilesPath);
                return Json(new List<DocumentInfo>());
            }

            var files = Directory.GetFiles(_protectedFilesPath)
                                 .Where(file => Path.GetFileName(file).StartsWith("Budget") || Path.GetFileName(file).StartsWith("Financial"))
                                 .OrderBy(Path.GetFileName)
                                 .Select(filePath => new DocumentInfo
                                 {
                                     FileName = Path.GetFileName(filePath),
                                     DisplayName = Path.GetFileNameWithoutExtension(filePath)
                                                        .Replace("Budget Report", "Budget Report")
                                                        .Replace("Financial Report", "Financial Report")
                                                        .Trim()
                                 })
                                 .ToList();

            return Json(files);
        }

        // New action to get the list of Minutes files
        public IActionResult GetMinutesFiles()
        {
            if (!Directory.Exists(_protectedFilesPath))
            {
                _logger.LogError("Protected files directory not found: {Path}", _protectedFilesPath);
                return Json(new List<DocumentInfo>());
            }

            var files = Directory.GetFiles(_protectedFilesPath)
                                 .Where(file => Path.GetFileName(file).StartsWith("Minutes") || Path.GetFileName(file).StartsWith("Agenda"))
                                 .OrderBy(Path.GetFileName)
                                 .Select(filePath => new DocumentInfo
                                 {
                                     FileName = Path.GetFileName(filePath),
                                     DisplayName = Path.GetFileNameWithoutExtension(filePath)
                                                        .Replace("Minutes", "Minutes")
                                                        .Replace("Agenda", "Agenda")
                                                        .Trim()
                                 })
                                 .ToList();

            return Json(files);
        }
    }
}