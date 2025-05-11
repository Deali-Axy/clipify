using Clipify.Core.Interfaces;
using Clipify.Core.Models;

namespace Clipify.Core.Services;

/// <summary>
/// FFmpeg命令生成器实现
/// </summary>
public class FFmpegCommandGenerator : IFFmpegCommandGenerator
{
    /// <summary>
    /// 生成视频分割命令参数
    /// </summary>
    /// <param name="options">视频处理选项</param>
    /// <returns>FFmpeg命令参数</returns>
    public string? GenerateVideoSplitArguments(VideoProcessingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.VideoPath) || string.IsNullOrWhiteSpace(options.OutputDir))
        {
            return null;
        }

        var args = "-y -hide_banner -progress pipe:1";

        // 处理开始时间和结束时间
        if (options.EnableStartTime)
        {
            var startTime = $"{options.StartHour}:{options.StartMinute}:{options.StartSecond}";
            args += $" -ss {startTime}";
        }

        // 如果仅设置结束时间而没有开始时间，则从0开始
        if (options.EnableEndTime && !options.EnableStartTime)
        {
            args += " -ss 0:0:0";
        }

        // 计算时长（当同时有开始和结束时间）
        if (options.EnableStartTime && options.EnableEndTime)
        {
            var startSeconds = options.StartHour * 3600 + options.StartMinute * 60 + options.StartSecond;
            var endSeconds = options.EndHour * 3600 + options.EndMinute * 60 + options.EndSecond;
            
            // 确保结束时间大于开始时间
            if (endSeconds > startSeconds)
            {
                var durationSeconds = endSeconds - startSeconds;
                var durationHour = durationSeconds / 3600;
                var durationMinute = (durationSeconds % 3600) / 60;
                var durationSecond = durationSeconds % 60;
                
                args += $" -t {durationHour}:{durationMinute}:{durationSecond}";
            }
        } 
        // 如果只有结束时间，则直接使用结束时间作为时长
        else if (options.EnableEndTime)
        {
            var duration = $"{options.EndHour}:{options.EndMinute}:{options.EndSecond}";
            args += $" -t {duration}";
        }

        args += $" -i \"{options.VideoPath}\"";

        // 根据选择决定是否保持原视频质量
        if (options.UseSameQuality)
        {
            args += " -c copy";
        }

        args += $" \"{options.OutputPath}\"";
        return args;
    }

    /// <summary>
    /// 生成音频提取命令参数
    /// </summary>
    /// <param name="options">音频提取选项</param>
    /// <returns>FFmpeg命令参数</returns>
    public string? GenerateAudioExtractionArguments(AudioExtractionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.VideoPath) || string.IsNullOrWhiteSpace(options.OutputDir))
        {
            return null;
        }

        return $"-y -hide_banner -progress pipe:1 -i \"{options.VideoPath}\" -map a -c:a copy \"{options.OutputPath}\"";
    }
} 