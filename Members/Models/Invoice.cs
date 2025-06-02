using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public enum InvoiceStatus
    {
        Due,
        Paid,
        Overdue,
        Cancelled
    }

    public enum InvoiceType
    {
        Dues,       // For regular assessments/annual dues
        Fine,       // For HOA violations
        LateFee,
        MiscCharge  // For other types of charges
    }

    public class Invoice
    {
        [Key]
        public int InvoiceID { get; set; }

        [Required]
        public string UserID { get; set; } // Foreign Key to IdentityUser

        [ForeignKey("UserID")]
        public virtual IdentityUser User { get; set; } // Navigation property

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Invoice Date")]
        public DateTime InvoiceDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Due Date")]
        public DateTime DueDate { get; set; }

        [Required]
        [StringLength(200, ErrorMessage = "Description cannot be longer than 200 characters.")]
        public string Description { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount Due")]
        public decimal AmountDue { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount Paid")]
        public decimal AmountPaid { get; set; } = 0.00m; // Default to 0

        [Required]
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Due; // Default to Due

        [Required]
        public InvoiceType Type { get; set; }

        // Optional: For tracking when the invoice was created and last updated
        [DataType(DataType.DateTime)]
        [Display(Name = "Date Created")]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        [DataType(DataType.DateTime)]
        [Display(Name = "Last Updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Constructor
        public Invoice()
        {
            Description = string.Empty;
            UserID = string.Empty;
            // User property is a navigation property, typically set by EF Core,
            // so we might not need to initialize it here to avoid issues,
            // or ensure it's handled correctly if nullable reference types are enabled.
            // For now, let EF Core handle it or use User = null!; if NRTs are on.
            User = null!;
        }
    }
}