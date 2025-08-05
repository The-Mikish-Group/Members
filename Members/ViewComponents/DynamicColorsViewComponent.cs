using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Members.Data;

namespace Members.ViewComponents
{
    [ViewComponent(Name = "DynamicColors")]
    public class DynamicColorsViewComponent(ApplicationDbContext context) : ViewComponent
    {
        private readonly ApplicationDbContext _context = context;

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var dynamicColors = await _context.ColorVars.ToDictionaryAsync(c => c.Name, c => c.Value);
                return View(dynamicColors);
            }
            catch (Exception)
            {
                // Log the exception if you have logging set up
                // Return empty dictionary as fallback
                return View(new Dictionary<string, string>());
            }
        }
    }
}
