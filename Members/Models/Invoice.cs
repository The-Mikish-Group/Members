using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Members.Models
{
    public enum InvoiceStatus
    {
        Draft,     
        Due,
        Paid,
        Overdue,
        Cancelled
    }
    public enum InvoiceType
    {
        Dues,
        Fine,
        LateFee,
        MiscCharge
    }
    public class Invoice
    {
        [Key]
        public int InvoiceID { get; set; }
        [Required]
        public string UserID { get; set; }

        [StringLength(250)]
        [Display(Name = "Reason for Cancellation")]
        public string? ReasonForCancellation { get; set; }

        [ForeignKey("UserID")]
        public virtual IdentityUser User { get; set; } = null!;
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Invoice Date")]
        public DateTime InvoiceDate { get; set; }
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Due Date")]
        public DateTime DueDate { get; set; }
        [Required]
        [StringLength(1000, ErrorMessage = "Description cannot be longer than 200 characters.")]
        public string Description { get; set; }
        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount Due")]
        public decimal AmountDue { get; set; }
        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        [Display(Name = "Amount Paid")]
        public decimal AmountPaid { get; set; } = 0.00m;
        [Required]
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Due;
        [Required]
        public InvoiceType Type { get; set; }
        // --- ADD THIS NEW PROPERTY ---
        [StringLength(100)]
        [Display(Name = "Batch ID")]
        public string? BatchID { get; set; } // To group invoices from the same batch run
        // --- END ADD ---
        [DataType(DataType.DateTime)]
        [Display(Name = "Date Created")]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        [DataType(DataType.DateTime)]
        [Display(Name = "Last Updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public Invoice()
        {
            Description = string.Empty;
            UserID = string.Empty;
            User = null!;
            BatchID = null; // Initialize nullable string
        }
    }
}