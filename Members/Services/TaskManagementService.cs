using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Members.Models.TaskStatus;

namespace Members.Services
{
    public interface ITaskManagementService
    {
        Task<List<TaskStatusViewModel>> GetCurrentTaskStatusAsync();
        Task<bool> CompleteTaskAsync(int taskId, string userId, string? notes = null, bool isAutomated = false);
        Task<bool> AssignTaskAsync(int taskId, string assignToUserId, string assignedByUserId);
        Task<bool> ShouldShowTaskReminderAsync(string userId);
        Task DismissTaskReminderAsync(string userId);
        Task<bool> HasOverdueTasksAsync();
        Task<bool> HasTasksDueNowAsync();
        Task InitializeTaskInstancesAsync();
        Task<bool> IsTaskCompletedThisMonthAsync(string taskHandler);
        Task<AdminTaskInstance?> GetTaskInstanceAsync(int taskId, int year, int month);
        Task<bool> MarkTaskCompletedAutomaticallyAsync(string actionHandler, string? notes = null);
    }

    public class TaskManagementService : ITaskManagementService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<TaskManagementService> _logger;

        public TaskManagementService(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<TaskManagementService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<List<TaskStatusViewModel>> GetCurrentTaskStatusAsync()
        {
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;
            var currentDay = DateTime.Now.Day;

            // Ensure task instances exist for current month
            await InitializeTaskInstancesAsync();

            var tasks = await _context.AdminTasks
                .Where(t => t.IsActive)
                .Include(t => t.TaskInstances.Where(ti => ti.Year == currentYear && ti.Month == currentMonth))
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.DayOfMonthStart)
                .ToListAsync();

            var result = new List<TaskStatusViewModel>();

            foreach (var task in tasks)
            {
                var instance = task.TaskInstances.FirstOrDefault();
                var computedStatus = ComputeTaskStatus(task, instance, currentDay);
                var sortPriority = ComputeSortPriority(computedStatus, task, currentDay);

                result.Add(new TaskStatusViewModel
                {
                    TaskID = task.TaskID,
                    TaskName = task.TaskName,
                    Description = task.Description,
                    Priority = task.Priority,
                    DayOfMonthStart = task.DayOfMonthStart,
                    DayOfMonthEnd = task.DayOfMonthEnd,
                    PageUrl = task.PageUrl,
                    CanAutomate = task.CanAutomate,
                    IsAutomated = task.IsAutomated,
                    TaskInstanceID = instance?.TaskInstanceID,
                    Status = instance?.Status ??  TaskStatus.Pending,
                    AssignedToUserId = instance?.AssignedToUserId,
                    CompletedDate = instance?.CompletedDate,
                    CompletedByUserId = instance?.CompletedByUserId,
                    Notes = instance?.Notes,
                    IsAutomatedCompletion = instance?.IsAutomatedCompletion ?? false,
                    ComputedStatus = computedStatus,
                    SortPriority = sortPriority,
                    Year = currentYear,
                    Month = currentMonth
                });
            }

            return result.OrderBy(t => t.SortPriority)
                        .ThenBy(t => (int)t.Priority)
                        .ThenBy(t => t.DayOfMonthStart)
                        .ToList();
        }

        public async Task<bool> CompleteTaskAsync(int taskId, string userId, string? notes = null, bool isAutomated = false)
        {
            try
            {
                var currentYear = DateTime.Now.Year;
                var currentMonth = DateTime.Now.Month;

                var taskInstance = await _context.AdminTaskInstances
                    .FirstOrDefaultAsync(ti => ti.TaskID == taskId && ti.Year == currentYear && ti.Month == currentMonth);

                if (taskInstance == null)
                {
                    // Create the instance if it doesn't exist
                    taskInstance = new AdminTaskInstance
                    {
                        TaskID = taskId,
                        Year = currentYear,
                        Month = currentMonth,
                        Status = TaskStatus.Pending,
                        DateCreated = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.AdminTaskInstances.Add(taskInstance);
                }

                taskInstance.Status = TaskStatus.Completed;
                taskInstance.CompletedDate = DateTime.UtcNow;
                taskInstance.CompletedByUserId = userId;
                taskInstance.Notes = notes;
                taskInstance.IsAutomatedCompletion = isAutomated;
                taskInstance.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing task {TaskId} for user {UserId}", taskId, userId);
                return false;
            }
        }

        public async Task<bool> AssignTaskAsync(int taskId, string assignToUserId, string assignedByUserId)
        {
            try
            {
                var currentYear = DateTime.Now.Year;
                var currentMonth = DateTime.Now.Month;

                var taskInstance = await _context.AdminTaskInstances
                    .FirstOrDefaultAsync(ti => ti.TaskID == taskId && ti.Year == currentYear && ti.Month == currentMonth);

                if (taskInstance == null)
                {
                    taskInstance = new AdminTaskInstance
                    {
                        TaskID = taskId,
                        Year = currentYear,
                        Month = currentMonth,
                        Status = TaskStatus.Pending,
                        DateCreated = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.AdminTaskInstances.Add(taskInstance);
                }

                taskInstance.AssignedToUserId = assignToUserId;
                taskInstance.Status = TaskStatus.InProgress;
                taskInstance.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning task {TaskId} to user {AssignToUserId}", taskId, assignToUserId);
                return false;
            }
        }

        public async Task<bool> ShouldShowTaskReminderAsync(string userId)
        {
            // Check if there are overdue or due now tasks
            var hasUrgentTasks = await HasOverdueTasksAsync() || await HasTasksDueNowAsync();

            if (!hasUrgentTasks) return false;

            // Check if user has dismissed reminder recently (within last 4 hours)
            var recentDismissal = await _context.TaskStatusMessages
                .Where(tsm => tsm.UserId == userId && tsm.DismissedAt > DateTime.UtcNow.AddHours(-4))
                .OrderByDescending(tsm => tsm.DismissedAt)
                .FirstOrDefaultAsync();

            return recentDismissal == null;
        }

        public async Task DismissTaskReminderAsync(string userId)
        {
            var existingMessage = await _context.TaskStatusMessages
                .Where(tsm => tsm.UserId == userId)
                .FirstOrDefaultAsync();

            if (existingMessage != null)
            {
                existingMessage.DismissedAt = DateTime.UtcNow;
                existingMessage.DismissalCount++;
            }
            else
            {
                _context.TaskStatusMessages.Add(new TaskStatusMessage
                {
                    UserId = userId,
                    DismissedAt = DateTime.UtcNow,
                    DismissalCount = 1
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<bool> HasOverdueTasksAsync()
        {
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;
            var today = DateTime.Now.Date;
            var endOfCurrentMonth = new DateTime(currentYear, currentMonth, DateTime.DaysInMonth(currentYear, currentMonth));

            var overdueTasks = await _context.AdminTasks
                .Where(t => t.IsActive)
                .Include(t => t.TaskInstances.Where(ti => ti.Year == currentYear && ti.Month == currentMonth))
                .Where(t => t.TaskInstances.Any(ti => ti.Status != TaskStatus.Completed) || !t.TaskInstances.Any())
                .Where(t => today > endOfCurrentMonth ||
                           (DateTime.Now.Day > t.DayOfMonthEnd && t.DayOfMonthEnd < DateTime.DaysInMonth(currentYear, currentMonth)))
                .AnyAsync();

            return overdueTasks;
        }

        public async Task<bool> HasTasksDueNowAsync()
        {
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;
            var currentDay = DateTime.Now.Day;

            var dueNowTasks = await _context.AdminTasks
                .Where(t => t.IsActive)
                .Include(t => t.TaskInstances.Where(ti => ti.Year == currentYear && ti.Month == currentMonth))
                .Where(t => t.TaskInstances.Any(ti => ti.Status != TaskStatus.Completed) || !t.TaskInstances.Any())
                .Where(t => currentDay >= t.DayOfMonthStart && currentDay <= t.DayOfMonthEnd)
                .AnyAsync();

            return dueNowTasks;
        }

        public async Task InitializeTaskInstancesAsync()
        {
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;

            var activeTasks = await _context.AdminTasks
                .Where(t => t.IsActive)
                .ToListAsync();

            foreach (var task in activeTasks)
            {
                var existingInstance = await _context.AdminTaskInstances
                    .FirstOrDefaultAsync(ti => ti.TaskID == task.TaskID && ti.Year == currentYear && ti.Month == currentMonth);

                if (existingInstance == null)
                {
                    _context.AdminTaskInstances.Add(new AdminTaskInstance
                    {
                        TaskID = task.TaskID,
                        Year = currentYear,
                        Month = currentMonth,
                        Status = TaskStatus.Pending,
                        DateCreated = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<bool> IsTaskCompletedThisMonthAsync(string taskHandler)
        {
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;

            var isCompleted = await _context.AdminTasks
                .Where(t => t.ActionHandler == taskHandler && t.IsActive)
                .Include(t => t.TaskInstances.Where(ti => ti.Year == currentYear && ti.Month == currentMonth))
                .AnyAsync(t => t.TaskInstances.Any(ti => ti.Status == TaskStatus.Completed));

            return isCompleted;
        }

        public async Task<AdminTaskInstance?> GetTaskInstanceAsync(int taskId, int year, int month)
        {
            return await _context.AdminTaskInstances
                .Include(ti => ti.AdminTask)
                .FirstOrDefaultAsync(ti => ti.TaskID == taskId && ti.Year == year && ti.Month == month);
        }

        public async Task<bool> MarkTaskCompletedAutomaticallyAsync(string actionHandler, string? notes = null)
        {
            try
            {
                var currentYear = DateTime.Now.Year;
                var currentMonth = DateTime.Now.Month;

                var task = await _context.AdminTasks
                    .FirstOrDefaultAsync(t => t.ActionHandler == actionHandler && t.IsActive);

                if (task == null)
                {
                    _logger.LogWarning("No active task found with handler: {ActionHandler}", actionHandler);
                    return false;
                }

                var taskInstance = await _context.AdminTaskInstances
                    .FirstOrDefaultAsync(ti => ti.TaskID == task.TaskID && ti.Year == currentYear && ti.Month == currentMonth);

                if (taskInstance == null)
                {
                    taskInstance = new AdminTaskInstance
                    {
                        TaskID = task.TaskID,
                        Year = currentYear,
                        Month = currentMonth,
                        Status = TaskStatus.Completed,
                        CompletedDate = DateTime.UtcNow,
                        Notes = notes ?? "Automatically marked as completed",
                        IsAutomatedCompletion = true,
                        DateCreated = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.AdminTaskInstances.Add(taskInstance);
                }
                else if (taskInstance.Status != TaskStatus.Completed)
                {
                    taskInstance.Status = TaskStatus.Completed;
                    taskInstance.CompletedDate = DateTime.UtcNow;
                    taskInstance.Notes = notes ?? "Automatically marked as completed";
                    taskInstance.IsAutomatedCompletion = true;
                    taskInstance.LastUpdated = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Task with handler {ActionHandler} automatically marked as completed", actionHandler);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error automatically completing task with handler: {ActionHandler}", actionHandler);
                return false;
            }
        }

        private static string ComputeTaskStatus(AdminTask task, AdminTaskInstance? instance, int currentDay)
        {
            if (instance?.Status == TaskStatus.Completed) return "Completed";

            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;
            var endOfMonth = DateTime.DaysInMonth(currentYear, currentMonth);
            var today = DateTime.Now.Date;
            var endOfCurrentMonth = new DateTime(currentYear, currentMonth, endOfMonth);

            if (currentDay >= task.DayOfMonthStart && currentDay <= task.DayOfMonthEnd)
                return "Due Now";

            return "Pending";
        }

        private static int ComputeSortPriority(string computedStatus, AdminTask task, int currentDay)
        {
            return computedStatus switch
            {
                "Completed" => 0,
                "Overdue" => 1,
                "Due Now" => 2,
                _ => 3
            };
        }
    }

    public class TaskStatusViewModel
    {
        public int TaskID { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public TaskPriority Priority { get; set; }
        public int DayOfMonthStart { get; set; }
        public int DayOfMonthEnd { get; set; }
        public string? PageUrl { get; set; }
        public bool CanAutomate { get; set; }
        public bool IsAutomated { get; set; }
        public int? TaskInstanceID { get; set; }
        public TaskStatus Status { get; set; }
        public string? AssignedToUserId { get; set; }
        public DateTime? CompletedDate { get; set; }
        public string? CompletedByUserId { get; set; }
        public string? Notes { get; set; }
        public bool IsAutomatedCompletion { get; set; }
        public string ComputedStatus { get; set; } = string.Empty;
        public int SortPriority { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        public string PriorityBadgeClass => Priority switch
        {
            TaskPriority.Low => "badge bg-secondary",
            TaskPriority.Medium => "badge bg-primary",
            TaskPriority.High => "badge bg-warning",
            TaskPriority.Critical => "badge bg-danger",
            _ => "badge bg-secondary"
        };

        public string StatusBadgeClass => ComputedStatus switch
        {
            "Completed" => "badge bg-success",
            "Overdue" => "badge bg-danger",
            "Due Now" => "badge bg-warning",
            "Pending" => "badge bg-secondary",
            _ => "badge bg-secondary"
        };

        public string DueDateRange => $"{DayOfMonthStart}{GetDaySuffix(DayOfMonthStart)} - {DayOfMonthEnd}{GetDaySuffix(DayOfMonthEnd)}";

        private static string GetDaySuffix(int day)
        {
            return day switch
            {
                1 or 21 or 31 => "st",
                2 or 22 => "nd",
                3 or 23 => "rd",
                _ => "th"
            };
        }
    }
}