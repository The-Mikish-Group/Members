using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Members.Helpers;

public static class ImageHelper
{
    private static IWebHostEnvironment? _webHostEnvironment;
    public static void Initialize(IWebHostEnvironment webHostEnvironment)
    {
        _webHostEnvironment = webHostEnvironment;
    }
    public static void CreateThumbnail(string file, string thumbnailsPath, int thumbnailWidth, int thumbnailHeight)
    {
        if (_webHostEnvironment == null)
        {
            return;
        }
        ;

        string filePath = Path.Combine(_webHostEnvironment.WebRootPath, file.TrimStart('~', '/')).Replace("\\", "/");
        using var originalImage = Image.Load(filePath);
        originalImage.Mutate(x => x
            .Resize(new ResizeOptions
            {
                Size = new Size(thumbnailWidth, thumbnailHeight),
                Mode = ResizeMode.Max
            }));
        originalImage.Save(Path.Combine(thumbnailsPath, Path.GetFileNameWithoutExtension(file) + "_thumb.jpg"), GetImageFormat(Path.GetExtension(filePath)));
    }
    private static IImageEncoder GetImageFormat(string extension)
    {
        return extension.ToLower() switch
        {
            ".png" => new PngEncoder(),
            ".jpg" or ".jpeg" => new JpegEncoder(),
            ".gif" => new GifEncoder(),
            ".bmp" => new BmpEncoder(),
            _ => new JpegEncoder()
        };
    }
}
