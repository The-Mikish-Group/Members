using Members.Models; // Assuming your models are in the Members.Models namespace
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png; // ADDED: For PNG encoding
using SixLabors.ImageSharp.Processing;
using Microsoft.AspNetCore.Hosting; // Make sure this is included for IWebHostEnvironment
using Microsoft.AspNetCore.Http; // Make sure this is included for IFormFile
using System.IO;
using System.Linq;
using System.Collections.Generic; // Make sure this is included for IEnumerable<IFormFile>
using System; // Make sure this is included for Guid and Exception
using System.Threading.Tasks; // Make sure this is included for async/await

namespace Members.Controllers // Assuming your controllers are in the Members.Controllers namespace
{
    public class ImageController(IWebHostEnvironment webHostEnvironment) : Controller
    {
        private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
        // --- Thumbnail Size Adjustment ---
        private const int ThumbnailWidth = 800; // Pixels - Adjusted size
        private const int ThumbnailHeight = 800; // Pixels - Adjusted size

        // Helper method to get the full path to a gallery directory
        private string GetGalleryPath(string galleryName)
        {
            // Sanitize galleryName to prevent directory traversal
            var sanitizedGalleryName = Path.GetFileName(galleryName);
            return Path.Combine(_webHostEnvironment.WebRootPath, "Galleries", sanitizedGalleryName);
        }

        // Helper method to get the full path to an image file
        private string GetImagePath(string galleryName, string fileName)
        {
            // Sanitize file name
            var sanitizedFileName = Path.GetFileName(fileName);
            return Path.Combine(GetGalleryPath(galleryName), sanitizedFileName);
        }

        // Helper method to get the full path to a thumbnail file
        // MODIFIED: The thumbnail will now retain the original image's extension
        private string GetThumbnailPath(string galleryName, string fileName)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var originalExtension = Path.GetExtension(fileName);
            // Sanitize file name without extension
            var sanitizedFileNameWithoutExtension = Path.GetFileName(fileNameWithoutExtension);
            // Use originalExtension for the thumbnail file name
            return Path.Combine(GetGalleryPath(galleryName), $"{sanitizedFileNameWithoutExtension}_thumb{originalExtension}");
        }

        // Helper method to generate a thumbnail for an image
        // MODIFIED: Added logic to use PngEncoder for PNGs to preserve transparency
        private static async Task GenerateThumbnail(string imagePath, string thumbnailPath)
        {
            try
            {
                // Ensure the directory for the thumbnail exists (user's existing robust check)
                var thumbnailDirectory = System.IO.Path.GetDirectoryName(thumbnailPath);
                if (thumbnailDirectory != null && thumbnailDirectory != string.Empty)
                {
                    thumbnailDirectory = thumbnailDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                if (thumbnailDirectory != null && thumbnailDirectory != string.Empty)
                {
                    if (!Directory.Exists(thumbnailDirectory))
                    {
                        Directory.CreateDirectory(thumbnailDirectory);
                    }
                }
                else
                {
                    throw new DirectoryNotFoundException("Thumbnail directory not found.");
                }

                using var image = await Image.LoadAsync(imagePath);
                // Calculate dimensions to maintain aspect ratio
                int newWidth = ThumbnailWidth;
                int newHeight = (int)Math.Round((double)image.Height * ThumbnailWidth / image.Width);

                if (newHeight > ThumbnailHeight)
                {
                    newHeight = ThumbnailHeight;
                    newWidth = (int)Math.Round((double)image.Width * ThumbnailHeight / image.Height);
                }

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(newWidth, newHeight),
                    Mode = ResizeMode.Max
                }));

                // ADDED LOGIC: Determine the encoder based on the original file's extension
                var originalExtension = Path.GetExtension(imagePath).ToLowerInvariant();
                if (originalExtension == ".png")
                {
                    await image.SaveAsync(thumbnailPath, new PngEncoder());
                }
                else
                {
                    await image.SaveAsync(thumbnailPath, new JpegEncoder());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating thumbnail for {imagePath}: {ex.Message}");
            }
        }

        // GET: /Image/GalleryList (For all users)
        // Displays a list of all available image galleries.
        public IActionResult GalleryList()
        {
            var galleriesRootPath = Path.Combine(_webHostEnvironment.WebRootPath, "Galleries");

            // Ensure the Galleries directory exists
            if (!Directory.Exists(galleriesRootPath))
            {
                Directory.CreateDirectory(galleriesRootPath);
            }

            // Get all directories within the Galleries root path
            var galleryDirectories = Directory.GetDirectories(galleriesRootPath)
                                                .Select(dir => new GalleryViewModel
                                                {
                                                    Name = new DirectoryInfo(dir).Name,
                                                    // Count image files, excluding thumbnails
                                                    // MODIFIED: Use Contains("_thumb") for robustness
                                                    ImageCount = Directory.GetFiles(dir)
                                                                        .Count(f => !f.Contains("_thumb", StringComparison.OrdinalIgnoreCase) &&
                                                                                    (f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                                                })
                                                .ToList();

            // Pass the list of galleries to the view
            return View(galleryDirectories);
        }

        // GET: /Image/GalleryView/{galleryName} (For all users)
        // Displays images within a specific gallery.
        public async Task<IActionResult> GalleryView(string galleryName)
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                return NotFound();
            }

            var galleryPath = GetGalleryPath(galleryName);

            // Check if the gallery directory exists
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = "Gallery not found.";
                return RedirectToAction(nameof(GalleryList));
            }

            var imageFiles = new List<ImageViewModel>();

            // Get all image files (excluding thumbnails) in the gallery directory
            var filesInGallery = Directory.GetFiles(galleryPath)
                                      // MODIFIED: Use Contains("_thumb") for robustness
                                      .Where(f => !f.Contains("_thumb", StringComparison.OrdinalIgnoreCase) &&
                                                  (f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                                      .OrderBy(f => Path.GetFileName(f))
                                      .ToList();

            // --- Thumbnail Recreation Check ---
            foreach (var filePath in filesInGallery)
            {
                var fileName = Path.GetFileName(filePath);
                var thumbnailPath = GetThumbnailPath(galleryName, fileName);

                // Check if the thumbnail exists. If not, generate it.
                if (!System.IO.File.Exists(thumbnailPath))
                {
                    await GenerateThumbnail(filePath, thumbnailPath);
                }

                // Add the image to the list for the view model
                imageFiles.Add(new ImageViewModel
                {
                    GalleryName = galleryName,
                    FileName = fileName,
                    // MODIFIED: Construct ThumbnailUrl using the original extension for the thumbnail
                    ThumbnailUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(fileName))}_thumb{Uri.EscapeDataString(Path.GetExtension(fileName))}",
                    FullImageUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(fileName)}"
                });
            }
            // --- End Thumbnail Recreation Check ---


            ViewBag.GalleryName = galleryName;
            return View(imageFiles);
        }

        // GET: /Image/ImageView/{galleryName}/{fileName} (For all users)
        // Displays a single full-size image.
        public IActionResult ImageView(string galleryName, string fileName)
        {
            if (string.IsNullOrEmpty(galleryName) || string.IsNullOrEmpty(fileName))
            {
                return NotFound();
            }

            var fullPath = GetImagePath(galleryName, fileName);

            // Check if the image file exists
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound();
            }

            // Create ViewModel for the single image
            var viewModel = new ImageViewModel
            {
                GalleryName = galleryName,
                FileName = fileName,
                // MODIFIED: Calculate and assign the ThumbnailUrl using the original extension for the thumbnail
                ThumbnailUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(fileName))}_thumb{Uri.EscapeDataString(Path.GetExtension(fileName))}",
                FullImageUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(fileName)}"
            };

            return View(viewModel);
        }


        // GET: /Image/ManageGalleries (Admins & Managers)
        // Displays a list of galleries with management options.
        [Authorize(Roles = "Admin,Manager")]
        public IActionResult ManageGalleries()
        {
            var galleriesRootPath = Path.Combine(_webHostEnvironment.WebRootPath, "Galleries");

            // Ensure the Galleries directory exists
            if (!Directory.Exists(galleriesRootPath))
            {
                Directory.CreateDirectory(galleriesRootPath);
            }

            // Get all directories within the Galleries root path
            var galleryDirectories = Directory.GetDirectories(galleriesRootPath)
                                                .Select(dir => new GalleryViewModel
                                                {
                                                    Name = new DirectoryInfo(dir).Name,
                                                    // Count image files, excluding thumbnails
                                                    // MODIFIED: Use Contains("_thumb") for robustness
                                                    ImageCount = Directory.GetFiles(dir)
                                                                        .Count(f => !f.Contains("_thumb", StringComparison.OrdinalIgnoreCase) &&
                                                                                    (f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                     f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                                                })
                                                .ToList();

            ViewBag.ShowManagementOptions = true;
            return View(galleryDirectories);
        }

        // POST: /Image/CreateGallery (Admins & Managers)
        // Handles the creation of a new gallery directory.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateGallery(CreateGalleryViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid gallery name. Please check the requirements.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            var galleryPath = GetGalleryPath(model.GalleryName);

            if (Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = $"A gallery named '{model.GalleryName}' already exists.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            try
            {
                Directory.CreateDirectory(galleryPath);
                TempData["SuccessMessage"] = $"Gallery '{model.GalleryName}' created successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error creating gallery: {ex.Message}";
                Console.WriteLine($"Error creating gallery '{model.GalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries));
        }

        // POST: /Image/DeleteGallery (Admins & Managers)
        // Handles the deletion of a gallery directory and its contents.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteGallery(string galleryName)
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                TempData["ErrorMessage"] = "Gallery name is required to delete.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            var galleryPath = GetGalleryPath(galleryName);

            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = $"Gallery '{galleryName}' not found.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            try
            {
                Directory.Delete(galleryPath, true);
                TempData["SuccessMessage"] = $"Gallery '{galleryName}' and its contents deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting gallery: {ex.Message}";
                Console.WriteLine($"Error deleting gallery '{galleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries));
        }

        // POST: /Image/RenameGallery (Admins & Managers)
        // Handles the renaming of a gallery directory.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RenameGallery(string oldGalleryName, string newGalleryName)
        {
            if (string.IsNullOrEmpty(oldGalleryName) || string.IsNullOrEmpty(newGalleryName))
            {
                TempData["ErrorMessage"] = "Both old and new gallery names are required.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            var sanitizedNewGalleryName = Path.GetFileName(newGalleryName);
            if (string.IsNullOrEmpty(sanitizedNewGalleryName) || sanitizedNewGalleryName.Any(c => Path.GetInvalidFileNameChars().Contains(c) || Path.GetInvalidPathChars().Contains(c)))
            {
                TempData["ErrorMessage"] = "Invalid characters in the new gallery name.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            var oldGalleryPath = GetGalleryPath(oldGalleryName);
            var newGalleryPath = GetGalleryPath(sanitizedNewGalleryName);

            if (!Directory.Exists(oldGalleryPath))
            {
                TempData["ErrorMessage"] = $"Gallery '{oldGalleryName}' not found.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            if (Directory.Exists(newGalleryPath))
            {
                TempData["ErrorMessage"] = $"A gallery named '{sanitizedNewGalleryName}' already exists. Cannot rename.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            try
            {
                Directory.Move(oldGalleryPath, newGalleryPath);
                TempData["SuccessMessage"] = $"Gallery '{oldGalleryName}' renamed to '{sanitizedNewGalleryName}' successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error renaming gallery: {ex.Message}";
                Console.WriteLine($"Error renaming gallery '{oldGalleryName}' to '{sanitizedNewGalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries));
        }

        // GET: /Image/ManageGalleryImages/{galleryName} (Admins & Managers)
        // Displays images within a specific gallery with management options.
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ManageGalleryImages(string galleryName)
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                return NotFound();
            }

            var galleryPath = GetGalleryPath(galleryName);
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = "Gallery not found.";
                return RedirectToAction(nameof(ManageGalleries));
            }

            var imageFiles = new List<ImageViewModel>();

            var filesInGallery = Directory.GetFiles(galleryPath)
                                      .Where(f => !f.Contains("_thumb", StringComparison.OrdinalIgnoreCase) &&
                                                  (f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                                      .OrderBy(f => Path.GetFileName(f))
                                      .ToList();

            foreach (var filePath in filesInGallery)
            {
                var fileName = Path.GetFileName(filePath);
                var thumbnailPath = GetThumbnailPath(galleryName, fileName);

                if (!System.IO.File.Exists(thumbnailPath))
                {
                    await GenerateThumbnail(filePath, thumbnailPath);
                }

                imageFiles.Add(new ImageViewModel
                {
                    GalleryName = galleryName,
                    FileName = fileName,
                    ThumbnailUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(fileName))}_thumb{Uri.EscapeDataString(Path.GetExtension(fileName))}",
                    FullImageUrl = $"/Galleries/{Uri.EscapeDataString(galleryName)}/{Uri.EscapeDataString(fileName)}"
                });
            }

            ViewBag.GalleryName = galleryName;
            return View(imageFiles);
        }

        // POST: /Image/UploadImage (Admins & Managers)
        // Handles the uploading of a new image to a gallery and generates a thumbnail.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadImage(string galleryName, IEnumerable<IFormFile> ImageFiles)
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                TempData["ErrorMessage"] = "Gallery name is missing.";
                return RedirectToAction("ManageGalleryImages", new { galleryName });
            }

            if (ImageFiles == null || !ImageFiles.Any())
            {
                TempData["ErrorMessage"] = "Please select one or more image files to upload.";
                return RedirectToAction("ManageGalleryImages", new { galleryName });
            }

            var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "Galleries", galleryName); // Corrected: "Galleries"

            // Ensure the directory exists
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            int successfulUploads = 0;
            List<string> failedUploads = new List<string>();

            foreach (var imageFile in ImageFiles)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                var fileExtension = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant();

                if (string.IsNullOrEmpty(fileExtension) || !allowedExtensions.Contains(fileExtension))
                {
                    failedUploads.Add(imageFile.FileName + " (invalid file type)");
                    continue;
                }

                if (imageFile.Length > 0)
                {
                    // MODIFIED LINE: Removed the GUID generation for unique filenames
                    var fileName = Path.GetFileName(imageFile.FileName); // Use the original filename

                    var filePath = Path.Combine(uploadsPath, fileName);

                    // OPTIONAL: Add a check here if you want to explicitly warn the user if a file with the same name exists.
                    // If you want to prevent overwrite and show an error:
                    // if (System.IO.File.Exists(filePath))
                    // {
                    //     failedUploads.Add(imageFile.FileName + " (file with same name already exists)");
                    //     continue;
                    // }
                    // If you want to overwrite silently, no extra check is needed here.

                    try
                    {
                        using (var stream = new FileStream(filePath, FileMode.Create)) // FileMode.Create will overwrite if file exists
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        // Generate thumbnail immediately after successful upload
                        var thumbnailPath = GetThumbnailPath(galleryName, fileName);
                        await GenerateThumbnail(filePath, thumbnailPath);

                        successfulUploads++;
                    }
                    catch (Exception ex)
                    {
                        failedUploads.Add(imageFile.FileName);
                        Console.WriteLine($"Error uploading {imageFile.FileName}: {ex.Message}");
                    }
                }
                else
                {
                    failedUploads.Add(imageFile.FileName + " (empty file)");
                }
            }

            if (successfulUploads > 0)
            {
                TempData["SuccessMessage"] = $"{successfulUploads} image(s) uploaded successfully!";
            }
            if (failedUploads.Count > 0)
            {
                TempData["ErrorMessage"] = $"Failed to upload: {string.Join(", ", failedUploads)}.";
            }
            else if (successfulUploads == 0 && ImageFiles.Any())
            {
                TempData["ErrorMessage"] = "No valid files were uploaded. Please check file types and sizes.";
            }

            return RedirectToAction("ManageGalleryImages", new { galleryName });
        }

        // POST: /Image/RenameImage (Admins & Managers)
        // Handles the renaming of an image file and its corresponding thumbnail.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RenameImage(RenameImageViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid new file name.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName });
            }

            var galleryPath = GetGalleryPath(model.GalleryName);
            var oldFilePath = GetImagePath(model.GalleryName, model.OldFileName);
            var newFilePath = GetImagePath(model.GalleryName, model.NewFileName);

            if (!Path.GetExtension(model.OldFileName).Equals(System.IO.Path.GetExtension(model.NewFileName), StringComparison.CurrentCultureIgnoreCase))
            {
                TempData["ErrorMessage"] = "The new file name must have the same extension as the old file name.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName });
            }

            if (!System.IO.File.Exists(oldFilePath))
            {
                TempData["ErrorMessage"] = $"Image '{model.OldFileName}' not found.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName });
            }

            if (System.IO.File.Exists(newFilePath))
            {
                TempData["ErrorMessage"] = $"An image named '{model.NewFileName}' already exists in this gallery.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName });
            }

            try
            {
                System.IO.File.Move(oldFilePath, newFilePath);

                var oldThumbnailPath = GetThumbnailPath(model.GalleryName, model.OldFileName);
                var newThumbnailPath = GetThumbnailPath(model.GalleryName, model.NewFileName);
                if (System.IO.File.Exists(oldThumbnailPath))
                {
                    System.IO.File.Move(oldThumbnailPath, newThumbnailPath);
                }

                TempData["SuccessMessage"] = $"Image '{model.OldFileName}' renamed to '{model.NewFileName}' successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error renaming image: {ex.Message}";
                Console.WriteLine($"Error renaming image '{model.OldFileName}' to '{model.NewFileName}' in gallery '{model.GalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName });
        }

        // POST: /Image/DeleteImage (Admins & Managers)
        // Handles the deletion of an image file and its corresponding thumbnail.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteImage(string galleryName, string fileName)
        {
            if (string.IsNullOrEmpty(galleryName) || string.IsNullOrEmpty(fileName))
            {
                TempData["ErrorMessage"] = "Gallery name and file name are required to delete.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName });
            }

            var filePath = GetImagePath(galleryName, fileName);
            var thumbnailPath = GetThumbnailPath(galleryName, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                TempData["ErrorMessage"] = $"Image '{fileName}' not found in gallery '{galleryName}'.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName });
            }

            try
            {
                System.IO.File.Delete(filePath);
                if (System.IO.File.Exists(thumbnailPath))
                {
                    System.IO.File.Delete(thumbnailPath);
                }
                TempData["SuccessMessage"] = $"Image '{fileName}' deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting image: {ex.Message}";
                Console.WriteLine($"Error deleting image '{fileName}' from gallery '{galleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleryImages), new { galleryName });
        }
    }
}