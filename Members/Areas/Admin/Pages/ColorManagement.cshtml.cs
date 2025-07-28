using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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

        public async Task<IActionResult> OnPostAsync()
        {
            foreach (var key in Request.Form.Keys)
            {
                if (key.StartsWith("colors["))
                {
                    var name = key.Substring(7, key.Length - 8);
                    var colorVar = await _context.ColorVars.FirstOrDefaultAsync(c => c.Name == name);
                    if (colorVar != null)
                    {
                        colorVar.Value = Request.Form[key]!;
                    }
                    else
                    {
                        _context.ColorVars.Add(new ColorVar { Name = name, Value = Request.Form[key]! });
                    }
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToPage();
        }
    }
}
