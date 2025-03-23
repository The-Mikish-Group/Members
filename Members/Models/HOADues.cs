using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Members.Models
{
    public class HOADues
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")] // Adjust precision as needed
        public decimal DueAmount { get; set; }

        public DateTime? LastPaidDate { get; set; }

        // Optional: Add fields for payment date, payment method, etc.
        public DateTime? PaymentDate { get; set; }
        public string? PaymentMethod { get; set; }

        // Link to User (UserProfile) - This is included as per your initial request
        public string? UserId { get; set; }
        [ForeignKey("UserId")]
        public UserProfile? UserProfile { get; set; }

        //Link to Plot - This is the key part
        public int? Plot { get; set; }
        [ForeignKey("Plot")]
        public Plots? PlotDetails { get; set; } // Added missing property for PlotDetails
    }
}