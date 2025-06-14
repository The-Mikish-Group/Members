using System.ComponentModel.DataAnnotations;

namespace Members.Models
{
    public class PDFCategory
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        public required string CategoryName { get; set; }

        [Required]
        public int SortOrder { get; set; }

        // Navigation property to the CategoryFiles
        public virtual ICollection<CategoryFile> CategoryFiles { get; set; } = [];

        public PDFCategory()
        {
            CategoryFiles = [];
        }
    }
}