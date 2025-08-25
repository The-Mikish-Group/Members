using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Members.Models
{
    public class CategoryLink
    {
        [Key]
        public int LinkID { get; set; }

        [ForeignKey("LinkCategory")]
        public int CategoryID { get; set; }

        [Required]
        public required string LinkName { get; set; }

        [Required]
        [Url]
        public required string LinkUrl { get; set; }

        public int SortOrder { get; set; }

        public virtual LinkCategory? LinkCategory { get; set; }
    }
}