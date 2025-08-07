using Members.Data;
using Members.Models;
using Members.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Members.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin,Manager")]
    public class TasksModel : PageModel
    {
        private readonly ITaskManagementService _taskService;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TasksModel> _logger;

        public TasksModel(
            ITaskManagementService taskService,
            UserManager<IdentityUser> userManager,
            ApplicationDbContext context,
            ILogger<TasksModel> logger)
        {
            _taskService = taskService;
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        public List<TaskStatusViewModel> Tasks { get; set; } = new();
        public TasksSummaryViewModel TasksSummary { get; set; } = new();
        public SelectList? UserSelectList { get; set; }

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        public async Task<IActionResult> OnPostCompleteTaskAsync(int taskId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "Unable to identify current user.";
                    return RedirectToPage();
                }

                var success = await _taskService.CompleteTaskAsync(taskId, user.Id, "Manually marked as completed", false);

                if (success)
                {
                    TempData["StatusMessage"] = "Task marked as completed successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to complete task. Please try again.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing task {TaskId}", taskId);
                TempData["ErrorMessage"] = "An error occurred while completing the task.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDismissReminderAsync()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    await _taskService.DismissTaskReminderAsync(user.Id);
                }
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dismissing task reminder");
                return new JsonResult(new { success = false });
            }
        }

        public async Task<IActionResult> OnPostAssignTaskAsync(int taskId, string assignToUserId)
        {
            try
            {
                if (string.IsNullOrEmpty(assignToUserId))
                {
                    TempData["ErrorMessage"] = "Please select a user to assign the task to.";
                    return RedirectToPage();
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "Unable to identify current user.";
                    return RedirectToPage();
                }

                var success = await _taskService.AssignTaskAsync(taskId, assignToUserId, user.Id);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning task {TaskId} to user {AssignToUserId}", taskId, assignToUserId);
                TempData["ErrorMessage"] = "An error occurred while assigning the task.";
            }

            return RedirectToPage();
        }

        private async Task LoadDataAsync()
        {
            // Load tasks
            Tasks = await _taskService.GetCurrentTaskStatusAsync();

            // Calculate summary
            TasksSummary = new TasksSummaryViewModel
            {
                TotalCount = Tasks.Count,
                CompletedCount = Tasks.Count(t => t.ComputedStatus == "Completed"),
                OverdueCount = Tasks.Count(t => t.ComputedStatus == "Overdue"),
                DueNowCount = Tasks.Count(t => t.ComputedStatus == "Due Now"),
                PendingCount = Tasks.Count(t => t.ComputedStatus == "Pending")
            };

            // Load user select list (Admin and Manager users only)
            await PopulateUserSelectListAsync();
        }

        private async Task PopulateUserSelectListAsync()
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var managerUsers = await _userManager.GetUsersInRoleAsync("Manager");

            var allUsers = adminUsers.Union(managerUsers).Distinct().ToList();

            var userProfiles = await _context.UserProfile
                .Where(up => allUsers.Select(u => u.Id).Contains(up.UserId))
                .ToDictionaryAsync(up => up.UserId);

            var userListItems = new List<SelectListItem>();

            foreach (var user in allUsers.OrderBy(u => u.Email))
            {
                var displayText = user.Email;
                if (userProfiles.TryGetValue(user.Id, out var profile) &&
                    !string.IsNullOrWhiteSpace(profile.FirstName) &&
                    !string.IsNullOrWhiteSpace(profile.LastName))
                {
                    displayText = $"{profile.FirstName} {profile.LastName} ({user.Email})";
                }

                userListItems.Add(new SelectListItem
                {
                    Value = user.Id,
                    Text = displayText
                });
            }

            UserSelectList = new SelectList(userListItems, "Value", "Text");
        }
    }

    public class TasksSummaryViewModel
    {
        public int TotalCount { get; set; }
        public int CompletedCount { get; set; }
        public int OverdueCount { get; set; }
        public int DueNowCount { get; set; }
        public int PendingCount { get; set; }
    }
}