using FFmpeg.NET;

namespace Clipify.Core.Interfaces;

/// <summary>
/// 视频处理服务接口
/// </summary>
public interface IVideoService
{
    /// <summary>
    /// 获取FFmpeg引擎实例
    /// </summary>
    Engine FFmpeg { get; }
    
    /// <summary>
    /// 计算文件MD5哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MD5哈希值</returns>
    string GetFileMd5(string filePath);
    
    /// <summary>
    /// 根据文件元数据计算MD5哈希值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MD5哈希值</returns>
    string GetFileMetadataMd5(string filePath);
    
    /// <summary>
    /// 生成视频缩略图
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缩略图相对路径</returns>
    Task<string> GenerateThumbnailAsync(string videoPath, CancellationToken? cancellationToken = null);
} 