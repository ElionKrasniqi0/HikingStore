namespace HikingStore.Services
{
    public record ImageUploadResult(bool Success, string? FileName, string? Error);

    /// <summary>
    /// Validates and stores uploaded images under wwwroot. The file type is detected from the file's
    /// content (magic bytes), never from the extension or content type supplied by the browser.
    /// </summary>
    public class ImageUploadService
    {
        public const long MaxFileSizeBytes = 5 * 1024 * 1024;
        public const string ProductFolder = "Product";
        public const string CategoryFolder = "category";

        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageUploadService> _logger;

        public ImageUploadService(IWebHostEnvironment environment, ILogger<ImageUploadService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<ImageUploadResult> SaveAsync(IFormFile? file, string folder)
        {
            if (file == null || file.Length == 0)
            {
                return new(false, null, "Please choose an image file.");
            }
            if (file.Length > MaxFileSizeBytes)
            {
                return new(false, null, "Images must be 5 MB or smaller.");
            }

            var header = new byte[12];
            await using (var stream = file.OpenReadStream())
            {
                var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
                if (read < header.Length)
                {
                    return new(false, null, "The file is not a valid image.");
                }
            }

            var extension = DetectImageExtension(header);
            if (extension == null)
            {
                return new(false, null, "Only JPEG, PNG, WebP or GIF images are allowed.");
            }

            var directory = GetFolderPath(folder);
            Directory.CreateDirectory(directory);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(directory, fileName);

            await using (var target = new FileStream(fullPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(target);
            }

            return new(true, fileName, null);
        }

        public void Delete(string? fileName, string folder)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var safeName = Path.GetFileName(fileName);
            if (safeName != fileName) return;

            var fullPath = Path.Combine(GetFolderPath(folder), safeName);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete image {File}", fullPath);
            }
        }

        private string GetFolderPath(string folder) => Path.Combine(_environment.WebRootPath, folder);

        public static string? DetectImageExtension(ReadOnlySpan<byte> header)
        {
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return ".jpg";
            if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return ".png";
            if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
                return ".gif";
            if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
                return ".webp";
            return null;
        }
    }
}
