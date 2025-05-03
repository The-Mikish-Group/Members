// ImageDisplayHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace Members.Helpers
{
    public class ImageDisplayHelper
    {
        private static IWebHostEnvironment? _env;
        public static void Initialize(IWebHostEnvironment env)
        {
            _env = env;
        }

        public static ImageData GetImageData(string imagesFolder, string currentController, string imagefolder)
        {
            var currentDisplay = currentController.Replace("slide", "", StringComparison.OrdinalIgnoreCase);

            var folders = Directory.GetDirectories(Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot/{imagesFolder}/{currentDisplay}"))
                                   .Select(folder => Path.GetFileName(folder))
                                   .ToList();

            var currentImageFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagesFolder, currentDisplay, imagefolder);

            var files = Directory.GetFiles(currentImageFolder, "*.*")
                    .Where(file =>
                        !Path.GetFileName(file).StartsWith("background", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(file).EndsWith("_thumb.jpg", StringComparison.OrdinalIgnoreCase) &&
                        (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(file).Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase)))
                    .Select(file => $"~/{Path.Combine(imagesFolder, currentDisplay, imagefolder, Path.GetFileName(file)).Replace("\\", "/")}")
                    .ToList();

            // Update the instantiation of ThumbnailService to pass the required 'env' parameter.
            var thumbnailService = new ThumbnailService(_env ?? throw new InvalidOperationException("Environment is not initialized."));

            var thumbnailsPath = currentImageFolder;


            foreach (var file in files)
            {
                thumbnailService.CreateOrRetrieveThumbnail(file, Path.Combine(thumbnailsPath, $"{Path.GetFileNameWithoutExtension(file)}_thumb.jpg"), thumbnailsPath);
            }

            var backgroundImage = GetBackgroundImage(imagesFolder, currentDisplay, imagefolder);

            return new ImageData
            {
                Folders = folders,
                Files = files,
                CurrentDisplay = currentDisplay,
                ImageFolder = imagefolder,
                ThumbnailsPath = thumbnailsPath,
                BackgroundImage = backgroundImage
            };
        }

        private static string GetBackgroundImage(string slideshowsFolder, string currentDisplay, string imagefolder)
        {
            var backgroundImage = $"{slideshowsFolder}/{currentDisplay}/{imagefolder}/background.webp"; // default
            string[] extensions = { ".jpg", ".png", ".webp", ".svg" };
            foreach (var extension in extensions)
            {
                var bgPath = $"{slideshowsFolder}/{currentDisplay}/{imagefolder}/background{extension}";
                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", bgPath)))
                {
                    backgroundImage = bgPath;
                    break;
                }
            }

            return backgroundImage;
        }
    }

    public class ImageData
    {
        public List<string>? Folders { get; set; }
        public List<string>? Files { get; set; }
        public string? CurrentDisplay { get; set; }
        public string? ImageFolder { get; set; }
        public string ThumbnailsPath { get; set; } = string.Empty; // Initialize with a default value
        public string? BackgroundImage { get; set; }
    }

    public class ThumbnailService
    {
        private IWebHostEnvironment? _env;

        public ThumbnailService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public void CreateOrRetrieveThumbnail(string file, string thumbnail, string thumbnailsPath)
        {
            try
            {
                if (!File.Exists(thumbnail))
                {
                    CreateThumbnail(file, thumbnailsPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating thumbnail: {ex.Message}");
            }
        }

        public void CreateThumbnail(string file, string thumbnailsPath)
        {
            try
            {
                int thumbnailWidth = 800;
                int thumbnailHeight = 800;

                if (_env?.WebRootPath == null)
                {
                    throw new InvalidOperationException("WebRootPath is null");
                }

                string filePath = Path.Combine(_env.WebRootPath, file.TrimStart('~', '/'));
                using var originalImage = Image.Load(filePath); // Simplified 'using' statement (IDE0063)
                originalImage.Mutate(x => x
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(thumbnailWidth, thumbnailHeight),
                        Mode = ResizeMode.Max
                    }));

                originalImage.Save(Path.Combine(thumbnailsPath, Path.GetFileNameWithoutExtension(file) + "_thumb.jpg"), GetImageFormat(Path.GetExtension(file)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private static IImageEncoder GetImageFormat(string extension)
        {
            switch (extension.ToLower())
            {
                case ".png":
                    return new PngEncoder();
                case ".jpg":
                case ".jpeg":
                    return new JpegEncoder();
                case ".gif":
                    return new GifEncoder();
                case ".bmp":
                    return new BmpEncoder();
                default:
                    return new JpegEncoder();
            }
        }
    }
}