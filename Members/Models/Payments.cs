using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Members.Models
{
    public enum PaymentMethod
    {
        Check,
        Cash,
        Online, // For future use
        Other
    }
    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }
        // A payment might not always be directly for a single invoice,
        // for example, if a member makes a general payment on their account
        // that could cover multiple small invoices or be a prepayment.
        // Making InvoiceID nullable allows for this flexibility.
        // If payments ALWAYS must be tied to one invoice, make this non-nullable
        // and potentially remove the UserID/User navigation if it's always via Invoice.
        public int? InvoiceID { get; set; } // Foreign Key to Invoice, Nullable
        [ForeignKey("InvoiceID")]    
        public virtual Invoice? Invoice { get; set; } // Navigation property, Nullable
        [Required]
        public string UserID { get; set; } // Foreign Key to IdentityUser, always required
        [ForeignKey("UserID")]
        public virtual IdentityUser User { get; set; } // Navigation property
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Payment Date")]
        public DateTime PaymentDate { get; set; }
        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        public decimal Amount { get; set; }
        [Required]
        public PaymentMethod Method { get; set; }
        [StringLength(1000)]
        [Display(Name = "Reference Number")]
        public string? ReferenceNumber { get; set; } // E.g., Check number, transaction ID
        [StringLength(200)]
        public string? Notes { get; set; } // Optional notes from admin
        // Optional: For tracking when the payment was recorded
        [DataType(DataType.DateTime)]
        [Display(Name = "Date Recorded")]
        public DateTime DateRecorded { get; set; } = DateTime.UtcNow;
        // Constructor

        [Display(Name = "Is Voided")]
        public bool IsVoided { get; set; } = false;

        [DataType(DataType.DateTime)]
        [Display(Name = "Date Voided")]
        public DateTime? VoidedDate { get; set; }

        [StringLength(250)]
        [Display(Name = "Reason for Voiding")]
        public string? ReasonForVoiding { get; set; }

        [DataType(DataType.DateTime)]
        [Display(Name = "Last Updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public Payment()
        {
            UserID = string.Empty;
            User = null!; // Null forgiveness, EF Core will set
            Invoice = null; // Explicitly null for nullable navigation
            ReferenceNumber = null;
            Notes = null;
        }
    }
}