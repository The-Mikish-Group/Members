using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;

namespace Members.Helpers
{
    public static class GalleryHelper
    {
        public static List<string> GetGalleryFolders(string baseDirectory, string galleryFolder)
        {
            var directories = Directory.GetDirectories(Path.Combine(baseDirectory, $"wwwroot/{galleryFolder}"));
            return directories
                .Where(folder =>
                {
                    char underscore = '_';
                    var folderName = Path.GetFileName(folder);
                    return !(folderName == null
                    || folderName.StartsWith(underscore));
                })
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .ToList()!;
        }

        public static List<GalleryFile> GetGalleryFiles(string folderPath, string[] allowedExtensions)
        {
            return [.. Directory.GetFiles(folderPath)
                .Where(file =>
                {
                    var fileName = Path.GetFileName(file);
                    var fileExtension = Path.GetExtension(file);
                    return fileName != null &&
                           fileExtension != null &&
                           allowedExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase) &&
                           !fileName.StartsWith("background", StringComparison.OrdinalIgnoreCase) &&
                           !fileName.EndsWith("_thumb.jpg", StringComparison.OrdinalIgnoreCase);
                })
                .Select(file => new GalleryFile
                {
                    Path = file,
                    Dimensions = GetImageDimensions(file)
                })
                .OrderBy(file => file.Dimensions.Width)
                .ThenBy(file => file.Dimensions.Height)];
        }

        private static Size GetImageDimensions(string imagePath)
        {
            using var image = Image.Load(imagePath);
            return new Size(image.Width, image.Height);
        }
    }

    public class GalleryFile
    {
        public string Path { get; set; } = string.Empty;
        public Size Dimensions { get; set; }
    }
}
