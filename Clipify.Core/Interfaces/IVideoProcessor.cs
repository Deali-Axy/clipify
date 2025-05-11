using Clipify.Core.Models;
using FFmpeg.NET;

namespace Clipify.Core.Interfaces;

/// <summary>
/// 视频处理器接口
/// </summary>
public interface IVideoProcessor
{
    /// <summary>
    /// 分割视频
    /// </summary>
    /// <param name="options">视频处理选项</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果</returns>
    Task<bool> SplitVideoAsync(VideoProcessingOptions options, IProgress<double>? progressCallback = null, CancellationToken? cancellationToken = null);
    
    /// <summary>
    /// 提取音频
    /// </summary>
    /// <param name="options">音频提取选项</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果</returns>
    Task<bool> ExtractAudioAsync(AudioExtractionOptions options, IProgress<double>? progressCallback = null, CancellationToken? cancellationToken = null);
    
    /// <summary>
    /// 获取视频元数据
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>视频元数据</returns>
    Task<MetaData?> GetVideoMetadataAsync(string videoPath, CancellationToken? cancellationToken = null);
} 