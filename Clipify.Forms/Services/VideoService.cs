using System.Security.Cryptography;
using FFmpeg.NET;

namespace Clipify.Forms.Services;

public class VideoService {
    private readonly IHostingEnvironment _environment;
    public Engine FFmpeg { get; }

    public VideoService(IHostingEnvironment environment) {
        _environment = environment;
        FFmpeg = new Engine();
    }

    public static string GetFileMd5(string filePath) {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    // 结合文件的其他属性（如文件名、修改时间等）进行 MD5 计算
    public static string GetFileMetadataMd5(string filePath) {
        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);
        var metaData = fileName + fileInfo.LastWriteTimeUtc.ToString();

        using var md5 = MD5.Create();
        var metaBytes = System.Text.Encoding.UTF8.GetBytes(metaData);
        var hash = md5.ComputeHash(metaBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public async Task<string> GenerateThumbnailAsync(string videoPath, CancellationToken? cancellationToken = null) {
        var inputFile = new InputFile(videoPath);
        var tempThumbnailDir = Path.Combine(_environment.WebRootPath, "temp", "thumbnails");
        if (!Directory.Exists(tempThumbnailDir)) {
            Directory.CreateDirectory(tempThumbnailDir);
        }

        var filename = $"{GetFileMetadataMd5(videoPath)}.jpeg";
        var outputPath = Path.Combine(tempThumbnailDir, filename);
        var outputFile = new OutputFile(outputPath);

        if (!File.Exists(outputPath)) {
            await FFmpeg.GetThumbnailAsync(inputFile, outputFile, cancellationToken ?? CancellationToken.None);
        }

        return $"temp/thumbnails/{filename}";
    }
}