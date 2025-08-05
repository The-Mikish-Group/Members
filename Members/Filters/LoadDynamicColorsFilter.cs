using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Members.Data;

namespace Members.Filters
{
    public class LoadDynamicColorsFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _context;

        public LoadDynamicColorsFilter(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                // Use GroupBy to handle any remaining duplicates in the query
                var colorQuery = await _context.ColorVars
                    .GroupBy(c => c.Name)
                    .Select(g => new { Name = g.Key, Value = g.FirstOrDefault().Value })
                    .ToDictionaryAsync(c => c.Name, c => c.Value);

                if (context.Controller is Controller controller)
                {
                    controller.ViewBag.DynamicColors = colorQuery;
                    
                }
            }
            catch (Exception ex)
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