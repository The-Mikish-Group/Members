using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

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
    }
}