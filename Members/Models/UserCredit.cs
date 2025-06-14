using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class UserCredit
    {
        [Key]
        public int UserCreditID { get; set; }

        [Required]
        public required string UserID { get; set; } // Foreign Key to IdentityUser  
        [ForeignKey("UserID")]
        public virtual IdentityUser User { get; set; } = null!;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Credit Date")]
        public DateTime CreditDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [DataType(DataType.Currency)]
        [Range(0.01, double.MaxValue, ErrorMessage = "Credit amount must be positive.")]
        public decimal Amount { get; set; }

        [Display(Name = "Source Payment ID")]
        public int? SourcePaymentID { get; set; }
        [ForeignKey("SourcePaymentID")]
        public virtual Payment? SourcePayment { get; set; }

        [Required]
        [StringLength(250)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Is Applied")]
        public bool IsApplied { get; set; } = false;

        [DataType(DataType.Date)]
        [Display(Name = "Date Applied")]
        public DateTime? AppliedDate { get; set; }

        [Display(Name = "Applied to Invoice ID")]
        public int? AppliedToInvoiceID { get; set; }
        [ForeignKey("AppliedToInvoiceID")]
        public virtual Invoice? AppliedToInvoice { get; set; }

        [StringLength(250)]
        [Display(Name = "Application Notes")]
        public string? ApplicationNotes { get; set; }

        [DataType(DataType.DateTime)]
        [Display(Name = "Date Created")]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        [DataType(DataType.DateTime)]
        [Display(Name = "Last Updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [Required]
        [Display(Name = "Is Voided?")]
        public bool IsVoided { get; set; } = false; // Initialize to false
    }
}