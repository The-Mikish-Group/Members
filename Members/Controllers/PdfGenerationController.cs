using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using System.Text;

// This controller is dedicated to generating PDF files, specifically the Member Directory PDF, using Syncfusion PDF Library.
// It also handles related data export functionality.

namespace Members.Controllers;

[Authorize(Roles = "Admin,Manager")]
public class PdfGenerationController : Controller
{
    // Injected dependencies for database access, environment info, logging, user management.
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PdfGenerationController> _logger;
    private readonly ApplicationDbContext _context;

    // Injected services for View Rendering, TempData handling, and ActionContext access
    private readonly IRazorViewEngine _razorViewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IActionContextAccessor _actionContextAccessor;

    // Define the base path for protected files where PDFs will be saved
    private readonly string _protectedFilesBasePath;

    // Constructor to inject the required services
    public PdfGenerationController(
        IWebHostEnvironment environment,
        ILogger<PdfGenerationController> logger,
        ApplicationDbContext context,

        IRazorViewEngine razorViewEngine,
        ITempDataProvider tempDataProvider,
        IActionContextAccessor actionContextAccessor)
    {
        _environment = environment;
        _logger = logger;
        _context = context;

        _razorViewEngine = razorViewEngine;
        _tempDataProvider = tempDataProvider;
        _actionContextAccessor = actionContextAccessor;

        // Set the base path for protected files in the constructor
        _protectedFilesBasePath = Path.Combine(_environment.ContentRootPath, "ProtectedFiles");

    }

    // GET: /PdfGeneration/CreatePdf - Displays the form for generating PDF (MODIFIED to hardcode Directory)
    [HttpGet]
    public async Task<IActionResult> CreatePdf() // Use async because we fetch data from DB
    {
        ViewData["Title"] = "New Directory PDF";

        // Find the "Directory" category in the database
        var directoryCategory = await _context.PDFCategories.FirstOrDefaultAsync(c => c.CategoryName == "Directory");

        // If the "Directory" category does not exist, show an error and redirect
        if (directoryCategory == null)
        {
            _logger.LogError("Directory category not found for PDF generation.");
            TempData["ErrorMessage"] = "The 'Directory' category was not found in the database. Please ensure it exists to generate the directory PDF.";
            // Redirect the user to the category management page or a specific error view
            return RedirectToAction("ManageCategories", "PdfCategory"); // Example: Redirect to AdminController.ManageCategories
        }

        // Calculate the next suggested sort order specifically for the "Directory" category
        int initialSuggestedSortOrder = 1;
        var maxSortOrder = await _context.CategoryFiles
           .Where(cf => cf.CategoryID == directoryCategory.CategoryID)
           .MaxAsync(cf => (int?)cf.SortOrder);

        initialSuggestedSortOrder = (maxSortOrder ?? 0) + 1;

        // Create the ViewModel, passing ONLY the Directory Category ID and Suggested Sort Order
        var viewModel = new CreatePdfFormViewModel
        {
            DirectoryCategoryId = directoryCategory.CategoryID, // Pass the found Directory Category ID
            SuggestedSortOrder = initialSuggestedSortOrder // Pass the calculated suggested sort order                                                           
        };

        // Return the CreatePdf view with the populated ViewModel
        return View(viewModel);
    }

    // POST: /PdfGeneration/CreatePdf - Handles form submission and PDF creation (using Syncfusion PDF Library)
    [HttpPost] // Use POST for form submission
    [ValidateAntiForgeryToken] // Protect against Cross-Site Request Forgery
    public async Task<IActionResult> CreatePdf(CreatePdfPostModel model) // Accept the POST ViewModel
    {
        // Use TempData for messages as we are redirecting away from this action to the AdminController
        // The CategoryId is expected to be provided by a hidden field for the Directory category
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("PDF creation failed due to invalid model state.");
            TempData["ErrorMessage"] = "Invalid input. Please check the file name and sort order.";
            // Redirect back to the GET action to re-display the form with errors and the Directory category data
            return RedirectToAction(nameof(CreatePdf));
        }

        // *** FILE NAME HANDLING - PRESERVES SPACES ***
        string originalFileName = model.FileName;
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string cleanFileName = new([.. originalFileName.Where(c => !invalidChars.Contains(c))]);
        string databaseAndPhysicalFileName = cleanFileName;
        if (!databaseAndPhysicalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            databaseAndPhysicalFileName += ".pdf";
        }
        string databaseFileName = databaseAndPhysicalFileName;
        string filePath = Path.Combine(_protectedFilesBasePath, databaseFileName);

        byte[]? pdfBytes = null;

        // We only want Members with the "Member" role, excluding those with "Admin" or "Manager" roles.
        try
        {
            string? memberRoleId = await _context.Roles.Where(r => r.Name == "Member").Select(r => r.Id).FirstOrDefaultAsync();
            string? adminRoleId = await _context.Roles.Where(r => r.Name == "Admin").Select(r => r.Id).FirstOrDefaultAsync();
            string? managerRoleId = await _context.Roles.Where(r => r.Name == "Manager").Select(r => r.Id).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(memberRoleId))
            {
                _logger.LogWarning("PDF generation aborted: 'Member' role not found.");
                TempData["ErrorMessage"] = "'Member' role not found. Cannot generate filtered directory.";
                return RedirectToAction(nameof(CreatePdf));
            }

            List<string> memberUserIds = await _context.UserRoles.Where(ur => ur.RoleId == memberRoleId).Select(ur => ur.UserId).ToListAsync();
            List<string> excludedUserIds = await _context.UserRoles
                .Where(ur => (adminRoleId != null && ur.RoleId == adminRoleId) || (managerRoleId != null && ur.RoleId == managerRoleId))
                .Select(ur => ur.UserId)
                .ToListAsync();
            List<UserProfile> userProfilesWithUsers = await _context.UserProfile
                .Include(up => up.User)
                .Where(up => up.User != null
                             && !string.IsNullOrEmpty(up.FirstName)
                             && !string.IsNullOrEmpty(up.LastName)
                             && memberUserIds.Contains(up.UserId!)
                             && !excludedUserIds.Contains(up.UserId!)
                       )
                .OrderBy(up => up.LastName)
                .ThenBy(up => up.FirstName)
                .ToListAsync();

            _logger.LogInformation("Finished fetching user profiles for PDF. Count: {Count}", userProfilesWithUsers.Count);
            _context.ChangeTracker.Clear();
            _logger.LogInformation("DbContext change tracker cleared.");

            if (userProfilesWithUsers.Count == 0)
            {
                _logger.LogWarning("PDF generation aborted: No user profiles found matching filter.");
                TempData["ErrorMessage"] = "No users found matching the role filter to generate the directory.";
                return RedirectToAction(nameof(CreatePdf));
            }

            _logger.LogInformation("Starting PDF generation using Syncfusion PDF Library...");

            // --- Start of Syncfusion PDF Generation
            using (PdfDocument document = new())
            {
                PdfPage page = document.Pages.Add();
                PdfGraphics graphics = page.Graphics;

                PdfFont regularFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12);

                // Keep for user names
                PdfFont boldFont = new PdfStandardFont(PdfFontFamily.Helvetica, 13, PdfFontStyle.Bold);
                PdfFont boldHeadingFont = new PdfStandardFont(PdfFontFamily.Helvetica, 15, PdfFontStyle.Bold);
                PdfFont HeadingFont = new PdfStandardFont(PdfFontFamily.Helvetica, 15);

                // Regular footer font
                PdfFont footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12);

                // Define a bold font specifically for the footer page numbers
                PdfFont boldFooterFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold); // Bold footer font

                PdfBrush brush = PdfBrushes.Black;

                // Define margins (adjust as needed)
                float horizontalMargin = 20;
                float verticalMargin = 20;

                PdfStringFormat format = new() { WordWrap = PdfWordWrapType.Word };

                // This defines the main content area rectangle
                Syncfusion.Drawing.RectangleF bounds = new(horizontalMargin, verticalMargin, page.GetClientSize().Width - (2 * horizontalMargin), page.GetClientSize().Height - (2 * verticalMargin));

                int numberOfColumns = 2;
                float columnSpacing = horizontalMargin / 2;
                float columnWidth = (bounds.Width - (columnSpacing)) / numberOfColumns;

                // Initial currentY is set for drawing the main title area with a reduced top margin
                float titleTopPosition = verticalMargin; // Position the top of the title block at the top vertical margin

                float currentX = bounds.Left;
                int currentColumn = 0;

                //PdfFont headingFont = new PdfStandardFont(PdfFontFamily.Helvetica, 13 /*PdfFontStyle.Bold*/);

                // --- Image Loading (Load once outside the page loop) ---
                PdfImage? logoInstance = null;
                string logoPath = Path.Combine(_environment.WebRootPath, "Images", "LinkImages", "SmallLogo.png");
                if (System.IO.File.Exists(logoPath))
                {
                    try
                    {
                        using (FileStream imageStream = new(logoPath, FileMode.Open, FileAccess.Read))
                        {
                            logoInstance = PdfImage.FromStream(imageStream);
                        }
                        _logger.LogInformation("Logo image loaded successfully from {Path}", logoPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading logo image from {Path}: {ErrorMessage}", logoPath, ex.Message);
                        // Continue without logo if loading fails
                        logoInstance = null;
                    }
                }
                else
                {
                    _logger.LogWarning("Logo image not found at {Path}. PDF will be generated without logo.", logoPath);
                }
                // --- End Image Loading ---

                float logoWidth = 30; // Declare logoWidth here
                float logoHeight = 30; // Declare logoHeight here
                float spacingBetweenLogoAndText = 5; // Space between the logo and the title text

                // --- Draw Heading and Logo on the FIRST Page ---
                // Restored to original first page heading logic
                string firstPageHeadingText = "Homeowners Association Directory";
                SizeF firstPageHeadingSize = boldHeadingFont.MeasureString(firstPageHeadingText);

                // Calculate the total width of the logo + spacing + text for centering
                float firstPageHeadingBlockWidth = (logoInstance != null ? logoWidth + spacingBetweenLogoAndText : 0) + firstPageHeadingSize.Width;

                // Calculate the X position to center the entire block (logo + text) within the bounds
                float firstPageBlockX = bounds.Left + (bounds.Width - firstPageHeadingBlockWidth) / 2;

                // Calculate the X position for the logo within the centered block
                float firstPageLogoX = firstPageBlockX;

                // Calculate the X position for the text within the centered block
                float firstPageTextX = firstPageBlockX + (logoInstance != null ? logoWidth + spacingBetweenLogoAndText : 0);


                // Draw the logo on the first page if loaded
                if (logoInstance != null)
                {
                    // Position the logo to align its top with the heading text's vertical center
                    float logoY = titleTopPosition + (boldFont.Height - logoHeight) / 2;
                    graphics.DrawImage(logoInstance, firstPageLogoX, logoY, logoWidth, logoHeight);
                }

                // Draw the heading text on the first page
                graphics.DrawString(firstPageHeadingText, boldHeadingFont, brush, new PointF(firstPageTextX, titleTopPosition));

                // Calculate the starting Y position for the content blocks after the heading on the first page
                float contentStartY = titleTopPosition + Math.Max(logoInstance != null ? logoHeight : 0, boldHeadingFont.Height) + verticalMargin;
                float currentY = contentStartY; // Set initial currentY to the content start positio

                // --- Iterate through user data and add to the PDF ---
                foreach (UserProfile userProfile in userProfilesWithUsers)
                {
                    // Construct the full name including middle name with correct spacing
                    string middleName = string.IsNullOrEmpty(userProfile.MiddleName) ? " " : $" {userProfile.MiddleName} ";
                    string nameLine = $"{userProfile.FirstName}{middleName}{userProfile.LastName}";

                    // Measure the height of the full name line in bold
                    SizeF fullNameSize = boldFont.MeasureString(nameLine, columnWidth, format);

                    // Calculate height of remaining data WITHOUT Plot
                    StringBuilder remainingUserDataCheck = new();
                    if (!string.IsNullOrEmpty(userProfile.AddressLine1)) remainingUserDataCheck.AppendLine(userProfile.AddressLine1);
                    if (!string.IsNullOrEmpty(userProfile.AddressLine2)) remainingUserDataCheck.AppendLine(userProfile.AddressLine2);
                    remainingUserDataCheck.AppendLine($"{userProfile.City}, {userProfile.State} {userProfile.ZipCode}");
                    if (!string.IsNullOrEmpty(userProfile.User?.PhoneNumber)) remainingUserDataCheck.AppendLine($"Cell Phone: {userProfile.User.PhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.HomePhoneNumber)) remainingUserDataCheck.AppendLine($"Home Phone: {userProfile.HomePhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.User?.Email)) remainingUserDataCheck.AppendLine($"Email: {userProfile.User.Email}");
                    if (userProfile.Birthday.HasValue) remainingUserDataCheck.AppendLine($"Birthday: {userProfile.Birthday.Value.ToShortDateString()}");
                    if (userProfile.Anniversary.HasValue) remainingUserDataCheck.AppendLine($"Anniversary: {userProfile.Anniversary.Value.ToShortDateString()}");

                    SizeF remainingTextSizeCheck = regularFont.MeasureString(remainingUserDataCheck.ToString(), columnWidth, format);

                    float totalBlockHeight = fullNameSize.Height + remainingTextSizeCheck.Height;


                    // Check if the current block fits in the current column
                    if (currentY + totalBlockHeight + (verticalMargin / 2) > bounds.Bottom)
                    {
                        currentColumn++;
                        if (currentColumn < numberOfColumns)
                        {
                            // Move to the next column on the same page
                            currentX = bounds.Left + (currentColumn * (columnWidth + columnSpacing));
                            currentY = contentStartY;
                        }
                        else
                        {
                            // Move to a new page
                            page = document.Pages.Add();
                            graphics = page.Graphics;
                            currentX = bounds.Left;
                            currentY = bounds.Top;
                            currentColumn = 0;

                            // --- Draw Heading and Logo on SUBSEQUENT Pages ---
                            // Separate the heading text and the date for subsequent pages                            
                            string boldContinuedHeadingText = "HOA Member Directory";
                            Syncfusion.Drawing.SizeF boldContinuedHeadingTextSize = boldHeadingFont.MeasureString(boldContinuedHeadingText);

                            string continuedHeadingText = " (Continued)";
                            Syncfusion.Drawing.SizeF continuedHeadingTextSize = regularFont.MeasureString(continuedHeadingText);


                            // Calculate the total width of the logo + spacing + static text + date text for centering
                            float continuedHeadingBlockWidth = (logoInstance != null ? logoWidth + spacingBetweenLogoAndText : 0) + boldContinuedHeadingTextSize.Width + continuedHeadingTextSize.Width;

                            // Calculate the X position to center the entire block (logo + text) within the bounds
                            float continuedBlockX = bounds.Left + (bounds.Width - continuedHeadingBlockWidth) / 2;

                            // Calculate the X position for the logo within the centered block
                            float continuedLogoX = continuedBlockX;

                            // Calculate the X position for the static text within the centered block
                            float boldContinuedHeadingTextX = continuedBlockX + (logoInstance != null ? logoWidth + spacingBetweenLogoAndText : 0);

                            // Calculate the X position for the 'Continue' text after the static text
                            float continuedHeadingTextX = boldContinuedHeadingTextX + boldContinuedHeadingTextSize.Width;

                            // Draw the logo on the new page if loaded
                            if (logoInstance != null)
                            {
                                // Position the logo to align its top with the heading text's vertical center (using boldHeadingFont height)
                                float logoY = titleTopPosition + (boldHeadingFont.Height - logoHeight) / 2;
                                graphics.DrawImage(logoInstance, continuedLogoX, logoY, logoWidth, logoHeight);
                            }

                            // Draw the static heading text on the new page using the bold heading font
                            graphics.DrawString(boldContinuedHeadingText, boldHeadingFont, brush, new PointF(boldContinuedHeadingTextX, titleTopPosition)); // Use boldHeadingFont

                            // Draw the Continue text on the new page using the regular font
                            graphics.DrawString(continuedHeadingText, HeadingFont, brush, new PointF(continuedHeadingTextX, titleTopPosition)); // Use regularFont


                            // Move currentY below the heading block (using the maximum height of logo or text)
                            float bottomOfContinuedHeadingBlock = titleTopPosition + Math.Max(logoInstance != null ? logoHeight : 0, boldHeadingFont.Height); // Use boldHeadingFont.Height
                            currentY = bottomOfContinuedHeadingBlock + verticalMargin; // Add vertical margin below the heading

                            // --- End Draw Heading and Logo on SUBSEQUENT Pages ---
                        }
                    }

                    graphics.DrawString(nameLine, boldFont, brush, new Syncfusion.Drawing.RectangleF(currentX, currentY, columnWidth, fullNameSize.Height), format);
                    currentY += fullNameSize.Height;

                    StringBuilder remainingUserData = new();
                    if (!string.IsNullOrEmpty(userProfile.AddressLine1)) remainingUserData.AppendLine(userProfile.AddressLine1);
                    if (!string.IsNullOrEmpty(userProfile.AddressLine2)) remainingUserData.AppendLine(userProfile.AddressLine2);
                    remainingUserData.AppendLine($"{userProfile.City}, {userProfile.State} {userProfile.ZipCode}");
                    if (!string.IsNullOrEmpty(userProfile.User?.PhoneNumber)) remainingUserData.AppendLine($"Cell Phone: {userProfile.User.PhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.HomePhoneNumber)) remainingUserData.AppendLine($"Home Phone: {userProfile.HomePhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.User?.Email)) remainingUserData.AppendLine($"Email: {userProfile.User.Email}");
                    if (userProfile.Birthday.HasValue)
                    {
                        var month = userProfile.Birthday.Value.ToString("MMMM");
                        var dayWithSuffix = GetDayWithSuffix(userProfile.Birthday.Value);
                        var birthdayString = $"{month} {dayWithSuffix}";
                        remainingUserData.AppendLine($"Birthday: {birthdayString}");
                    }
                    if (userProfile.Anniversary.HasValue)
                    {
                        var month = userProfile.Anniversary.Value.ToString("MMMM");
                        var dayWithSuffix = GetDayWithSuffix(userProfile.Anniversary.Value);
                        var birthdayString = $"{month} {dayWithSuffix}";
                        remainingUserData.AppendLine($"Anniversary: {birthdayString}");
                    }

                    Syncfusion.Drawing.SizeF remainingTextSize = regularFont.MeasureString(remainingUserData.ToString(), columnWidth, format);
                    graphics.DrawString(remainingUserData.ToString(), regularFont, brush, new Syncfusion.Drawing.RectangleF(currentX, currentY, columnWidth, remainingTextSize.Height), format);

                    // MODIFIED: Increased verticalMargin for spacing after user block
                    currentY += remainingTextSize.Height + (verticalMargin / 2); // Increased spacing here
                }

                //footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9); // Keep original font definition
                PdfStringFormat footerFormat = new()
                {
                    // Restored PdfTextAlignment
                    Alignment = PdfTextAlignment.Right, // Keep right alignment for footer
                    LineAlignment = PdfVerticalAlignment.Bottom
                };

                string currentDate = DateTime.Now.ToString("MMMM dd,yyyy");

                for (int i = 0; i < document.Pages.Count; i++)
                {
                    PdfPage currentPage = document.Pages[i];
                    PdfGraphics currentPageGraphics = currentPage.Graphics;

                    // Define footer bounds using horizontal and vertical margins
                    Syncfusion.Drawing.RectangleF footerBounds = new(horizontalMargin, currentPage.GetClientSize().Height - verticalMargin, currentPage.GetClientSize().Width - (2 * horizontalMargin), verticalMargin);

                    // Separate footer parts
                    string footerDateText = currentDate;
                    // Restored original spacing and text for footer
                    string footerPageStaticText = "  Page: " + (i + 1).ToString();


                    // Measure the size of each part using the correct font
                    SizeF footerDateSize = footerFont.MeasureString(footerDateText); // Use regular footer font
                    SizeF footerPageStaticSize = boldFooterFont.MeasureString(footerPageStaticText); // Use bold footer font


                    // Calculate the total width of the footer text parts
                    float totalFooterTextWidth = footerDateSize.Width + footerPageStaticSize.Width;

                    // Calculate the starting X position for the footer text to be right-aligned within the bounds
                    float footerTextStartX = footerBounds.Left + footerBounds.Width - totalFooterTextWidth;

                    // Draw the date part (regular font)
                    currentPageGraphics.DrawString(footerDateText, footerFont, brush, new PointF(footerTextStartX, footerBounds.Top));

                    // Draw the static " - Page " part (regular font) after the date
                    float footerPageStaticX = footerTextStartX + footerDateSize.Width;
                    currentPageGraphics.DrawString(footerPageStaticText, boldFooterFont, brush, new PointF(footerPageStaticX, footerBounds.Top));

                }

                // --- End of PDF Generation ---

                // Save the document to a MemoryStream
                using MemoryStream stream = new();
                document.Save(stream);
                pdfBytes = stream.ToArray();
            }

            _logger.LogInformation("PDF generation using Syncfusion completed successfully.");

            if (!Directory.Exists(_protectedFilesBasePath))
            {
                try
                {
                    Directory.CreateDirectory(_protectedFilesBasePath);
                    _logger.LogInformation("Created ProtectedFiles directory: {Path}", _protectedFilesBasePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating ProtectedFiles directory: {ErrorMessage}", ex.Message);
                    TempData["ErrorMessage"] = $"Error creating necessary directory on the server: {ex.Message}";
                    return RedirectToAction(nameof(CreatePdf));
                }
            }

            bool physicalFileExisted = System.IO.File.Exists(filePath);
            if (physicalFileExisted)
            {
                _logger.LogInformation("Physical file {FilePath} already exists. It will be overwritten.", filePath);
            }

            try
            {
                _logger.LogInformation("Attempting to save PDF file to {FilePath}...", filePath);
                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);
                    _logger.LogInformation("PDF file saved/overwritten successfully to {FilePath}", filePath);
                }
                else
                {
                    _logger.LogError("PDF bytes were null or empty. File not saved.");
                    TempData["ErrorMessage"] = "PDF generation failed. Could not save physical file.";
                    return RedirectToAction(nameof(CreatePdf));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving/overwriting PDF file to {FilePath}: {ErrorMessage}", filePath, ex.Message);
                TempData["ErrorMessage"] = $"Error saving/overwriting PDF file to the server: {ex.Message}";
                return RedirectToAction(nameof(CreatePdf));
            }

            _logger.LogInformation("Checking if CategoryFile entry already exists for file {FileName} in category {CategoryId} in the database...", databaseFileName, model.CategoryId);
            CategoryFile? existingCategoryFile = await _context.CategoryFiles
                .FirstOrDefaultAsync(cf => cf.CategoryID == model.CategoryId && cf.FileName == databaseFileName);

            _logger.LogInformation("Existing CategoryFile entry found: {Exists}", existingCategoryFile != null);

            if (existingCategoryFile != null)
            {
                _logger.LogInformation("Updating existing database entry for file {FileName} in category {CategoryId}.", databaseFileName, model.CategoryId);
                existingCategoryFile.SortOrder = model.SortOrder;
                _context.CategoryFiles.Update(existingCategoryFile);
                _logger.LogInformation("Calling SaveChangesAsync to update database entry...");
                await _context.SaveChangesAsync();
                _logger.LogInformation("Database entry updated successfully.");
                TempData["SuccessMessage"] = $"Directory PDF file '{databaseFileName}' overwritten and database entry updated successfully.";
            }
            else
            {
                _logger.LogInformation("No existing database entry found. Creating new entry for file {FileName} in category {CategoryId}. SortOrder: {SortOrder}", databaseFileName, model.CategoryId, model.SortOrder);
                PDFCategory? categoryToAttach = await _context.PDFCategories.FindAsync(model.CategoryId);
                if (categoryToAttach == null)
                {
                    _logger.LogError("Database Error: Selected Category with ID {CategoryId} not found when creating new CategoryFile entry.", model.CategoryId);
                    TempData["ErrorMessage"] = $"Database Error: The selected category was not found when trying to add a new file entry. Physical file was saved/overwritten.";
                    return RedirectToAction("ManageCategoryFiles", "Admin", new { categoryId = model.CategoryId });
                }
                CategoryFile newCategoryFile = new()
                {
                    CategoryID = model.CategoryId,
                    FileName = databaseFileName,
                    SortOrder = model.SortOrder,
                    PDFCategory = categoryToAttach!
                };
                _context.CategoryFiles.Add(newCategoryFile);
                _logger.LogInformation("Calling SaveChangesAsync to add new database entry...");
                await _context.SaveChangesAsync();
                _logger.LogInformation("New database entry added successfully.");
                TempData["SuccessMessage"] = $"New Directory PDF file '{databaseFileName}' saved successfully.";
            }

            _logger.LogInformation("Redirecting to PdfCategory/ManageCategoryFiles for category {CategoryId}", model.CategoryId);
            return RedirectToAction("ManageCategoryFiles", "PdfCategory", new { categoryId = model.CategoryId });

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during PDF generation or saving for file {FileName}: {ErrorMessage}", databaseFileName, ex.Message);
            TempData["ErrorMessage"] = $"An unexpected error occurred: {ex.Message}";
            return RedirectToAction(nameof(CreatePdf));
        }
    }

    // API Endpoint: /PdfGeneration/CheckFileExists - Used by client-side JS for overwrite confirmation
    [HttpGet] // Use GET as we are just querying data
    public async Task<IActionResult> CheckFileExists(string fileName, int categoryId)
    {
        // Sanitize the incoming file name similar to the POST action (preserves spaces)
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleanFileName = new string([.. fileName.Where(c => !invalidChars.Contains(c))]);

        // Add the .pdf extension for the database check
        var databaseFileName = cleanFileName;
        if (!databaseFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            databaseFileName += ".pdf";
        }

        // Check if a file with this name exists in the specified category
        var fileExists = await _context.CategoryFiles
            .AnyAsync(cf => cf.CategoryID == categoryId && cf.FileName == databaseFileName);

        // Return a JSON response indicating if the file exists
        return Json(new { exists = fileExists });
    }

    // *** GET Action for Exporting User Data with Roles (No UserProfile ID) - Based on your provided file ***
    // GET: /PdfGeneration/ExportUserData - Exports user data with roles as a delimited file (CSV)

    [HttpGet]
    public async Task<IActionResult> ExportUserData()
    {
        _logger.LogInformation("ExportUserData: Starting data export with roles...");

        try
        {
            // Fetch data by joining AspNetUsers, UserProfile, and AspNetUserRoles
            // Group by user to get all roles for each user
            var userDataWithRoles = await _context.Users // Start with AspNetUsers
                .Join(_context.UserProfile, // Join with UserProfile
                        user => user.Id,
                        profile => profile.UserId,
                        (user, profile) => new { User = user, Profile = profile })
                .GroupJoin(_context.UserRoles, // Left join with AspNetUserRoles to get roles (GroupJoin is like Left Join for relationships)
                            joined => joined.User.Id,
                            userRole => userRole.UserId,
                            (joined, userRoles) => new { joined.User, joined.Profile, UserRoles = userRoles })
                .SelectMany( // Flatten the GroupJoin results
                    x => x.UserRoles.DefaultIfEmpty(), // Include users with no roles
                    (joined, userRole) => new { joined.User, joined.Profile, UserRole = userRole })
                .GroupJoin(_context.Roles, // Left join with AspNetRoles to get Role Names
                            joined => joined.UserRole != null ? joined.UserRole.RoleId : null, // Handle users with no roles (UserRole is null)
                            role => role.Id,
                            (joined, roles) => new { joined.User, joined.Profile, Role = roles.FirstOrDefault() }) // Get the first role name (there should only be one per role ID)
                .GroupBy( // Group by User and Profile to aggregate roles per user
                    x => new { x.User.Id, x.Profile.UserId }, // Group by Identity User ID and UserProfile ID
                    (key, g) => new
                    {
                        // Select the data needed for the CSV row
                        // Exclude Id (Identity User ID), UserName, and UserProfileId as requested
                        g.First().User.Email,
                        g.First().User.PhoneNumber, // From AspNetUsers
                        g.First().Profile.FirstName,
                        g.First().Profile.MiddleName,
                        g.First().Profile.LastName,
                        g.First().Profile.AddressLine1,
                        g.First().Profile.AddressLine2,
                        g.First().Profile.City,
                        g.First().Profile.State,
                        g.First().Profile.ZipCode,
                        g.First().Profile.HomePhoneNumber, // From UserProfile
                        g.First().Profile.Birthday,
                        g.First().Profile.Anniversary,
                        //g.First().Profile.Plot,
                        g.First().Profile.LastLogin, // CORRECTED: Access from Profile (as in your file)
                        g.First().User.EmailConfirmed,
                        g.First().User.PhoneNumberConfirmed,
                        // Aggregate Role Names for each user
                        Roles = g.Where(x => x.Role != null).Select(x => x.Role!.Name).ToList() // Get list of role names, filter out null roles
                    })
                .OrderBy(x => x.LastName) // Order the final results
                .ThenBy(x => x.FirstName)
                .ToListAsync(); // Execute the query

            if (userDataWithRoles == null || userDataWithRoles.Count == 0)
            {
                _logger.LogWarning("ExportUserData: No user data found to export.");
                // Return a simple message if no data
                return Content("No user data found to export.", "text/plain");
            }

            // Build the CSV content
            var builder = new StringBuilder();

            builder.AppendLine("First Name,Middle Name,Last Name,Address Line 1,Address Line 2,City,State,Zip Code,Email,Gell Phone,Home Phone,Birthday,Anniversary,Last Login,Email Confirmed,Phone Number Confirmed,Roles"); // Updated Header

            // Add Data Rows
            foreach (var user in userDataWithRoles)
            {
                // Format roles as a comma-separated string
                var rolesString = string.Join(", ", user.Roles);

                // Append data fields, using the EscapeCsv helper for each field
                // The order here must match the header row above
                builder.AppendLine(

                    $"{EscapeCsv(user.FirstName)}," +
                    $"{EscapeCsv(user.MiddleName)}," +
                    $"{EscapeCsv(user.LastName)}," +
                    $"{EscapeCsv(user.AddressLine1)}," +
                    $"{EscapeCsv(user.AddressLine2)}," +
                    $"{EscapeCsv(user.City)}," +
                    $"{EscapeCsv(user.State)}," +
                    $"{EscapeCsv(user.ZipCode)}," +
                    $"{EscapeCsv(user.Email)}," +
                    $"{EscapeCsv(user.PhoneNumber)}," +
                    $"{EscapeCsv(user.HomePhoneNumber)}," +
                    $"{EscapeCsv(user.Birthday?.ToShortDateString())}," + // Format dates
                    $"{EscapeCsv(user.Anniversary?.ToShortDateString())}," + // Format dates
                    //$"{EscapeCsv(user.Plot)}," +
                    $"{EscapeCsv(user.LastLogin?.ToString())}," + // Format DateTime
                    $"{EscapeCsv(user.EmailConfirmed)}," +
                    $"{EscapeCsv(user.PhoneNumberConfirmed)}," +
                    $"{EscapeCsv(rolesString)}" // Add the aggregated roles string
                );
            }

            // Convert the StringBuilder content to bytes
            var csvBytes = Encoding.UTF8.GetBytes(builder.ToString());

            _logger.LogInformation("ExportUserData: CSV data generated. Size: {Size} bytes", csvBytes.Length);

            // Return the CSV file for download
            return File(csvBytes, "text/csv", "UserDirectoryExportWithRoles.csv"); // Changed default filename slightly

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportUserData: Error during data export.");
            // Return a server error status code and message
            return StatusCode(500, "An error occurred during data export.");
        }
    }


    // ADDED: Helper function to get the day of the month with the correct suffix (st, nd, rd, th)
    private static string GetDayWithSuffix(DateTime date)
    {
        int day = date.Day;
        if (day >= 11 && day <= 13)
        {
            return day + "th";
        }
        return (day % 10) switch
        {
            1 => day + "st",
            2 => day + "nd",
            3 => day + "rd",
            _ => day + "th",
        };
    }

    // Helper function for basic CSV escaping (handles commas, quotes, newlines within fields)
    // It also wraps fields in quotes if they contain these characters or leading/trailing spaces
    private static string EscapeCsv(object? field)
    {
        if (field == null)
            return ""; // Return empty string for null values

        var data = field.ToString();
        if (data == null)
            return "";

        data = data.Replace("\"", "\"\""); // Escape existing double quotes by doubling them

        // Check if the data needs to be enclosed in double quotes
        // Fields containing comma, double quote, newline, carriage return, or leading/trailing spaces should be quoted
        if (data.Contains(',') || data.Contains('"') || data.Contains('\n') || data.Contains('\r') || data.StartsWith(' ') || data.EndsWith(' ')) // Added space check for robustness
        {
            return $"\"{data}\""; // Enclose the entire field in double quotes
        }

        return data; // Return the data as is if no special characters requiring quoting
    }

} // Closing brace for the PdfGenerationController class