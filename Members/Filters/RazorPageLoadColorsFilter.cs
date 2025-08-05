using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Members.Data;

namespace Members.Filters
{
    public class RazorPageLoadColorsFilter(ApplicationDbContext context) : IAsyncPageFilter
    {
        private readonly ApplicationDbContext _context = context;

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
        {
            return Task.CompletedTask;
        }

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            try
            {
                // Check if we have a valid context
                if (_context?.ColorVars == null)
                {
                    // Skip color loading if context is not available
                    await next();
                    return;
                }

                // Load colors and handle duplicates in memory to avoid null warnings
                var allColors = await _context.ColorVars.ToListAsync();
                var colorQuery = allColors
                    .GroupBy(c => c.Name)
                    .ToDictionary(g => g.Key, g => g.First().Value);

                if (context.HandlerInstance is PageModel pageModel)
                {
                    pageModel.ViewData["DynamicColors"] = colorQuery;
                }
            }
            catch (Exception ex)
            {
                // Log the exception if possible and continue without colors
                System.Diagnostics.Debug.WriteLine($"RazorPageLoadColorsFilter error: {ex.Message}");

                if (context.HandlerInstance is PageModel pageModel)
                {
                    pageModel.ViewData["DynamicColors"] = new Dictionary<string, string>();
                }
            }

            await next();
        }
    }
}