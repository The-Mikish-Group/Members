using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Members.Data;

namespace Members.Filters
{
    public class RazorPageLoadColorsFilter : IAsyncPageFilter
    {
        private readonly ApplicationDbContext _context;

        public RazorPageLoadColorsFilter(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
        {
            return Task.CompletedTask;
        }

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            try
            {
                var colorQuery = await _context.ColorVars
                    .GroupBy(c => c.Name)
                    .Select(g => new { Name = g.Key, Value = g.FirstOrDefault().Value })
                    .ToDictionaryAsync(c => c.Name, c => c.Value);

                if (context.HandlerInstance is PageModel pageModel)
                {
                    pageModel.ViewData["DynamicColors"] = colorQuery;
                    
                }
            }
            catch (Exception ex)
            {
                if (context.HandlerInstance is PageModel pageModel)
                {
                    pageModel.ViewData["DynamicColors"] = new Dictionary<string, string>();
                    
                }
            }

            await next();
        }
    }
}