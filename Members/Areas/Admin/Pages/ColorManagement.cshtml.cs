using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
                    var name = "--" + color.Key;
                    var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == name);
                    if (colorVar != null)
                    {
                        colorVar.Value = color.Value;
                    }
                    else
                    {
                        _context.ColorVars.Add(new ColorVar { Name = name, Value = color.Value });
                    }
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToPage();
        }
    }
}
