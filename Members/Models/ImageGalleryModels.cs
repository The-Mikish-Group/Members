#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Members.Models
{
    // ViewModel for displaying a list of galleries
    public class GalleryViewModel
    {
        public required string Name { get; set; }
        public int ImageCount { get; set; }
    }

    // ViewModel for displaying a single image
    public class ImageViewModel
    {
        public required string GalleryName { get; set; }
        public required string FileName { get; set; } // e.g., myimage.jpg
        public required string ThumbnailUrl { get; set; } // e.g., /Galleries/GalleryName/myimage_thumb.jpg
        public required string FullImageUrl { get; set; } // e.g., /Galleries/GalleryName/myimage.jpg
    }

    // ViewModel for creating a new gallery
    public class CreateGalleryViewModel
    {
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 3)]
        [RegularExpression(@"^[a-zA-Z0-9\s-]+$", ErrorMessage = "Gallery name can only contain letters, numbers, spaces, and hyphens.")]
        public required string GalleryName { get; set; }
    }

    // ViewModel for uploading an image
    public class UploadImageViewModel
    {
        [Required]
        public required string GalleryName { get; set; }

        [Required(ErrorMessage = "Please select an image file.")]
        [DataType(DataType.Upload)]
        [AllowedExtensions([".jpg", ".jpeg", ".png", ".gif"], ErrorMessage = "Only .jpg, .jpeg, .png, and .gif files are allowed.")] // Custom validation attribute
        public required IFormFile ImageFile { get; set; }
    }

    // ViewModel for renaming an image
    public class RenameImageViewModel
    {
        [Required]
        public required string GalleryName { get; set; }

        [Required]
        public required string OldFileName { get; set; } // e.g., myimage.jpg

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 3)]
        [RegularExpression(@"^[a-zA-Z0-9\s.-]+$", ErrorMessage = "File name can only contain letters, numbers, spaces, dots, and hyphens.")]
        public required string NewFileName { get; set; } // e.g., newname.jpg
    }

    // Custom Validation Attribute for file extensions
    public class AllowedExtensionsAttribute(string[] extensions) : ValidationAttribute
    {
        private readonly string[] _extensions = extensions;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is IFormFile file)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_extensions.Contains(extension))
                {
                    return new ValidationResult(ErrorMessage);
                }
            }
            // If no file is uploaded, or if the validation is not required (handled by [Required]), return Success.
            // The [Required] attribute will handle the case where no file is provided.
            return ValidationResult.Success!;
        }
    }
}
