using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class CategoryFile
    {
        [Key]
        public int FileID { get; set; }
        [ForeignKey("PDFCategory")] // Explicitly link CategoryID to the PDFCategory navigation property
        public int CategoryID { get; set; }
        [Required]
        public required string FileName { get; set; }
        public int SortOrder { get; set; }        

        public required virtual PDFCategory PDFCategory { get; set; }
    }
}