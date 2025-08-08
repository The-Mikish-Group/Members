using Members.Models;
using Members.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Members.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin,Manager")]
    public class TasksManagementModel : PageModel
    {
        private readonly ITaskManagementService _taskService;
        private readonly UserManager<IdentityUser> _userManager;

        public TasksManagementModel(ITaskManagementService taskService, UserManager<IdentityUser> userManager)
        {
            _taskService = taskService;
            _userManager = userManager;
        }

        public List<TaskStatusViewModel> Tasks { get; set; } = new();
        public TasksSummaryViewModel TasksSummary { get; set; } = new();
        public List<SelectListItem>? UserSelectList { get; set; }

        public async Task OnGetAsync()
        {
            Console.WriteLine("=== OnGetAsync DEBUG ===");

            Tasks = await _taskService.GetCurrentTaskStatusAsync();
            Console.WriteLine($"Retrieved {Tasks.Count} tasks from service");

            foreach (var task in Tasks)
            {
                Console.WriteLine($"Task {task.TaskID}: {task.TaskName} - Status: {task.ComputedStatus}");
            }

            UserSelectList = await GetAdminManagerUsersAsync();
            Console.WriteLine($"Retrieved {UserSelectList.Count} users for assignment");

            // Calculate summary
            TasksSummary = new TasksSummaryViewModel
            {
                TotalCount = Tasks.Count,
                CompletedCount = Tasks.Count(t => t.ComputedStatus == "Completed"),
                OverdueCount = Tasks.Count(t => t.ComputedStatus == "Overdue"),
                DueNowCount = Tasks.Count(t => t.ComputedStatus == "Due Now")
            };

            Console.WriteLine($"Summary - Total: {TasksSummary.TotalCount}, Completed: {TasksSummary.CompletedCount}, Overdue: {TasksSummary.OverdueCount}, Due Now: {TasksSummary.DueNowCount}");
        }

        public async Task<IActionResult> OnPostCompleteTaskAsync([FromForm] int taskId)
        {
            // Debug logging
            Console.WriteLine($"=== COMPLETE TASK DEBUG ===");
            Console.WriteLine($"TaskId: {taskId}");

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Unable to identify user.";
                return RedirectToPage();
            }

            if (taskId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid task ID.";
                return RedirectToPage();
            }

            var success = await _taskService.CompleteTaskAsync(taskId, userId);

            if (success)
            {
                TempData["StatusMessage"] = "Task marked as completed successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to complete task. Please try again.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAssignTaskAsync([FromForm] int taskId, [FromForm] string assignToUserId)
        {
            // Debug logging to see what we received
            //Console.WriteLine($"=== ASSIGN TASK DEBUG ===");
            //Console.WriteLine($"TaskId: {taskId}");
            //Console.WriteLine($"AssignToUserId: {assignToUserId ?? "NULL"}");

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Unable to identify user.";
                return RedirectToPage();
            }

            if (taskId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid task ID.";
                return RedirectToPage();
            }

            if (string.IsNullOrEmpty(assignToUserId))
            {
                TempData["ErrorMessage"] = "Please select a user to assign the task to.";
                return RedirectToPage();
            }

            var success = await _taskService.AssignTaskAsync(taskId, assignToUserId, userId);

            if (success)
            {
                var assignedUser = await _userManager.FindByIdAsync(assignToUserId);
                var assignedUserName = assignedUser?.Email ?? "Unknown User";
                TempData["StatusMessage"] = $"Task assigned to {assignedUserName} successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to assign task. Please try again.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDismissReminderAsync()
        {
            // Debug logging
            Console.WriteLine($"=== DISMISS REMINDER DEBUG ===");

            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                await _taskService.DismissTaskReminderAsync(userId);
                Console.WriteLine($"Dismissed reminder for user: {userId}");
            }
            else
            {
                Console.WriteLine("User ID was null or empty");
            }

            return new JsonResult(new { success = true });
        }

        private async Task<List<SelectListItem>> GetAdminManagerUsersAsync()
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var managerUsers = await _userManager.GetUsersInRoleAsync("Manager");

            // Combine and deduplicate users (in case someone has both roles)
            var allUsers = adminUsers.Union(managerUsers).Distinct().ToList();

            var userSelectList = new List<SelectListItem>();

            foreach (var user in allUsers.OrderBy(u => u.Email))
            {
                // Use email as display name, or UserName if email is not available
                var displayName = !string.IsNullOrEmpty(user.Email) ? user.Email : user.UserName;

                userSelectList.Add(new SelectListItem
                {
                    Value = user.Id,
                    Text = displayName
                });
            }

            return userSelectList;
        }
    }

    public class TasksSummaryViewModel
    {
        public int TotalCount { get; set; }
        public int CompletedCount { get; set; }
        public int OverdueCount { get; set; }
        public int DueNowCount { get; set; }
    }
}