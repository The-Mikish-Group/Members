using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class CreditApplication
    {
        [Key]
        public int CreditApplicationID { get; set; }

        [Required]
        public int UserCreditID { get; set; }
        public virtual UserCredit? UserCredit { get; set; }

        [Required]
        public int InvoiceID { get; set; }
        public virtual Invoice? Invoice { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal AmountApplied { get; set; }

        [Required]
        public DateTime ApplicationDate { get; set; }

        [Required]
        public bool IsReversed { get; set; } = false;

        public DateTime? ReversedDate { get; set; }

        [StringLength(255)]
        public string? Notes { get; set; }

        public CreditApplication()
        {
            ApplicationDate = DateTime.UtcNow; // Default value
            IsReversed = false;
        }
    }
}
