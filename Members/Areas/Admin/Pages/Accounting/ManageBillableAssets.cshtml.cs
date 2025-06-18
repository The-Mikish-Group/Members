using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System; // Added for DateTime
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class ManageBillableAssetsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ManageBillableAssetsModel> _logger;
        public ManageBillableAssetsModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, ILogger<ManageBillableAssetsModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }
        public List<BillableAssetViewModel> Assets { get; set; } = new List<BillableAssetViewModel>();
        [BindProperty]
        public AddBillableAssetInputModel NewAssetInput { get; set; } = new AddBillableAssetInputModel();
        [BindProperty]
        public EditAssetInputModel? EditInput { get; set; }
        public bool ShowEditForm { get; set; } = false;
        public SelectList? BillingContactUsersSL { get; set; }
        // Properties for Sort State
        [BindProperty(SupportsGet = true)]
        public string? CurrentSort { get; set; }
        public string? PlotIdSort { get; set; }
        public string? BillingContactSort { get; set; }
        public string? DescriptionSort { get; set; }
        public string? DateCreatedSort { get; set; }
        public string? LastUpdatedSort { get; set; }
        public class EditAssetInputModel
        {
            [Required]
            public int BillableAssetID { get; set; }
            [Required(ErrorMessage = "Plot ID / Asset Identifier is required.")]
            [StringLength(100)]
            [Display(Name = "Plot ID / Asset Identifier")]
            public string PlotID { get; set; } = string.Empty;
            [Display(Name = "Assign to Billing Contact")]
            public string? SelectedUserID { get; set; }
            [StringLength(250)]
            [Display(Name = "Optional Description")]
            public string? Description { get; set; }
            // [Required(ErrorMessage = "Assessment Fee is required.")] // Commented out
            [DataType(DataType.Currency)]
            [Range(0.00, 1000000.00, ErrorMessage = "Assessment Fee must be a non-negative value (0.00 is allowed).")]
            [Display(Name = "Assessment Fee")]
            public decimal AssessmentFee { get; set; }
        }
        public class BillableAssetViewModel
        {
            public int BillableAssetID { get; set; }
            public string PlotID { get; set; } = string.Empty;
            public string? UserID { get; set; }
            public string? BillingContactFullName { get; set; } // Format: "LastName, FirstName (Email)"
            public string? BillingContactEmail { get; set; }
            public DateTime DateCreated { get; set; }
            public DateTime LastUpdated { get; set; }
            public string? Description { get; set; }
            [DataType(DataType.Currency)]
            public decimal AssessmentFee { get; set; }
        }
        public class AddBillableAssetInputModel
        {
            // [Required(ErrorMessage = "Plot ID / Asset Identifier is required.")] // Commented out
            [StringLength(100)]
            [Display(Name = "Plot ID / Asset Identifier")]
            public string PlotID { get; set; } = string.Empty;
            // [Required(ErrorMessage = "A Billing Contact must be selected.")] // Commented out
            [Display(Name = "Assign to Billing Contact")]
            public string SelectedUserID { get; set; } = string.Empty;
            [StringLength(250)]
            [Display(Name = "Optional Description")]
            public string? Description { get; set; }
            // [Required(ErrorMessage = "Assessment Fee is required.")] // Commented out
            [DataType(DataType.Currency)]
            [Range(0.00, 1000000.00, ErrorMessage = "Assessment Fee must be a non-negative value (0.00 is allowed).")]
            [Display(Name = "Assessment Fee")]
            public decimal AssessmentFee { get; set; }
        }
        private async Task PopulateBillingContactUsersSL()
        {
            var billingContactProfiles = await _context.UserProfile
                .Where(up => up.IsBillingContact)
                .OrderBy(up => up.LastName)
                .ThenBy(up => up.FirstName)
                .Select(up => new { up.UserId, up.FirstName, up.LastName })
                .ToListAsync();
            var userIds = billingContactProfiles.Select(p => p.UserId).ToList();
            var identityUsers = await _context.Users // Using _context.Users (from IdentityDbContext)
                                      .Where(u => userIds.Contains(u.Id))
                                      .ToDictionaryAsync(u => u.Id);
            var selectListItems = billingContactProfiles.Select(p => new SelectListItem
            {
                Value = p.UserId,
                Text = $"{p.LastName}, {p.FirstName} ({(identityUsers.TryGetValue(p.UserId, out var idUser) ? idUser.Email : "N/A")})"
            }).ToList();
            BillingContactUsersSL = new SelectList(selectListItems, "Value", "Text");
        }
        public async Task OnGetAsync()
        {
            _logger.LogInformation("Loading ManageBillableAssets page.");
            var assetsFromDb = await _context.BillableAssets
                                   .Include(ba => ba.User) // Eager load User for email access
                                   .OrderBy(ba => ba.PlotID)
                                   .ToListAsync();
            var userIdsForAssets = assetsFromDb
                                   .Where(a => a.UserID != null)
                                   .Select(a => a.UserID!) // Use null-forgiving operator as we checked for null
                                   .Distinct()
                                   .ToList();
            var userProfilesForAssets = await _context.UserProfile
                                        .Where(up => userIdsForAssets.Contains(up.UserId))
                                        .ToDictionaryAsync(up => up.UserId);
            Assets = new List<BillableAssetViewModel>();
            foreach (var asset in assetsFromDb)
            {
                string? contactFullName = null;
                string? contactEmail = null;
                if (asset.User != null) // User is populated from Include()
                {
                    userProfilesForAssets.TryGetValue(asset.User.Id, out UserProfile? userProfile);
                    contactFullName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                      ? $"{userProfile.LastName}, {userProfile.FirstName}"
                                      : asset.User?.UserName; // Use ?.
                    contactEmail = asset.User?.Email; // Use ?.
                }
                Assets.Add(new BillableAssetViewModel
                {
                    BillableAssetID = asset.BillableAssetID,
                    PlotID = asset.PlotID,
                    UserID = asset.UserID,
                    BillingContactFullName = contactFullName ?? "N/A (Unassigned)",
                    BillingContactEmail = contactEmail,
                    DateCreated = asset.DateCreated,
                    LastUpdated = asset.LastUpdated,
                    Description = asset.Description,
                    AssessmentFee = asset.AssessmentFee
                });
            }
            _logger.LogInformation("Loaded {AssetCount} billable assets before sorting.", Assets.Count);
            // Initialize sorting properties
            string defaultSortColumn = "contact_asc";
            string activeSort = CurrentSort ?? defaultSortColumn;
            this.CurrentSort = activeSort;
            PlotIdSort = activeSort == "plotid_asc" ? "plotid_desc" : "plotid_asc";
            BillingContactSort = activeSort == "contact_asc" ? "contact_desc" : "contact_asc";
            DescriptionSort = activeSort == "desc_asc" ? "desc_desc" : "desc_asc";
            DateCreatedSort = activeSort == "created_asc" ? "created_desc" : "created_asc";
            LastUpdatedSort = activeSort == "updated_asc" ? "updated_desc" : "updated_asc";
            _logger.LogInformation("Sorting parameters initialized. CurrentSort/ActiveSort: {ActiveSort}, PlotIdSort: {PlotSortVal}, BillingContactSort: {ContactSortVal}, DescriptionSort: {DescSortVal}, DateCreatedSort: {CreatedSortVal}, LastUpdatedSort: {UpdatedSortVal}",
                activeSort, PlotIdSort, BillingContactSort, DescriptionSort, DateCreatedSort, LastUpdatedSort);
            // Apply Sorting to Assets list
            switch (activeSort)
            {
                case "plotid_desc":
                    Assets = Assets.OrderByDescending(a => a.PlotID).ToList();
                    break;
                case "plotid_asc":
                    Assets = Assets.OrderBy(a => a.PlotID).ToList();
                    break;
                case "contact_desc":
                    Assets = Assets.OrderByDescending(a => a.BillingContactFullName ?? string.Empty).ToList();
                    break;
                case "contact_asc":
                    Assets = Assets.OrderBy(a => a.BillingContactFullName ?? string.Empty).ToList();
                    break;
                case "desc_desc":
                    Assets = Assets.OrderByDescending(a => a.Description ?? string.Empty).ToList();
                    break;
                case "desc_asc":
                    Assets = Assets.OrderBy(a => a.Description ?? string.Empty).ToList();
                    break;
                case "created_desc":
                    Assets = Assets.OrderByDescending(a => a.DateCreated).ToList();
                    break;
                case "created_asc":
                    Assets = Assets.OrderBy(a => a.DateCreated).ToList();
                    break;
                case "updated_desc":
                    Assets = Assets.OrderByDescending(a => a.LastUpdated).ToList();
                    break;
                case "updated_asc":
                    Assets = Assets.OrderBy(a => a.LastUpdated).ToList();
                    break;
                default:
                    Assets = Assets.OrderBy(a => a.PlotID).ToList();
                    break;
            }
            _logger.LogInformation("Assets list sorted by {ActiveSort}. Final Count: {Count}", activeSort, Assets.Count);
            await PopulateBillingContactUsersSL();
            _logger.LogInformation("Finished OnGetAsync. Loaded {AssetCount} billable assets and {UserCount} potential billing contacts after sorting.", Assets.Count, BillingContactUsersSL?.Count() ?? 0);
        }
        public async Task<IActionResult> OnPostAddAssetAsync()
        {
            ModelState.Remove("PlotID");
            ModelState.Remove("NewAssetInput.PlotID");
            ModelState.Remove("SelectedUserID");
            ModelState.Remove("NewAssetInput.SelectedUserID");
            ModelState.Remove("AssessmentFee");
            ModelState.Remove("NewAssetInput.AssessmentFee");
            _logger.LogInformation("OnPostAddAssetAsync Raw Form Data - NewAssetInput.PlotID: {PlotID_Form}, NewAssetInput.SelectedUserID: {UserID_Form}, NewAssetInput.AssessmentFee: {Fee_Form}, NewAssetInput.Description: {Desc_Form}",
                Request.Form["NewAssetInput.PlotID"],
                Request.Form["NewAssetInput.SelectedUserID"],
                Request.Form["NewAssetInput.AssessmentFee"],
                Request.Form["NewAssetInput.Description"]);
            if (NewAssetInput != null)
            {
                _logger.LogInformation("OnPostAddAssetAsync After Model Binding - NewAssetInput.PlotID: {PlotID_Bound}, NewAssetInput.SelectedUserID: {UserID_Bound}, NewAssetInput.AssessmentFee: {Fee_Bound}, NewAssetInput.Description: {Desc_Bound}",
                    NewAssetInput.PlotID ?? "(null)",
                    NewAssetInput.SelectedUserID ?? "(null)",
                    NewAssetInput.AssessmentFee,
                    NewAssetInput.Description ?? "(null)");
            }
            else
            {
                _logger.LogWarning("OnPostAddAssetAsync: NewAssetInput object is null after model binding attempt.");
            }
            // Original log line, can be kept or removed if redundant with the new detailed ones.
            _logger.LogInformation("Attempting to add new billable asset (model state): PlotID = {PlotID}, UserID = {UserID}, Fee = {Fee}", NewAssetInput?.PlotID, NewAssetInput?.SelectedUserID, NewAssetInput?.AssessmentFee);
            // Step 2: Perform Manual Validation for fields where [Required] was troublesome
            if (NewAssetInput != null)
            {
                if (string.IsNullOrWhiteSpace(NewAssetInput.PlotID))
                {
                    ModelState.AddModelError("NewAssetInput.PlotID", "Plot ID / Asset Identifier is required.");
                }
                if (string.IsNullOrWhiteSpace(NewAssetInput.SelectedUserID))
                {
                    ModelState.AddModelError("NewAssetInput.SelectedUserID", "A Billing Contact must be selected.");
                }
                // Check if AssessmentFee was an empty string from form and bound to 0
                if (Request.Form["NewAssetInput.AssessmentFee"] == "" && NewAssetInput.AssessmentFee == 0)
                {
                    ModelState.AddModelError("NewAssetInput.AssessmentFee", "Assessment Fee is required and cannot be empty.");
                }
                // Note: The [Range(0.00,...)] attribute on AssessmentFee should still be active
                // and will catch negative values. If it's also desired that 0 isn't allowed even if explicitly entered,
                // the Range attribute should be [Range(0.01, ...)] or a specific check for == 0 added.
            }
            else // NewAssetInput is null
            {
                ModelState.AddModelError(string.Empty, "Input model could not be bound.");
            }
            // Step 3: Check ModelState (includes manual errors + attribute errors like StringLength, Range)
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("OnPostAddAssetAsync: ModelState is invalid after manual checks. Logging detailed errors...");
                foreach (var modelStateKey in ModelState.Keys)
                {
                    var modelStateVal = ModelState[modelStateKey];
                    if (modelStateVal != null)
                    {
                        foreach (var error in modelStateVal.Errors)
                        {
                            _logger.LogWarning("ModelState Key: {Key}, Error: {ErrorMessage}, Exception: {ExceptionMessage}",
                                modelStateKey ?? "(null key)",
                                error.ErrorMessage ?? "(null message)",
                                error.Exception?.Message ?? "(no exception message)");
                        }
                    }
                }
                await PopulateBillingContactUsersSL(); // Ensure dropdown is repopulated
                // await PopulateBillingContactUsersSL(); // Duplicate line removed
                await OnGetAsync(); // Reload full asset list for display alongside form
                return Page();
            }
            // Step 4: Proceed with logic, using null-forgiving where appropriate for [Required] fields
            // The previous defensive check for PlotID being null/empty AFTER ModelState.IsValid is now removed.
            // string plotIdValue = NewAssetInput.PlotID ?? string.Empty; // This line is removed.
            // if (string.IsNullOrEmpty(plotIdValue)) ... // This entire block is removed.
            // If execution reaches here, NewAssetInput is not null.
            // Also, NewAssetInput.PlotID has passed the string.IsNullOrWhiteSpace check
            // (otherwise ModelState would be invalid and we would have returned Page()).
            // Therefore, NewAssetInput.PlotID is a non-null, non-whitespace string here.
            #pragma warning disable CS8602 // Dereference of a possibly null reference.
            string trimmedPlotId = NewAssetInput.PlotID!.Trim();
            #pragma warning restore CS8602 // Dereference of a possibly null reference.
            // Check for duplicate PlotID
            if (await _context.BillableAssets.AnyAsync(ba => ba.PlotID == trimmedPlotId))
            {
                ModelState.AddModelError("NewAssetInput.PlotID", "This Plot ID / Asset Identifier already exists.");
                _logger.LogWarning("Add new asset failed: Duplicate PlotID {PlotID}.", trimmedPlotId);
                await PopulateBillingContactUsersSL();
                await OnGetAsync();
                NewAssetInput.PlotID = trimmedPlotId;
                ShowEditForm = false;
                return Page();
            }
            var newAsset = new BillableAsset
            {
                PlotID = trimmedPlotId,
                UserID = NewAssetInput.SelectedUserID!,
                Description = NewAssetInput.Description,
                AssessmentFee = NewAssetInput.AssessmentFee,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _context.BillableAssets.Add(newAsset);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully added new billable asset: PlotID = {PlotID}, UserID = {UserID}, AssetID = {AssetID}", newAsset.PlotID, newAsset.UserID, newAsset.BillableAssetID);
                TempData["StatusMessage"] = $"Billable Asset '{newAsset.PlotID}' added successfully and assigned to the selected contact.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving new billable asset {PlotID}", NewAssetInput.PlotID);
                // Check for unique constraint violation specifically if possible, though general message is okay too
                if (ex.InnerException?.Message.Contains("Cannot insert duplicate key row") == true && ex.InnerException.Message.Contains("IX_BillableAssets_PlotID"))
                {
                    ModelState.AddModelError("NewAssetInput.PlotID", "This Plot ID / Asset Identifier already exists. It might have been added by someone else concurrently.");
                    TempData["ErrorMessage"] = "Error: This Plot ID already exists.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Error saving new billable asset. Check logs for details.";
                }
                await OnGetAsync(); // Reload assets and dropdown
                return Page();
            }
            return RedirectToPage();
        }
        public async Task<IActionResult> OnGetShowEditFormAsync(int assetId)
        {
            _logger.LogInformation("OnGetShowEditFormAsync called for assetId: {AssetId}", assetId);
            var assetToEdit = await _context.BillableAssets.FindAsync(assetId);
            if (assetToEdit == null)
            {
                _logger.LogWarning("Asset with ID {AssetId} not found for editing.", assetId);
                TempData["ErrorMessage"] = "Selected billable asset not found.";
                return RedirectToPage();
            }
            // this.PlotIdBeingEdited = assetToEdit.PlotID; // Property removed
            EditInput = new EditAssetInputModel
            {
                BillableAssetID = assetToEdit.BillableAssetID,
                PlotID = assetToEdit.PlotID, // ADD THIS LINE BACK
                SelectedUserID = assetToEdit.UserID,
                Description = assetToEdit.Description,
                AssessmentFee = assetToEdit.AssessmentFee
            };
            ShowEditForm = true;
            await OnGetAsync();
            _logger.LogInformation("Populated EditInput for AssetID {AssetId} (PlotID: {PlotID_Bound}) and set ShowEditForm to true.", assetId, EditInput.PlotID);
            return Page();
        }
        public async Task<IActionResult> OnPostUpdateAssetAsync()
        {
            ModelState.Remove("PlotID");
            ModelState.Remove("SelectedUserID");
            ModelState.Remove("EditInput.SelectedUserID");
            ModelState.Remove("AssessmentFee");
            ModelState.Remove("EditInput.AssessmentFee");
            _logger.LogInformation("OnPostUpdateAssetAsync Raw Form Data - EditInput.BillableAssetID: {AssetID_Form}, EditInput.PlotID: {PlotID_Form}, EditInput.SelectedUserID: {UserID_Form}, EditInput.AssessmentFee: {Fee_Form}, EditInput.Description: {Desc_Form}",
                Request.Form["EditInput.BillableAssetID"],
                Request.Form["EditInput.PlotID"],
                Request.Form["EditInput.SelectedUserID"],
                Request.Form["EditInput.AssessmentFee"],
                Request.Form["EditInput.Description"]);
            if (EditInput != null)
            {
                _logger.LogInformation("OnPostUpdateAssetAsync After Model Binding - EditInput.BillableAssetID: {AssetID_Bound}, EditInput.PlotID: {PlotID_Bound}, EditInput.SelectedUserID: {UserID_Bound}, EditInput.AssessmentFee: {Fee_Bound}, EditInput.Description: {Desc_Bound}",
                    EditInput.BillableAssetID,
                    EditInput.PlotID ?? "(null)",
                    EditInput.SelectedUserID ?? "(null)",
                    EditInput.AssessmentFee,
                    EditInput.Description ?? "(null)");
            }
            else
            {
                _logger.LogWarning("OnPostUpdateAssetAsync: EditInput object is null after model binding attempt.");
            }
            // Original log line, can be kept or removed.
            _logger.LogInformation("Processing OnPostUpdateAssetAsync for BillableAssetID (from bound model): {BillableAssetID}", EditInput?.BillableAssetID);
            if (EditInput == null || EditInput.BillableAssetID == 0)
            {
                TempData["ErrorMessage"] = "Error identifying asset to update. Please try again.";
                _logger.LogWarning("OnPostUpdateAssetAsync called with invalid EditInput or BillableAssetID.");
                return RedirectToPage();
            }
            await PopulateBillingContactUsersSL();
            // Manual Validation for EditInput
            if (EditInput != null) // EditInput itself is checked for null earlier
            {
                // AssessmentFee check for empty string submission resulting in 0
                if (Request.Form["EditInput.AssessmentFee"] == "" && EditInput.AssessmentFee == 0)
                {
                    ModelState.AddModelError("EditInput.AssessmentFee", "Assessment Fee is required and cannot be empty.");
                }
                // PlotID is not part of EditInput.
                // SelectedUserID is optional.
                // BillableAssetID is [Required] and checked by ModelState.
                // AssessmentFee also has [Range(0.00,...)] which will be checked by ModelState.
            }
            // No else needed here as EditInput null or BillableAssetID == 0 is handled before this.
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("OnPostUpdateAssetAsync: ModelState is invalid after manual checks (if any) for AssetID {BillableAssetID}. Logging detailed errors...", EditInput?.BillableAssetID);
                foreach (var modelStateKey in ModelState.Keys)
                {
                    var modelStateVal = ModelState[modelStateKey];
                    if (modelStateVal != null)
                    {
                        foreach (var error in modelStateVal.Errors)
                        {
                            _logger.LogWarning($"ModelState Key: {modelStateKey}, Error: {error.ErrorMessage}, Exception: {error.Exception?.Message}");
                        }
                    }
                }
                ShowEditForm = true;
                await OnGetAsync(); // Reload full asset list
                return Page();
            }
            var assetToUpdate = await _context.BillableAssets.FindAsync(EditInput!.BillableAssetID);
            if (assetToUpdate == null)
            {
                _logger.LogWarning("Asset with ID {BillableAssetID} not found for update.", EditInput.BillableAssetID);
                TempData["ErrorMessage"] = "Selected billable asset not found for update.";
                return RedirectToPage();
            }
            string newTrimmedPlotId = EditInput.PlotID!.Trim(); // PlotID is [Required], non-null if ModelState valid.
            // Only perform unique check and update if the PlotID has actually changed
            if (assetToUpdate.PlotID != newTrimmedPlotId)
            {
                _logger.LogInformation("PlotID changed for AssetID {AssetID}. Old: '{OldPlotID}', New attempt: '{NewPlotID}'. Checking for duplicates.",
                    EditInput.BillableAssetID, assetToUpdate.PlotID, newTrimmedPlotId);
                if (await _context.BillableAssets.AnyAsync(ba => ba.PlotID == newTrimmedPlotId && ba.BillableAssetID != EditInput.BillableAssetID))
                {
                    ModelState.AddModelError("EditInput.PlotID", "This Plot ID / Asset Identifier already exists for another asset.");
                    _logger.LogWarning("Update asset failed: Duplicate PlotID {PlotID} attempt for AssetID {AssetId}.",
                        newTrimmedPlotId, EditInput.BillableAssetID);
                    ShowEditForm = true;
                    await PopulateBillingContactUsersSL();
                    // EditInput still holds the attempted (duplicate) PlotID, so the form will show it for correction.
                    // We need to ensure the main Assets list is also reloaded for the page.
                    await OnGetAsync(); // Reloads Assets list
                    return Page();
                }
                assetToUpdate.PlotID = newTrimmedPlotId; // Update if changed and not a duplicate
            }
            // Continue with other property updates:
            assetToUpdate.UserID = string.IsNullOrWhiteSpace(EditInput.SelectedUserID) ? null : EditInput.SelectedUserID;
            assetToUpdate.Description = EditInput.Description;
            assetToUpdate.AssessmentFee = EditInput.AssessmentFee;
            assetToUpdate.LastUpdated = DateTime.UtcNow;
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated billable asset: PlotID = {PlotID}, AssetID = {AssetID}", assetToUpdate.PlotID, assetToUpdate.BillableAssetID); // assetToUpdate.PlotID is the original, unchanged PlotID
                TempData["StatusMessage"] = $"Billable Asset '{assetToUpdate.PlotID}' updated successfully.";
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error updating billable asset {PlotID} (ID: {AssetID}). It may have been modified or deleted by another user.", assetToUpdate.PlotID, assetToUpdate.BillableAssetID);
                TempData["ErrorMessage"] = "Error updating asset due to a concurrency conflict. Please refresh and try again.";
                ShowEditForm = false;
                return RedirectToPage();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error updating billable asset {PlotID} (ID: {AssetID})", assetToUpdate.PlotID, assetToUpdate.BillableAssetID);
                TempData["ErrorMessage"] = "Error updating billable asset. Check logs for details.";
                ShowEditForm = true;
                await OnGetAsync();
                return Page();
            }
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostDeleteAssetAsync(int assetId)
        {
            _logger.LogInformation("OnPostDeleteAssetAsync called for assetId: {AssetId}", assetId);
            if (assetId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid Asset ID provided for deletion.";
                _logger.LogWarning("OnPostDeleteAssetAsync called with invalid assetId: {AssetId}", assetId);
                return RedirectToPage();
            }
            var assetToDelete = await _context.BillableAssets.FindAsync(assetId);
            if (assetToDelete == null)
            {
                TempData["WarningMessage"] = $"Billable Asset with ID {assetId} not found. It may have already been deleted.";
                _logger.LogWarning("Asset with ID {AssetId} not found for deletion.", assetId);
                return RedirectToPage();
            }
            try
            {
                _context.BillableAssets.Remove(assetToDelete);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully deleted billable asset: PlotID = {PlotID}, AssetID = {AssetID}", assetToDelete.PlotID, assetToDelete.BillableAssetID);
                TempData["StatusMessage"] = $"Billable Asset '{assetToDelete.PlotID}' (ID: {assetToDelete.BillableAssetID}) has been deleted.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error deleting billable asset {PlotID} (ID: {AssetID}). It might be in use or a database error occurred.", assetToDelete.PlotID, assetToDelete.BillableAssetID);
                // Check for specific foreign key constraint issues if BillableAsset is linked elsewhere
                // For now, a general message:
                TempData["ErrorMessage"] = $"Error deleting Billable Asset '{assetToDelete.PlotID}'. It might be referenced by other records, or a database error occurred. Check logs.";
            }
            return RedirectToPage();
        }
    }
}
