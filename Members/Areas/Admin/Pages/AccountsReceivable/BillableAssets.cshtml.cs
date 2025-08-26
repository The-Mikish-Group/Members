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
using System.Text; // Added for StringBuilder
using System.Threading.Tasks;

namespace Members.Areas.Admin.Pages.AccountsReceivable
{
    [Authorize(Roles = "Admin,Manager")]
    public class BillableAssetsModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, ILogger<BillableAssetsModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<BillableAssetsModel> _logger = logger;
        public List<BillableAssetViewModel> Assets { get; set; } = [];

        [BindProperty]
        public AddBillableAssetInputModel NewAssetInput { get; set; } = new AddBillableAssetInputModel();

        [BindProperty]
        public EditAssetInputModel? EditInput { get; set; }

        public SelectList? BillingContactUsersSL { get; set; }

        // Search Property
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        // Pagination Properties
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 20; // Default page size to 20

        public int TotalAssets { get; set; }
        public int TotalPages { get; set; }

        // Properties for Sort State
        [BindProperty(SupportsGet = true)]
        public string? CurrentSort { get; set; }

        // Individual XyzSort properties removed
        public class EditAssetInputModel
        {
            [Required]
            public int BillableAssetID { get; set; }

            [Required(ErrorMessage = "Asset Identifier is required.")]
            [StringLength(100)]
            [Display(Name = "Asset Identifier")]
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
            [StringLength(100)]
            [Display(Name = "Asset Identifier")]
            public string PlotID { get; set; } = string.Empty;

            [Display(Name = "Assign to Billing Contact")]
            public string SelectedUserID { get; set; } = string.Empty;

            [StringLength(250)]
            [Display(Name = "Optional Description")]
            public string? Description { get; set; }

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

        private async Task LoadAssetsDataAsync()
        {
            _logger.LogInformation("LoadAssetsDataAsync called. SearchTerm: {SearchTerm}, PageNumber: {PageNumber}, PageSize: {PageSize}, CurrentSort: {CurrentSort}", SearchTerm, PageNumber, PageSize, CurrentSort);
            // Start with BillableAssets
            IQueryable<BillableAsset> baseAssetQuery = _context.BillableAssets;
            // Join with IdentityUser (for ba.User)
            var queryWithUser = baseAssetQuery
                .Select(ba => new
                {
                    BillableAsset = ba,
                    ba.User // This is the IdentityUser linked to BillableAsset
                });
            // Now, GroupJoin with UserProfile on User.Id == UserProfile.UserId
            var queryWithJoinedData = queryWithUser
                .GroupJoin(
                    _context.UserProfile, // Changed to singular UserProfile
                    outer => outer.User != null ? outer.User.Id : null,
                    userProfile => userProfile.UserId,
                    (outer, profiles) => new
                    {
                        outer.BillableAsset,
                        outer.User, // IdentityUser
                        UserProfile = profiles.FirstOrDefault() // UserProfile or null
                    }
                );
            // Apply Filtering
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                string searchTermLower = SearchTerm.ToLower().Trim();
                queryWithJoinedData = queryWithJoinedData.Where(item =>
                    (item.BillableAsset.PlotID != null && item.BillableAsset.PlotID.ToLower().Contains(searchTermLower)) ||
                    (item.BillableAsset.Description != null && item.BillableAsset.Description.ToLower().Contains(searchTermLower)) ||
                    (item.UserProfile != null && (
                        (item.UserProfile.FirstName != null && item.UserProfile.FirstName.ToLower().Contains(searchTermLower)) ||
                        (item.UserProfile.LastName != null && item.UserProfile.LastName.ToLower().Contains(searchTermLower))
                    )) ||
                    (item.User != null && item.User.Email != null && item.User.Email.ToLower().Contains(searchTermLower))
                );
            }
            TotalAssets = await queryWithJoinedData.CountAsync();
            _logger.LogInformation("Total assets after filtering: {TotalAssets}", TotalAssets);
            TotalPages = (int)Math.Ceiling(TotalAssets / (double)PageSize);
            if (PageNumber < 1) PageNumber = 1;
            if (PageNumber > TotalPages && TotalPages > 0) PageNumber = TotalPages;
            else if (TotalPages == 0) PageNumber = 1;
            _logger.LogInformation("[BACKEND_DEBUG] LoadAssetsDataAsync - TotalAssets: {TotalAssets}, PageSize: {PageSize}, Calculated TotalPages: {TotalPages}, Final PageNumber: {PageNumber}", TotalAssets, PageSize, TotalPages, PageNumber);
            string activeSort = CurrentSort ?? "contact_asc"; // Default sort
            this.CurrentSort = activeSort; // Ensure CurrentSort property is set for UI links
            var assetsSortableQuery = queryWithJoinedData;
            var orderedQueryable = activeSort switch
            {
                "plotid_desc" => assetsSortableQuery.OrderByDescending(item => item.BillableAsset.PlotID),
                "plotid_asc" => assetsSortableQuery.OrderBy(item => item.BillableAsset.PlotID),
                "contact_desc" => assetsSortableQuery.OrderByDescending(item => item.UserProfile != null && item.UserProfile.LastName != null && item.UserProfile.FirstName != null ? (item.UserProfile.LastName + ", " + item.UserProfile.FirstName) : (item.User != null ? item.User.Email : null)),
                "contact_asc" => assetsSortableQuery.OrderBy(item => item.UserProfile != null && item.UserProfile.LastName != null && item.UserProfile.FirstName != null ? (item.UserProfile.LastName + ", " + item.UserProfile.FirstName) : (item.User != null ? item.User.Email : null)),
                "desc_desc" => assetsSortableQuery.OrderByDescending(item => item.BillableAsset.Description),
                "desc_asc" => assetsSortableQuery.OrderBy(item => item.BillableAsset.Description),
                "created_desc" => assetsSortableQuery.OrderByDescending(item => item.BillableAsset.DateCreated),
                "created_asc" => assetsSortableQuery.OrderBy(item => item.BillableAsset.DateCreated),
                "updated_desc" => assetsSortableQuery.OrderByDescending(item => item.BillableAsset.LastUpdated),
                "updated_asc" => assetsSortableQuery.OrderBy(item => item.BillableAsset.LastUpdated),
                "fee_desc" => assetsSortableQuery.OrderByDescending(item => item.BillableAsset.AssessmentFee),
                "fee_asc" => assetsSortableQuery.OrderBy(item => item.BillableAsset.AssessmentFee),
                _ => assetsSortableQuery.OrderBy(item => item.UserProfile != null && item.UserProfile.LastName != null && item.UserProfile.FirstName != null ? (item.UserProfile.LastName + ", " + item.UserProfile.FirstName) : (item.User != null ? item.User.Email : null)) // Default
            };
            _logger.LogInformation("Applied IQueryable sorting based on: {ActiveSort}", activeSort);
            var paginatedResults = await orderedQueryable
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
            _logger.LogInformation("Fetched {PaginatedCount} assets after pagination.", paginatedResults.Count);
            Assets = [];
            foreach (var item in paginatedResults)
            {
                var assetEntity = item.BillableAsset;
                var user = item.User;
                var userProfile = item.UserProfile;
                string? contactFullName = null;
                string? contactEmail = null;
                if (user != null)
                {
                    contactEmail = user.Email;
                    if (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                    {
                        contactFullName = $"{userProfile.LastName}, {userProfile.FirstName}";
                    }
                    else
                    {
                        contactFullName = user.UserName;
                    }
                }
                Assets.Add(new BillableAssetViewModel
                {
                    BillableAssetID = assetEntity.BillableAssetID,
                    PlotID = assetEntity.PlotID,
                    UserID = assetEntity.UserID,
                    BillingContactFullName = contactFullName ?? "N/A (Unassigned)",
                    BillingContactEmail = contactEmail,
                    DateCreated = assetEntity.DateCreated,
                    LastUpdated = assetEntity.LastUpdated,
                    Description = assetEntity.Description,
                    AssessmentFee = assetEntity.AssessmentFee
                });
            }
            _logger.LogInformation("Populated BillableAssetViewModel with {AssetCount} assets.", Assets.Count);
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("ManageBillableAssets OnGetAsync called.");
            await LoadAssetsDataAsync();
            // Logic for setting individual XyzSort properties removed.
            // CurrentSort is the single source of truth, managed by LoadAssetsDataAsync and client-side.
            _logger.LogInformation("Sorting state for OnGetAsync. CurrentSort: {CurrentSort}", CurrentSort);
            await PopulateBillingContactUsersSL();
            _logger.LogInformation("Finished OnGetAsync. Loaded {AssetCount} billable assets for display. TotalAssets: {TotalOverallAssets}, TotalPages: {TotalPageCount}", Assets.Count, TotalAssets, TotalPages);
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
                    ModelState.AddModelError("NewAssetInput.PlotID", "Asset Identifier is required.");
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
                ModelState.AddModelError("NewAssetInput.PlotID", "This Asset Identifier already exists.");
                _logger.LogWarning("Add new asset failed: Duplicate Asset Identifier {PlotID}.", trimmedPlotId);
                await PopulateBillingContactUsersSL();
                await OnGetAsync();
                NewAssetInput.PlotID = trimmedPlotId;
                // ShowEditForm = false; // Removed
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
                TempData["AssetStatusMessage"] = $"Billable Asset '{newAsset.PlotID}' added successfully and assigned to the selected contact.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving new billable asset {PlotID}", NewAssetInput.PlotID);
                // Check for unique constraint violation specifically if possible, though general message is okay too
                if (ex.InnerException?.Message.Contains("Cannot insert duplicate key row") == true && ex.InnerException.Message.Contains("IX_BillableAssets_PlotID"))
                {
                    ModelState.AddModelError("NewAssetInput.PlotID", "This Asset Identifier already exists. It might have been added by someone else concurrently.");
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
            _logger.LogInformation("OnGetShowEditFormAsync called for assetId: {AssetId} to fetch data for modal", assetId);
            var assetToEdit = await _context.BillableAssets.FindAsync(assetId);
            if (assetToEdit == null)
            {
                _logger.LogWarning("Asset with ID {AssetId} not found for editing.", assetId);
                return NotFound(new { message = "Selected billable asset not found." });
            }
            var editInputData = new EditAssetInputModel
            {
                BillableAssetID = assetToEdit.BillableAssetID,
                PlotID = assetToEdit.PlotID,
                SelectedUserID = assetToEdit.UserID,
                Description = assetToEdit.Description,
                AssessmentFee = assetToEdit.AssessmentFee
            };
            _logger.LogInformation("Returning Json data for AssetID {AssetId} (PlotID: {PlotID_Bound})", assetId, editInputData.PlotID);
            return new JsonResult(editInputData);
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
                            _logger.LogWarning("ModelState Key: {modelStateKey}, Error: {error.ErrorMessage}, Exception: {error.Exception?.Message}", modelStateKey, error.ErrorMessage, error.Exception?.Message);
                        }
                    }
                }
                // ShowEditForm = true; // Removed
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
            string newTrimmedPlotId = EditInput.PlotID!.Trim();
            if (assetToUpdate.PlotID != newTrimmedPlotId)
            {
                _logger.LogInformation("PlotID changed for AssetID {AssetID}. Old: '{OldPlotID}', New attempt: '{NewPlotID}'. Checking for duplicates.",
                    EditInput.BillableAssetID, assetToUpdate.PlotID, newTrimmedPlotId);
                if (await _context.BillableAssets.AnyAsync(ba => ba.PlotID == newTrimmedPlotId && ba.BillableAssetID != EditInput.BillableAssetID))
                {
                    ModelState.AddModelError("EditInput.PlotID", "This Asset Identifier already exists for another asset.");
                    _logger.LogWarning("Update asset failed: Duplicate Asset Identifier {PlotID} attempt for AssetID {AssetId}.",
                        newTrimmedPlotId, EditInput.BillableAssetID);
                    await PopulateBillingContactUsersSL();
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
                TempData["AssetStatusMessage"] = $"Billable Asset '{assetToUpdate.PlotID}' updated successfully.";
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error updating billable asset {PlotID} (ID: {AssetID}). It may have been modified or deleted by another user.", assetToUpdate.PlotID, assetToUpdate.BillableAssetID);
                TempData["ErrorMessage"] = "Error updating asset due to a concurrency conflict. Please refresh and try again.";

                return RedirectToPage();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error updating billable asset {PlotID} (ID: {AssetID})", assetToUpdate.PlotID, assetToUpdate.BillableAssetID);
                TempData["ErrorMessage"] = "Error updating billable asset. Check logs for details.";

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
                TempData["AssetStatusMessage"] = $"Billable Asset '{assetToDelete.PlotID}' (ID: {assetToDelete.BillableAssetID}) has been deleted.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error deleting billable asset {PlotID} (ID: {AssetID}). It might be in use or a database error occurred.", assetToDelete.PlotID, assetToDelete.BillableAssetID);
                TempData["ErrorMessage"] = $"Error deleting Billable Asset '{assetToDelete.PlotID}'. It might be referenced by other records, or a database error occurred. Check logs.";
            }
            return RedirectToPage();
        }

        public async Task<PartialViewResult> OnGetPartialTableAsync(string? searchTerm, int pageNumber, int pageSize, string? currentSort)
        {
            // Set model properties from parameters
            SearchTerm = searchTerm;
            PageNumber = pageNumber;
            PageSize = pageSize;
            CurrentSort = currentSort;
            _logger.LogInformation("OnGetPartialTableAsync called. SearchTerm: {SearchTerm}, PageNumber: {PageNumber}, PageSize: {PageSize}, CurrentSort: {CurrentSort}", SearchTerm, PageNumber, PageSize, CurrentSort);
            await LoadAssetsDataAsync();
            _logger.LogInformation("[BACKEND_DEBUG] OnGetPartialTableAsync - Passing to partial - TotalAssets: {TotalAssets}, TotalPages: {TotalPages}, PageNumber: {PageNumber}, PageSize: {PageSize}, SearchTerm: {SearchTerm}, CurrentSort: {CurrentSort}", this.TotalAssets, this.TotalPages, this.PageNumber, this.PageSize, this.SearchTerm, this.CurrentSort);
            return Partial("_AssetsTablePartial", this);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }

        public async Task<IActionResult> OnGetExportCsvAsync()
        {
            _logger.LogInformation("[ManageBillableAssets Export CSV] Handler started.");

            try
            {
                // Fetch all BillableAssets with related User and UserProfile data
                var allAssetsQuery = _context.BillableAssets
                    .Select(ba => new
                    {
                        BillableAsset = ba,
                        ba.User // IdentityUser linked to BillableAsset
                    })
                    .GroupJoin(
                        _context.UserProfile,
                        outer => outer.User != null ? outer.User.Id : null,
                        userProfile => userProfile.UserId,
                        (outer, profiles) => new
                        {
                            outer.BillableAsset,
                            outer.User, // IdentityUser
                            UserProfile = profiles.FirstOrDefault() // UserProfile or null
                        }
                    );

                var assetsToExportData = await allAssetsQuery.ToListAsync();
                _logger.LogInformation("[ManageBillableAssets Export CSV] Fetched {AssetCount} assets for export.", assetsToExportData.Count);

                if (assetsToExportData.Count == 0)
                {
                    _logger.LogWarning("[ManageBillableAssets Export CSV] No billable assets found to export.");
                    // Let it proceed to generate an empty CSV for now.
                }

                var sb = new StringBuilder();
                sb.AppendLine("\"BillableAssetID\",\"PlotID\",\"BillingContactFullName\",\"BillingContactEmail\",\"AssessmentFee\",\"Description\",\"DateCreated\",\"LastUpdated\"");

                foreach (var item in assetsToExportData)
                {
                    var assetEntity = item.BillableAsset;
                    var user = item.User;
                    var userProfile = item.UserProfile;

                    string? contactFullName = "N/A (Unassigned)";
                    string? contactEmail = null;

                    if (user != null)
                    {
                        contactEmail = user.Email;
                        if (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                        {
                            contactFullName = $"{userProfile.LastName}, {userProfile.FirstName}";
                        }
                        else
                        {
                            contactFullName = user.UserName;
                        }
                    }

                    sb.AppendFormat("\"{0}\",", assetEntity.BillableAssetID);
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(assetEntity.PlotID));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(contactFullName));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(contactEmail));
                    sb.AppendFormat("{0},", assetEntity.AssessmentFee.ToString("F2"));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(assetEntity.Description));
                    sb.AppendFormat("\"{0}\",", assetEntity.DateCreated.ToString("yyyy-MM-dd"));
                    sb.AppendFormat("\"{0}\"", assetEntity.LastUpdated.ToString("yyyy-MM-dd"));
                    sb.AppendLine();
                }

                byte[] csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
                string fileName = $"billable_assets_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                _logger.LogInformation("[ManageBillableAssets Export CSV] CSV string generated. Byte length: {Length}. Filename: {FileName}", csvBytes.Length, fileName);

                if (csvBytes.Length <= sb.ToString().Split(Environment.NewLine)[0].Length + 2 && assetsToExportData.Count == 0)
                {
                     _logger.LogWarning("[ManageBillableAssets Export CSV] CSV is empty or contains only header. This might not trigger a download.");
                }

                return File(csvBytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ManageBillableAssets Export CSV] CRITICAL ERROR during CSV export generation.");
                TempData["ErrorMessage"] = "A critical error occurred while generating the CSV export for Billable Assets. Please check the logs.";
                return RedirectToPage(); // Redirect back to the page with an error message
            }
        }
    }
}