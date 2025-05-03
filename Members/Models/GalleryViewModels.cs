#nullable enable
using System.Collections.Generic; // Needed for List
using System.IO; // Needed for Path.GetFileNameWithoutExtension
using System.Linq; // Needed for Any()

namespace Members.Models
{
    // ViewModel to represent a single image in the gallery view
    public class GalleryImageViewModel
    {
        public string ThumbnailFileName { get; set; } = string.Empty;

        // Marked as required, providing explicit default to satisfy compiler
        // Changed from = default!; back to = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;

        public string DisplayName // Calculated property, doesn't need required or default
        {
            get
            {
                // Derive the display name from the thumbnail file name, removing "_thumb" and extension
                return Path.GetFileNameWithoutExtension(ThumbnailFileName).Replace("_thumb", "");
            }
        }

        // Marked as required, providing explicit default to satisfy compiler
        // Changed from = default!; back to = string.Empty;
        public required string GalleryName { get; set; }
    }

    // ViewModel to hold the paginated list of images and pagination metadata
    public class PaginatedGalleryViewModel
    {
        // Marked as required, providing explicit default to satisfy compiler
        // List should be initialized, so using new List<GalleryImageViewModel>() is also an option
        public required List<GalleryImageViewModel> Images { get; set; } = default!; // Keep default! here, List initialization is handled in controller

        public int PageNumber { get; set; } // Value type, non-nullable by default
        public int PageSize { get; set; } // Value type, non-nullable by default
        public int TotalItems { get; set; } // Value type, non-nullable by default
        public int TotalPages { get; set; } // Value type, non-nullable by default

        // Marked as required, providing explicit default to satisfy compiler
        public required string GalleryName { get; set; } = string.Empty; // Keep string.Empty here


        // Helper property to check if there's a previous page
        public bool HasPreviousPage => PageNumber > 1;

        // Helper property to check if there's a next page
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
