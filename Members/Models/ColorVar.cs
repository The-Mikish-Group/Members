using System.ComponentModel.DataAnnotations;

namespace Members.Models
{
    public class ColorVar
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(7)]
        public string Value { get; set; } = string.Empty;
    }
}
