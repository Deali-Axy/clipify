using System.Security.Cryptography;
using Clipify.Core.Interfaces;
using FFmpeg.NET;

namespace Clipify.Maui.Services;

/// <summary>
/// MAUI视频服务实现
/// </summary>
public class VideoServiceImpl : IVideoService {
    private readonly IHostingEnvironment _environment;

    /// <summary>
    /// 获取FFmpeg引擎实例
    /// </summary>
    public Engine FFmpeg { get; }

    /// <summary>
    /// 初始化视频服务
    /// </summary>
    /// <param name="environment">环境服务</param>
    public VideoServiceImpl(IHostingEnvironment environment) {
        _environment = environment;

        // todo 不在代码中指定 ffmpeg 路径，后续支持用户自行配置
        string ffmpegPath = "";

        FFmpeg = new Engine();
    }

    /// <summary>
    /// 计算文件MD5哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MD5哈希值</returns>
    public string GetFileMd5(string filePath) {
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
    public string GetFileMetadataMd5(string filePath) {
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
    public async Task<string> GenerateThumbnailAsync(string videoPath, CancellationToken? cancellationToken = null) {
        var tempThumbnailDir = Path.Combine(_environment.WebRootPath, "temp", "thumbnails");
        if (!Directory.Exists(tempThumbnailDir)) {
            Directory.CreateDirectory(tempThumbnailDir);
        }

        var filename = $"{GetFileMetadataMd5(videoPath)}.jpeg";
        var outputPath = Path.Combine(tempThumbnailDir, filename);

        // 这里我们简化实现，在真实应用中需要实际调用FFmpeg生成缩略图
        // 在MAUI中，我们可能需要使用平台特定的实现或其他库

        // 模拟异步操作
        await Task.Delay(100);

        return $"temp/thumbnails/{filename}";
    }
}