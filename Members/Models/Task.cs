using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Members.Models
{
    public class AdminTask
    {
        [Key]
        public int TaskID { get; set; }

        [Required]
        [StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required]
        public TaskFrequency Frequency { get; set; } = TaskFrequency.Monthly;

        [Required]
        public int DayOfMonthStart { get; set; } = 1; // Start day of month (1-31)

        [Required]
        public int DayOfMonthEnd { get; set; } = 5; // End day of month (1-31)

        [Required]
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        [StringLength(200)]
        public string? PageUrl { get; set; } // URL to the page where task is performed

        [StringLength(100)]
        public string? ActionHandler { get; set; } // Handler method name for automation

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public bool CanAutomate { get; set; } = false; // Whether this task can be automated

        [Required]
        public bool IsAutomated { get; set; } = false; // Whether automation is currently enabled

        [Required]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation property for task instances
        public virtual ICollection<AdminTaskInstance> TaskInstances { get; set; } = new List<AdminTaskInstance>();
    }

    public class AdminTaskInstance
    {
        [Key]
        public int TaskInstanceID { get; set; }

        [Required]
        public int TaskID { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int Month { get; set; }

        [Required]
        public TaskStatus Status { get; set; } = TaskStatus.Pending;

        public string? AssignedToUserId { get; set; } // Optional assignment to specific user

        public DateTime? CompletedDate { get; set; }

        public string? CompletedByUserId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required]
        public bool IsAutomatedCompletion { get; set; } = false; // Was this completed automatically?

        [Required]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("TaskID")]
        public virtual AdminTask AdminTask { get; set; } = null!;

        [ForeignKey("AssignedToUserId")]
        public virtual IdentityUser? AssignedToUser { get; set; }

        [ForeignKey("CompletedByUserId")]
        public virtual IdentityUser? CompletedByUser { get; set; }
    }

    public class TaskStatusMessage
    {
        [Key]
        public int MessageID { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public DateTime DismissedAt { get; set; }

        [Required]
        public int DismissalCount { get; set; } = 1;

        // Navigation property
        [ForeignKey("UserId")]
        public virtual IdentityUser User { get; set; } = null!;
    }

    public enum TaskFrequency
    {
        Monthly = 1,
        Quarterly = 2,
        Annually = 3
    }

    public enum TaskPriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum TaskStatus
    {
        Pending = 1,
        InProgress = 2,
        Completed = 3,
        Overdue = 4,
        Skipped = 5
    }
}