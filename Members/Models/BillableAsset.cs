using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class BillableAsset
    {
        [Key]
        public int BillableAssetID { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Plot ID / Asset Identifier")]
        public string PlotID { get; set; } = string.Empty;

        // Foreign Key to IdentityUser (represents the Billing Contact)
        // Nullable, as an asset might be unassigned or assignment might be pending
        public string? UserID { get; set; } 

        [ForeignKey("UserID")]
        public virtual IdentityUser? User { get; set; }

        [Display(Name = "Date Created")]
        [DataType(DataType.DateTime)]
        public DateTime DateCreated { get; set; }

        [Display(Name = "Last Updated")]
        [DataType(DataType.DateTime)]
        public DateTime LastUpdated { get; set; }

        // Optional: A description for the asset itself
        [StringLength(250)]
        public string? Description { get; set; } 

        // New Property for Assessment Fee
        [Required(ErrorMessage = "Assessment Fee is required.")]
        [DataType(DataType.Currency)]
        [Range(0.00, 1000000.00, ErrorMessage = "Assessment Fee must be a non-negative value.")]
        [Column(TypeName = "decimal(18, 2)")] // Specify SQL Server column type for precision
        [Display(Name = "Assessment Fee")]
        public decimal AssessmentFee { get; set; }
    }
}
