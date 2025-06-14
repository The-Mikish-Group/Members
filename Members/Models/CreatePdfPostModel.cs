using System.ComponentModel.DataAnnotations;

namespace Members.Models
{
    public class CreatePdfPostModel
    {
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public int CategoryId { get; set; } // This should be the Directory Category ID

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Sort Order must be a positive number.")]
        public int SortOrder { get; set; }

        // Add this property to handle overwrite confirmation from the client
        public bool OverwriteConfirmed { get; set; }
    }
}

