// Members.Models.ViewModels/CreatePdfFormViewModel.cs
namespace Members.Models // Make sure this namespace matches your folder structure
{
    // ViewModel used for the GET request to display the PDF generation form
    public class CreatePdfFormViewModel
    {
        // List of categories to populate the dropdown
        public List<PDFCategory> Categories { get; set; } = [];

        // Suggested next sort order for the new file
        public int SuggestedSortOrder { get; set; } = 1;
        public int DirectoryCategoryId { get; internal set; }

        // Properties for displaying messages if redirecting back to this view (Optional, TempData is used in controller)
        // public string? SuccessMessage { get; set; }
        // public string? ErrorMessage { get; set; }
    }
}