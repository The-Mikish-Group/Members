using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class LinkCategory
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        public required string CategoryName { get; set; }

        [Required]
        public int SortOrder { get; set; }

        public bool IsAdminOnly { get; set; } = false;

        public virtual ICollection<CategoryLink> CategoryLinks { get; set; } = [];
    }
}