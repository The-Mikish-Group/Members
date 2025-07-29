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

        public ColorManagementModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ColorVar>? ColorVars { get; set; }

        public async Task OnGetAsync()
        {
            ColorVars = await _context.ColorVars.ToListAsync();
        }

        public async Task<IActionResult> OnPostAsync(Dictionary<string, string> colors)
        {
            foreach (var color in colors)
            {
                if (Regex.IsMatch(color.Value, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$"))
                {
                    var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == color.Key);
                    if (colorVar != null)
                    {
                        colorVar.Value = color.Value;
                    }
                    else
                    {
                        _context.ColorVars.Add(new ColorVar { Name = color.Key, Value = color.Value });
                    }
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            var colors = await _context.ColorVars.ToListAsync();
            var builder = new StringBuilder();
            builder.AppendLine("Name,Value");
            foreach (var color in colors)
            {
                builder.AppendLine($"{color.Name},{color.Value}");
            }

            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", "colors.csv");
        }

        public async Task<IActionResult> OnPostImportAsync(IFormFile csvFile)
        {
            if (csvFile != null && csvFile.Length > 0)
            {
                using (var reader = new StreamReader(csvFile.OpenReadStream()))
                {
                    var header = await reader.ReadLineAsync(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line != null)
                        {
                            var values = line.Split(',');
                            if (values.Length == 2)
                            {
                                var name = values[0];
                                var value = values[1];
                                if (Regex.IsMatch(value, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$"))
                                {
                                    var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == name);
                                    if (colorVar != null)
                                    {
                                        colorVar.Value = value;
                                    }
                                    else
                                    {
                                        _context.ColorVars.Add(new ColorVar { Name = name, Value = value });
                                    }
                                }
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToPage();
        }
    }
}
