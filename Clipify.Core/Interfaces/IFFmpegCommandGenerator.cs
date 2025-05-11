using Clipify.Core.Models;

namespace Clipify.Core.Interfaces;

/// <summary>
/// FFmpeg命令生成器接口
/// </summary>
public interface IFFmpegCommandGenerator
{
    /// <summary>
    /// 生成视频分割命令参数
    /// </summary>
    /// <param name="options">视频处理选项</param>
    /// <returns>FFmpeg命令参数</returns>
    string? GenerateVideoSplitArguments(VideoProcessingOptions options);
    
    /// <summary>
    /// 生成音频提取命令参数
    /// </summary>
    /// <param name="options">音频提取选项</param>
    /// <returns>FFmpeg命令参数</returns>
    string? GenerateAudioExtractionArguments(AudioExtractionOptions options);
} 