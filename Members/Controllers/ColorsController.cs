using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ColorsController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

        [HttpGet]
        public async Task<ActionResult<Dictionary<string, string>>> GetColors()
        {
            try
            {
                var colors = await _context.ColorVars.ToListAsync();
                return colors.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Value);
            }
            catch (System.Exception)
            {
                return new StatusCodeResult(500);
            }
        }
    }
}
