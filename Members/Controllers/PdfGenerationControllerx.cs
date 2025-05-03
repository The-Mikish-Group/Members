using Microsoft.AspNetCore.Authorization; // For the [Authorize] attribute
using Microsoft.AspNetCore.Hosting; // Needed for IWebHostEnvironment
using Microsoft.AspNetCore.Mvc; // For Controller, IActionResult, etc.
using Microsoft.Extensions.Logging; // For ILogger
using Members.Data;
using Members.Models;
using System.IO; // For Path.Combine, File operations, MemoryStream
using System.Threading.Tasks; // For async/await
using Microsoft.EntityFrameworkCore; // Needed for EF Core methods like Include, ToListAsync, MaxAsync, FirstOrDefaultAsync, AnyAsync, SelectMany, GroupBy
using System; // For Exception, ArgumentNullException, DateTime
using Microsoft.AspNetCore.Identity; // Needed for UserManager, IdentityUser
using Members.Services; 
using System.Collections.Generic; // Needed for Lists and IEnumerables
using System.Linq; // Needed for LINQ methods like OrderBy, Where, Select, Max, Any, ToListAsync, Aggregate, SelectMany, GroupBy
using System.ComponentModel.DataAnnotations; // Needed for ViewModel attributes
using System.Text; // Needed for StringBuilder and Encoding

// --- Add Syncfusion PDF Namespaces ---
using Syncfusion;
using Syncfusion.Licensing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing; // Contains RectangleF and SizeF for Syncfusion
using Syncfusion.Pdf.Tables; // If using tables for layout
using Syncfusion.Pdf.Interactive; // If needed for links, etc.
using Syncfusion.Pdf.Grid; // If using grids for layout
using Syncfusion.Pdf.Parsing;

// --- Namespaces for View Rendering to String 
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;

// This controller is dedicated to generating PDF files, specifically the Member Directory PDF, using Syncfusion PDF Library.
// It also handles related data export functionality.

namespace Members.Controllers;

[Authorize(Roles = "Manager,Admin")]
public class PdfGenerationController : Controller // Inherit from Controller
{
    // Injected dependencies for database access, environment info, logging, user management, AND View Rendering (keep if RenderViewToStringAsync is used elsewhere)
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PdfGenerationController> _logger;
    private readonly ApplicationDbContext _context;   
    
    // Injected services for View Rendering
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
               
        // Inject services for View Rendering
        IRazorViewEngine razorViewEngine,
        ITempDataProvider tempDataProvider,
        IActionContextAccessor actionContextAccessor)
    {
        _environment = environment;
        _logger = logger;
        _context = context;       
       
        // Assign injected services for View Rendering
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
            return RedirectToAction("ManageCategories", "Admin"); // Example: Redirect to AdminController.ManageCategories
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
    [HttpPost] // Specify the HTTP method
    [ValidateAntiForgeryToken] // Protect against Cross-Site Request Forgery
    public async Task<IActionResult> CreatePdf(CreatePdfPostModel model) // Accept the POST ViewModel
    {
        // Use TempData for messages as we are redirecting away from this action
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

            // --- Start of Syncfusion PDF Generation with Spacing Adjustments ---
            using (PdfDocument document = new())
            {
                PdfPage page = document.Pages.Add();
                PdfGraphics graphics = page.Graphics;
                PdfFont regularFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12);
                PdfFont boldFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
                PdfBrush brush = PdfBrushes.Black;

                // Reduced margin for less space at top/bottom and sides
                float margin = 20; // Reduced from 40

                PdfStringFormat format = new() { WordWrap = PdfWordWrapType.Word };
                
                // This defines the main content area rectangle
                RectangleF bounds = new(margin, margin, page.GetClientSize().Width - (2 * margin), page.GetClientSize().Height - (2 * margin));

                int numberOfColumns = 2;
                float columnSpacing = margin / 2; // Column spacing relative to the new margin
                float columnWidth = (bounds.Width - (columnSpacing)) / numberOfColumns;

                // Initial currentY is set for drawing the main title area with a reduced top margin
                float titleTopPosition = margin / 2; // Position the top of the title block closer to the top edge
                float currentY = titleTopPosition; // This will be the starting Y for both the image and the text

                float currentX = bounds.Left;
                int currentColumn = 0;

                PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 14, PdfFontStyle.Bold);

                // --- Title Area Drawing ---
                float logoWidth = 30; // Declare logoWidth here
                float logoHeight = 30; // Declare logoHeight here

                // --- Image Loading and Drawing ---
                using (FileStream imageStream = new("wwwroot\\Images\\LinkImages\\Oaks-trees.png", FileMode.Open, FileAccess.Read))
                {
                    PdfImage logoInstance = PdfImage.FromStream(imageStream); // Create an instance of the image

                    // Draw the image at the currentY (aligning its top with the top of the title area)
                    graphics.DrawImage(logoInstance, bounds.Left, currentY - 10, logoWidth, logoHeight);
                }               

                // Calculate the X position for the title text, considering the logo and some spacing
                float spacingBetweenLogoAndText = 5; // Space between the logo and the title text
                float titleX = bounds.Left + logoWidth + spacingBetweenLogoAndText;
               
                graphics.DrawString("Oaks Village Homeowners Association Directory " + DateTime.Now.ToString("MM/yyyy"), titleFont, brush, new Syncfusion.Drawing.PointF(titleX, currentY));
                                
                float bottomOfImage = currentY + logoHeight;
                float bottomOfText = currentY + titleFont.Height; // Assuming DrawString PointF Y is near the top

                float bottomOfTitleBlock = Math.Max(bottomOfImage, bottomOfText);

                // Add the desired space below the title block
                float spaceBelowTitle = margin / 2; // Reduced space below the title
                currentY = bottomOfTitleBlock + spaceBelowTitle;

                // --- End of Title Block Drawing and Spacing ---

                foreach (UserProfile userProfile in userProfilesWithUsers)
                {
                    string middleName = string.IsNullOrEmpty(userProfile.MiddleName) ? " " : $" {userProfile.MiddleName} ";
                    string nameLine = $"{userProfile.FirstName}{middleName}{userProfile.LastName}";
                    Syncfusion.Drawing.SizeF nameSize = boldFont.MeasureString(nameLine, columnWidth, format);

                    StringBuilder remainingUserDataCheck = new();
                    if (!string.IsNullOrEmpty(userProfile.AddressLine1)) remainingUserDataCheck.AppendLine(userProfile.AddressLine1);
                    if (!string.IsNullOrEmpty(userProfile.AddressLine2)) remainingUserDataCheck.AppendLine(userProfile.AddressLine2);
                    remainingUserDataCheck.AppendLine($"{userProfile.City}, {userProfile.State} {userProfile.ZipCode}");
                    if (!string.IsNullOrEmpty(userProfile.User?.PhoneNumber)) remainingUserDataCheck.AppendLine($"Cell Phone: {userProfile.User.PhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.HomePhoneNumber)) remainingUserDataCheck.AppendLine($"Home Phone: {userProfile.HomePhoneNumber}");
                    if (!string.IsNullOrEmpty(userProfile.User?.Email)) remainingUserDataCheck.AppendLine($"Email: {userProfile.User.Email}");

                    if (userProfile.Birthday.HasValue)
                    {
                        var month = userProfile.Birthday.Value.ToString("MMMM");
                        var dayWithSuffix = GetDayWithSuffix(userProfile.Birthday.Value);
                        var birthdayString = $"{month} {dayWithSuffix}";
                        remainingUserDataCheck.AppendLine($"Birthday: {birthdayString}");
                    }

                    if (userProfile.Anniversary.HasValue)
                    {
                        var month = userProfile.Anniversary.Value.ToString("MMMM");
                        var dayWithSuffix = GetDayWithSuffix(userProfile.Anniversary.Value);
                        var anniversaryString = $"{month} {dayWithSuffix}";
                        remainingUserDataCheck.AppendLine($"Anniversary: {anniversaryString}");
                    }

                    Syncfusion.Drawing.SizeF remainingTextSizeCheck = regularFont.MeasureString(remainingUserDataCheck.ToString(), columnWidth, format);

                    float totalBlockHeight = nameSize.Height + remainingTextSizeCheck.Height;

                    // Pagination Check: Using bounds.Bottom which reflects the reduced margin
                    // Also using a reduced spacing between blocks for the check
                    float spaceBetweenBlocks = margin / 4; // Reduced space between user profile blocks
                    if (currentY + totalBlockHeight + spaceBetweenBlocks > bounds.Bottom)
                    {
                        currentColumn++;
                        if (currentColumn < numberOfColumns)
                        {
                            currentX = bounds.Left + (currentColumn * (columnWidth + columnSpacing));                            
                            float contentStartY = titleTopPosition + Math.Max(titleFont.Height, logoHeight) + spaceBelowTitle; // Recalculate the starting Y for content
                            currentY = contentStartY;
                        }
                        else
                        {
                            // New Page
                            page = document.Pages.Add();
                            graphics = page.Graphics;
                            currentX = bounds.Left;
                            currentColumn = 0;

                            // Adjust currentY reset for new page - positioning the continued title
                            float continuedTitleTopPosition = margin / 2; // Same top margin as the main title
                            currentY = continuedTitleTopPosition; // Start currentY for drawing the continued title
                            float continuedTitleX = bounds.Left + (bounds.Width - titleFont.MeasureString("Homeowners Association Directory (Continued) " + DateTime.Now.ToString("MM/yyyy")).Width) / 2;
                            // --- Image Loading and Drawing ---
                            using (FileStream imageStream = new("wwwroot\\Images\\LinkImages\\Oaks-trees.png", FileMode.Open, FileAccess.Read))
                            {
                                PdfImage logoInstance = PdfImage.FromStream(imageStream); // Create an instance of the image

                                // Draw the image at the currentY (aligning its top with the top of the title area)
                                graphics.DrawImage(logoInstance, bounds.Left, currentY - 10, logoWidth, logoHeight);
                            }
                                                        
                            graphics.DrawString(
                                "Homeowners Association Directory (Continued) " + DateTime.Now.ToString("MM/yyyy"), // Text to draw
                                titleFont, // Font
                                brush, // Brush
                                new Syncfusion.Drawing.PointF(continuedTitleX, currentY), // PointF for position
                                new PdfStringFormat()
                            );
                            
                            // Now update currentY to start drawing content below the continued title
                            float spaceBelowContinuedTitle = margin / 2; // Space below the continued title
                            currentY += titleFont.MeasureString("Homeowners Association Directory (Continued) " + DateTime.Now.ToString("MM/yyyy")).Height + spaceBelowContinuedTitle;
                        }
                    }

                    // Draw the current user profile block
                    graphics.DrawString(nameLine, boldFont, brush, new RectangleF(currentX, currentY, columnWidth, nameSize.Height), format);
                    currentY += nameSize.Height;

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
                        var anniversaryString = $"{month} {dayWithSuffix}";
                        remainingUserData.AppendLine($"Anniversary: {anniversaryString}");
                    }

                    SizeF remainingTextSize = regularFont.MeasureString(remainingUserData.ToString(), columnWidth, format);
                    graphics.DrawString(remainingUserData.ToString(), regularFont, brush, new RectangleF(currentX, currentY, columnWidth, remainingTextSize.Height), format);

                    // Add the reduced space between blocks after drawing
                    currentY += remainingTextSize.Height + spaceBetweenBlocks;
                }

                // --- Start of Footer Spacing Adjustment ---

                PdfFont footerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);
                PdfStringFormat footerFormat = new()
                {
                    Alignment = PdfTextAlignment.Right, // Right-justify the footer text
                    LineAlignment = PdfVerticalAlignment.Bottom
                };

                // Calculate footer height using the font used for drawing the footer text (assuming boldFont based on your code)
                float footerHeight = boldFont.Height;

                // Define the desired space below the footer text
                float footerBottomMargin = margin / 4; // Reduced space below the footer

                for (int i = 0; i < document.Pages.Count; i++)
                {
                    PdfPage currentPage = document.Pages[i];
                    PdfGraphics currentPageGraphics = currentPage.Graphics;

                    // Calculate the Y position for the bottom of the footer text, leaving footerBottomMargin space below it
                    float footerBottomY = currentPage.GetClientSize().Height - footerBottomMargin;

                    // Calculate the top Y position for the footer bounds based on the desired bottom Y and footer height
                    float footerTopY = footerBottomY - footerHeight;

                    // Define the footerBounds using the calculated top Y and height, and reduced horizontal margins
                    // The width of footerBounds determines the area within which the text will be right-justified.
                    Syncfusion.Drawing.RectangleF footerBounds = new(margin, footerTopY, currentPage.GetClientSize().Width - (2 * margin), footerHeight);

                    string footerText = $"Page {i + 1}";

                    // Draw the string within the adjusted bounds using the footerFormat with Right alignment.
                    currentPageGraphics.DrawString(footerText, boldFont, brush, footerBounds, footerFormat);
                }

                // --- End of Footer Spacing Adjustment ---


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
                    return RedirectToAction("ManageCategoryFiles", "PdfCategory", new { categoryId = model.CategoryId });
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

            _logger.LogInformation("Redirecting to Admin/ManageCategoryFiles for category {CategoryId}", model.CategoryId);
            return RedirectToAction("ManageCategoryFiles", "PdfCategory", new { categoryId = model.CategoryId });

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during PDF generation or saving for file {FileName}: {ErrorMessage}", databaseFileName, ex.Message);
            TempData["ErrorMessage"] = $"An unexpected error occurred: {ex.Message}";
            return RedirectToAction(nameof(CreatePdf));
        }
    }
   
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
                        g.First().Profile.Plot,
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
                        
            builder.AppendLine("First Name,Middle Name,Last Name,Address Line 1,Address Line 2,City,State,Zip Code,Email,Gell Phone,Home Phone,Birthday,Anniversary,Plot,Last Login,Email Confirmed,Phone Number Confirmed,Roles"); // Updated Header

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
                    $"{EscapeCsv(user.Plot)}," +
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