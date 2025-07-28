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
    public class ColorsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ColorsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<Dictionary<string, string>>> GetColors()
        {
            var colors = await _context.ColorVars.ToListAsync();
            return colors.ToDictionary(c => c.Name, c => c.Value);
        }
    }
}
