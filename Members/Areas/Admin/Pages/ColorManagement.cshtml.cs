using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Members.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    public class ColorManagementModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ColorManagementModel> _logger;

        public ColorManagementModel(ApplicationDbContext context, ILogger<ColorManagementModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        public IList<ColorVar>? ColorVars { get; set; }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGetAsync called - Loading color management page");
            ColorVars = await _context.ColorVars.ToListAsync();
            _logger.LogInformation($"Loaded {ColorVars?.Count ?? 0} color variables");
        }

        public async Task<IActionResult> OnPostAsync(Dictionary<string, string> colors)
        {
            _logger.LogInformation("OnPostAsync called - Saving colors");

            if (colors == null)
            {
                _logger.LogWarning("Colors dictionary is null, redirecting");
                return RedirectToPage();
            }

            _logger.LogInformation($"Received {colors.Count} colors to save");

            foreach (var color in colors)
            {
                // Skip system fields
                if (color.Key.Equals("__RequestVerificationToken", StringComparison.OrdinalIgnoreCase) ||
                    color.Key.Equals("handler", StringComparison.OrdinalIgnoreCase) ||
                    color.Key.Equals("csvFile", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Regex.IsMatch(color.Value, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$"))
                {
                    var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == color.Key);
                    if (colorVar != null)
                    {
                        colorVar.Value = color.Value;
                        _logger.LogInformation($"Updated color {color.Key} to {color.Value}");
                    }
                    else
                    {
                        _context.ColorVars.Add(new ColorVar { Name = color.Key, Value = color.Value });
                        _logger.LogInformation($"Added new color {color.Key} with value {color.Value}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Invalid color format for {color.Key}: {color.Value}");
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Colors saved successfully");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnGetExportCsvAsync()
        {
            _logger.LogInformation("=== OnGetExportCsvAsync called! ===");

            try
            {
                var colors = await _context.ColorVars.ToListAsync();
                _logger.LogInformation($"Retrieved {colors?.Count ?? 0} colors from database");

                var builder = new StringBuilder();
                builder.AppendLine("Name,Value");

                if (colors != null && colors.Any())
                {
                    foreach (var color in colors)
                    {
                        // Debug: Log the actual values from database
                        _logger.LogInformation($"Processing color - Name: '{color.Name}', Value: '{color.Value}'");

                        var colorName = color.Name ?? "Unknown";
                        var colorValue = color.Value ?? "#000000";

                        var line = $"{colorName},{colorValue}";
                        builder.AppendLine(line);
                        _logger.LogInformation($"Added to CSV: {line}");
                    }
                }
                else
                {
                    _logger.LogWarning("No colors found in database");
                }

                var fileName = $"colors_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                var content = builder.ToString();
                var buffer = Encoding.UTF8.GetBytes(content);

                _logger.LogInformation($"Generated CSV file via GET: {fileName}");
                _logger.LogInformation($"Content size: {buffer.Length} bytes");

                return File(buffer, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnGetExportCsvAsync: {Message}", ex.Message);
                _logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
                TempData["ErrorMessage"] = $"Failed to export colors: {ex.Message}";
                return RedirectToPage();
            }
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }

        public async Task<IActionResult> OnPostImport(IFormFile csvFile)
        {
            _logger.LogInformation("OnPostImportAsync called");

            if (csvFile == null || csvFile.Length == 0)
            {
                _logger.LogWarning("No CSV file provided for import");
                TempData["ErrorMessage"] = "Please select a valid CSV file.";
                return RedirectToPage();
            }

            try
            {
                _logger.LogInformation($"Processing CSV file: {csvFile.FileName}, Size: {csvFile.Length}");

                var importCount = 0;
                var errorCount = 0;

                using (var reader = new StreamReader(csvFile.OpenReadStream()))
                {
                    var header = await reader.ReadLineAsync(); // Skip header
                    _logger.LogInformation($"CSV header: {header}");

                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        _logger.LogInformation($"Processing line: {line}");

                        // Simple split since your CSV doesn't have quoted fields with commas
                        var values = line.Split(',');
                        if (values.Length >= 2)
                        {
                            var name = values[0].Trim();
                            var value = values[1].Trim();

                            _logger.LogInformation($"Parsed - Name: '{name}', Value: '{value}'");

                            if (!string.IsNullOrEmpty(name) && Regex.IsMatch(value, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$"))
                            {
                                var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == name);
                                if (colorVar != null)
                                {
                                    _logger.LogInformation($"Updating existing color {name} from {colorVar.Value} to {value}");
                                    colorVar.Value = value;
                                }
                                else
                                {
                                    _logger.LogInformation($"Adding new color {name} with value {value}");
                                    _context.ColorVars.Add(new ColorVar { Name = name, Value = value });
                                }
                                importCount++;
                            }
                            else
                            {
                                _logger.LogWarning($"Invalid color data in import: Name='{name}', Value='{value}'");
                                errorCount++;
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid line format: {line}");
                            errorCount++;
                        }
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Import completed - {importCount} imported, {errorCount} errors");

                if (importCount > 0)
                {
                    TempData["SuccessMessage"] = $"Successfully imported {importCount} colors.";
                }
                if (errorCount > 0)
                {
                    TempData["WarningMessage"] = $"{errorCount} rows were skipped due to invalid format.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CSV import");
                TempData["ErrorMessage"] = $"Error importing file: {ex.Message}";
            }

            return RedirectToPage();
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result.ToArray();
        }
    }
}