using System.Security.Cryptography;
using Clipify.Core.Interfaces;
using FFmpeg.NET;

namespace Clipify.Forms.Services;

/// <summary>
/// 视频服务实现
/// </summary>
public class VideoServiceImpl : IVideoService
{
    private readonly IHostingEnvironment _environment;
    
    /// <summary>
    /// 获取FFmpeg引擎实例
    /// </summary>
    public Engine FFmpeg { get; }

    /// <summary>
    /// 初始化视频服务
    /// </summary>
    /// <param name="environment">环境服务</param>
    public VideoServiceImpl(IHostingEnvironment environment)
    {
        _environment = environment;
        FFmpeg = new Engine();
    }

    /// <summary>
    /// 计算文件MD5哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MD5哈希值</returns>
    public string GetFileMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 根据文件元数据计算MD5哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MD5哈希值</returns>
    public string GetFileMetadataMd5(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);
        var metaData = fileName + fileInfo.LastWriteTimeUtc.ToString();

        using var md5 = MD5.Create();
        var metaBytes = System.Text.Encoding.UTF8.GetBytes(metaData);
        var hash = md5.ComputeHash(metaBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 生成视频缩略图
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缩略图相对路径</returns>
    public async Task<string> GenerateThumbnailAsync(string videoPath, CancellationToken? cancellationToken = null)
    {
        var inputFile = new InputFile(videoPath);
        var tempThumbnailDir = Path.Combine(_environment.WebRootPath, "temp", "thumbnails");
        if (!Directory.Exists(tempThumbnailDir))
        {
            Directory.CreateDirectory(tempThumbnailDir);
        }

        var filename = $"{GetFileMetadataMd5(videoPath)}.jpeg";
        var outputPath = Path.Combine(tempThumbnailDir, filename);
        var outputFile = new OutputFile(outputPath);

        var opt = new ConversionOptions
        {
            HideBanner = true,
            HWAccelOutputFormatCopy = true,
            MapMetadata = true,
        };
        
        if (!File.Exists(outputPath))
        {
            await FFmpeg.GetThumbnailAsync(inputFile, outputFile, cancellationToken ?? CancellationToken.None);
        }

        return $"temp/thumbnails/{filename}";
    }
} 