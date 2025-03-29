using Members.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Members.Controllers
{
    public class AdminController(UserService userService) : Controller
    {
        private readonly UserService _userService = userService;

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ImportUsers()
        {
            await _userService.ImportUsersFromImportFileAsync();
            ViewBag.Message = "User import process initiated. Check the console for details.";
            return View();
        }
    }
}
