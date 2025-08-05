using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Members.Data;

namespace Members.Filters
{
    public class LoadDynamicColorsFilter(ApplicationDbContext context) : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _context = context;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
{
    try
    {
        // Load colors and handle duplicates in memory to avoid null warnings
        var allColors = await _context.ColorVars.ToListAsync();
        var colorQuery = allColors
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First().Value);

        if (context.Controller is Controller controller)
        {
            controller.ViewBag.DynamicColors = colorQuery;
        }
    }
    catch (Exception)
    {
        // Set empty dictionary as fallback
        if (context.Controller is Controller controller)
        {
            controller.ViewBag.DynamicColors = new Dictionary<string, string>();
        }
    }

    await next();
}
    }
}