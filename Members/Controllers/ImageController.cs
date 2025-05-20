using Members.Models; // Assuming your models are in the Members.Models namespace
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png; // ADDED: For PNG encoding
using SixLabors.ImageSharp.Processing;

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
            var originalExtension = Path.GetExtension(fileName); // CHANGED: Renamed 'extension' to 'originalExtension'
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
                    Mode = ResizeMode.Max // Resizes to fit within bounds while maintaining aspect ratio
                                          // Change to ResizeMode.Crop if you want strict 800x800 square thumbnails
                }));

                // ADDED LOGIC: Determine the encoder based on the original file's extension
                var originalExtension = Path.GetExtension(imagePath).ToLowerInvariant(); // Get original extension from imagePath
                if (originalExtension == ".png")
                {
                    await image.SaveAsync(thumbnailPath, new PngEncoder()); // Save as PNG to preserve transparency
                }
                else
                {
                    await image.SaveAsync(thumbnailPath, new JpegEncoder()); // Save other formats as JPEG
                }
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                Console.WriteLine($"Error generating thumbnail for {imagePath}: {ex.Message}");
                // Optionally, copy the original image as a fallback if thumbnail generation fails
                // System.IO.File.Copy(imagePath, thumbnailPath, true);
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
        public async Task<IActionResult> GalleryView(string galleryName) // Changed to async Task<IActionResult>
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                return NotFound(); // Return 404 if gallery name is not provided
            }

            var galleryPath = GetGalleryPath(galleryName);

            // Check if the gallery directory exists
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = "Gallery not found."; // Show error message
                return RedirectToAction(nameof(GalleryList)); // Redirect back to gallery list
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
                                      .OrderBy(f => Path.GetFileName(f)) // Order files by name
                                      .ToList();

            // --- Thumbnail Recreation Check ---
            foreach (var filePath in filesInGallery)
            {
                var fileName = Path.GetFileName(filePath);
                var thumbnailPath = GetThumbnailPath(galleryName, fileName); // Thumbnail path now includes original extension

                // Check if the thumbnail exists. If not, generate it.
                if (!System.IO.File.Exists(thumbnailPath))
                {
                    await GenerateThumbnail(filePath, thumbnailPath); // Await thumbnail generation
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


            ViewBag.GalleryName = galleryName; // Pass gallery name to the view
            return View(imageFiles); // Pass the list of images to the view
        }

        // GET: /Image/ImageView/{galleryName}/{fileName} (For all users)
        // Displays a single full-size image.
        public IActionResult ImageView(string galleryName, string fileName)
        {
            if (string.IsNullOrEmpty(galleryName) || string.IsNullOrEmpty(fileName))
            {
                return NotFound(); // Return 404 if gallery or file name is missing
            }

            var fullPath = GetImagePath(galleryName, fileName);

            // Check if the image file exists
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(); // Return 404 if the image is not found
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

            return View(viewModel); // Pass the image ViewModel to the view
        }


        // GET: /Image/ManageGalleries (Admins & Managers)
        // Displays a list of galleries with management options.
        [Authorize(Roles = "Admin,Manager")] // Restrict access to Admin and Manager roles
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

            ViewBag.ShowManagementOptions = true; // Flag to potentially show/hide UI elements in the view
            return View(galleryDirectories); // Reuse GalleryList view model for simplicity
        }

        // POST: /Image/CreateGallery (Admins & Managers)
        // Handles the creation of a new gallery directory.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public IActionResult CreateGallery(CreateGalleryViewModel model)
        {
            // Check if the model state is valid based on DataAnnotations
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid gallery name. Please check the requirements.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back to manage page with error
            }

            var galleryPath = GetGalleryPath(model.GalleryName);

            // Check if a directory with the same name already exists
            if (Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = $"A gallery named '{model.GalleryName}' already exists.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            try
            {
                Directory.CreateDirectory(galleryPath); // Create the new directory
                TempData["SuccessMessage"] = $"Gallery '{model.GalleryName}' created successfully."; // Show success message
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error creating gallery: {ex.Message}"; // Show error message
                // Log the exception for debugging
                Console.WriteLine($"Error creating gallery '{model.GalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries)); // Redirect back to manage page
        }

        // POST: /Image/DeleteGallery (Admins & Managers)
        // Handles the deletion of a gallery directory and its contents.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public IActionResult DeleteGallery(string galleryName)
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                TempData["ErrorMessage"] = "Gallery name is required to delete.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            var galleryPath = GetGalleryPath(galleryName);

            // Check if the gallery directory exists
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = $"Gallery '{galleryName}' not found.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            try
            {
                Directory.Delete(galleryPath, true); // Delete the directory and all its contents
                TempData["SuccessMessage"] = $"Gallery '{galleryName}' and its contents deleted successfully."; // Show success message
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting gallery: {ex.Message}"; // Show error message
                // Log the exception for debugging
                Console.WriteLine($"Error deleting gallery '{galleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries)); // Redirect back to manage page
        }

        // POST: /Image/RenameGallery (Admins & Managers)
        // Handles the renaming of a gallery directory.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public IActionResult RenameGallery(string oldGalleryName, string newGalleryName)
        {
            if (string.IsNullOrEmpty(oldGalleryName) || string.IsNullOrEmpty(newGalleryName))
            {
                TempData["ErrorMessage"] = "Both old and new gallery names are required.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            // Sanitize new gallery name
            var sanitizedNewGalleryName = Path.GetFileName(newGalleryName);
            if (string.IsNullOrEmpty(sanitizedNewGalleryName) || sanitizedNewGalleryName.Any(c => Path.GetInvalidFileNameChars().Contains(c) || Path.GetInvalidPathChars().Contains(c)))
            {
                TempData["ErrorMessage"] = "Invalid characters in the new gallery name.";
                return RedirectToAction(nameof(ManageGalleries));
            }


            var oldGalleryPath = GetGalleryPath(oldGalleryName);
            var newGalleryPath = GetGalleryPath(sanitizedNewGalleryName);

            // Check if the old gallery exists
            if (!Directory.Exists(oldGalleryPath))
            {
                TempData["ErrorMessage"] = $"Gallery '{oldGalleryName}' not found.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            // Check if a directory with the new name already exists
            if (Directory.Exists(newGalleryPath))
            {
                TempData["ErrorMessage"] = $"A gallery named '{sanitizedNewGalleryName}' already exists. Cannot rename.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back with error
            }

            try
            {
                Directory.Move(oldGalleryPath, newGalleryPath); // Rename the directory
                TempData["SuccessMessage"] = $"Gallery '{oldGalleryName}' renamed to '{sanitizedNewGalleryName}' successfully."; // Show success message
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error renaming gallery: {ex.Message}"; // Show error message
                // Log the exception for debugging
                Console.WriteLine($"Error renaming gallery '{oldGalleryName}' to '{sanitizedNewGalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleries)); // Redirect back to manage page
        }

        // GET: /Image/ManageGalleryImages/{galleryName} (Admins & Managers)
        // Displays images within a specific gallery with management options.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        public async Task<IActionResult> ManageGalleryImages(string galleryName) // Changed to async Task<IActionResult>
        {
            if (string.IsNullOrEmpty(galleryName))
            {
                return NotFound(); // Return 404 if gallery name is missing
            }

            var galleryPath = GetGalleryPath(galleryName);
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = "Gallery not found."; // Show error message
                return RedirectToAction(nameof(ManageGalleries)); // Redirect back to manage galleries
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
                                      .OrderBy(f => Path.GetFileName(f)) // Order files by name
                                      .ToList();

            // --- Thumbnail Recreation Check ---
            foreach (var filePath in filesInGallery)
            {
                var fileName = Path.GetFileName(filePath);
                var thumbnailPath = GetThumbnailPath(galleryName, fileName); // Thumbnail path now includes original extension

                // Check if the thumbnail exists. If not, generate it.
                if (!System.IO.File.Exists(thumbnailPath))
                {
                    await GenerateThumbnail(filePath, thumbnailPath); // Await thumbnail generation
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


            ViewBag.GalleryName = galleryName; // Pass gallery name to the view
            return View(imageFiles); // Pass the list of images to the view
        }

        // POST: /Image/UploadImage (Admins & Managers)
        // Handles the uploading of a new image to a gallery and generates a thumbnail.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public async Task<IActionResult> UploadImage(UploadImageViewModel model)
        {
            // Check if the model state is valid based on DataAnnotations
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid image file or gallery name.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
            }

            var galleryPath = GetGalleryPath(model.GalleryName);

            // Check if the gallery exists
            if (!Directory.Exists(galleryPath))
            {
                TempData["ErrorMessage"] = $"Gallery '{model.GalleryName}' not found.";
                return RedirectToAction(nameof(ManageGalleries)); // Redirect to manage galleries if gallery not found
            }

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var fileName = Path.GetFileName(model.ImageFile.FileName);
                var filePath = Path.Combine(galleryPath, fileName);

                // Prevent overwriting existing files
                if (System.IO.File.Exists(filePath))
                {
                    TempData["ErrorMessage"] = $"An image with the name '{fileName}' already exists in this gallery.";
                    return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
                }

                try
                {
                    // Save the uploaded image file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.ImageFile.CopyToAsync(stream);
                    }

                    // Generate thumbnail for the uploaded image
                    var thumbnailPath = GetThumbnailPath(model.GalleryName, fileName);
                    await GenerateThumbnail(filePath, thumbnailPath);

                    TempData["SuccessMessage"] = $"Image '{fileName}' uploaded successfully."; // Show success message
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Error uploading image: {ex.Message}"; // Show error message
                    // Log the exception for debugging
                    Console.WriteLine($"Error uploading image '{fileName}' to gallery '{model.GalleryName}': {ex.Message}");
                }
            }
            else
            {
                TempData["ErrorMessage"] = "No image file selected."; // Show error if no file was uploaded
            }

            return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back to manage images page
        }

        // POST: /Image/RenameImage (Admins & Managers)
        // Handles the renaming of an image file and its corresponding thumbnail.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public IActionResult RenameImage(RenameImageViewModel model)
        {
            // Check if the model state is valid based on DataAnnotations
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid new file name.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
            }

            var galleryPath = GetGalleryPath(model.GalleryName);
            var oldFilePath = GetImagePath(model.GalleryName, model.OldFileName);
            var newFilePath = GetImagePath(model.GalleryName, model.NewFileName);

            // Ensure new name has the same extension as the old name
            if (System.IO.Path.GetExtension(model.OldFileName).ToLower() != System.IO.Path.GetExtension(model.NewFileName).ToLower())
            {
                TempData["ErrorMessage"] = "The new file name must have the same extension as the old file name.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
            }

            // Check if the old image file exists
            if (!System.IO.File.Exists(oldFilePath))
            {
                TempData["ErrorMessage"] = $"Image '{model.OldFileName}' not found.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
            }

            // Check if a file with the new name already exists
            if (System.IO.File.Exists(newFilePath))
            {
                TempData["ErrorMessage"] = $"An image named '{model.NewFileName}' already exists in this gallery.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back with error
            }

            try
            {
                System.IO.File.Move(oldFilePath, newFilePath); // Rename the image file

                // Also rename the thumbnail if it exists
                var oldThumbnailPath = GetThumbnailPath(model.GalleryName, model.OldFileName);
                var newThumbnailPath = GetThumbnailPath(model.GalleryName, model.NewFileName);
                if (System.IO.File.Exists(oldThumbnailPath))
                {
                    System.IO.File.Move(oldThumbnailPath, newThumbnailPath);
                }

                TempData["SuccessMessage"] = $"Image '{model.OldFileName}' renamed to '{model.NewFileName}' successfully."; // Show success message
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error renaming image: {ex.Message}"; // Show error message
                // Log the exception for debugging
                Console.WriteLine($"Error renaming image '{model.OldFileName}' to '{model.NewFileName}' in gallery '{model.GalleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleryImages), new { galleryName = model.GalleryName }); // Redirect back to manage images page
        }


        // POST: /Image/DeleteImage (Admins & Managers)
        // Handles the deletion of an image file and its corresponding thumbnail.
        [Authorize(Roles = "Admin,Manager")] // Restrict access
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent CSRF attacks
        public IActionResult DeleteImage(string galleryName, string fileName)
        {
            if (string.IsNullOrEmpty(galleryName) || string.IsNullOrEmpty(fileName))
            {
                TempData["ErrorMessage"] = "Gallery name and file name are required to delete.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName }); // Redirect back with error
            }

            var filePath = GetImagePath(galleryName, fileName);
            var thumbnailPath = GetThumbnailPath(galleryName, fileName);

            // Check if the image file exists
            if (!System.IO.File.Exists(filePath))
            {
                TempData["ErrorMessage"] = $"Image '{fileName}' not found in gallery '{galleryName}'.";
                return RedirectToAction(nameof(ManageGalleryImages), new { galleryName }); // Redirect back with error
            }

            try
            {
                System.IO.File.Delete(filePath); // Delete the image file
                // Delete the thumbnail if it exists
                if (System.IO.File.Exists(thumbnailPath))
                {
                    System.IO.File.Delete(thumbnailPath);
                }
                TempData["SuccessMessage"] = $"Image '{fileName}' deleted successfully."; // Show success message
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting image: {ex.Message}"; // Show error message
                // Log the exception for debugging
                Console.WriteLine($"Error deleting image '{fileName}' from gallery '{galleryName}': {ex.Message}");
            }

            return RedirectToAction(nameof(ManageGalleryImages), new { galleryName }); // Redirect back to manage images page
        }
    }
}