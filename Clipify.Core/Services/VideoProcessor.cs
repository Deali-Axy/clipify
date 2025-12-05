using Clipify.Core.Interfaces;
using Clipify.Core.Models;
using FFmpeg.NET;

namespace Clipify.Core.Services;

/// <summary>
/// 视频处理器实现
/// </summary>
public class VideoProcessor : IVideoProcessor
{
    private readonly IVideoService _videoService;
    private readonly IFFmpegCommandGenerator _commandGenerator;
    private readonly IMessageService _messageService;

    /// <summary>
    /// 初始化视频处理器
    /// </summary>
    /// <param name="videoService">视频服务</param>
    /// <param name="commandGenerator">命令生成器</param>
    /// <param name="messageService">消息服务</param>
    public VideoProcessor(
        IVideoService videoService, 
        IFFmpegCommandGenerator commandGenerator, 
        IMessageService messageService)
    {
        _videoService = videoService;
        _commandGenerator = commandGenerator;
        _messageService = messageService;
    }

    /// <summary>
    /// 分割视频
    /// </summary>
    /// <param name="options">视频处理选项</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果</returns>
    public async Task<bool> SplitVideoAsync(VideoProcessingOptions options, IProgress<double>? progressCallback = null, CancellationToken? cancellationToken = null)
    {
        try
        {
            // 验证参数
            if (string.IsNullOrWhiteSpace(options.VideoPath))
            {
                await _messageService.Error("未选择文件");
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.OutputDir))
            {
                await _messageService.Error("没有选择输出目录");
                return false;
            }

            // 获取视频元数据
            var metadata = await GetVideoMetadataAsync(options.VideoPath, cancellationToken);
            if (metadata == null)
            {
                await _messageService.Error("视频读取失败");
                return false;
            }

            // 生成命令参数
            var args = _commandGenerator.GenerateVideoSplitArguments(options);
            if (string.IsNullOrWhiteSpace(args))
            {
                await _messageService.Error("无法生成ffmpeg命令参数");
                return false;
            }

            // 检查时间设置的有效性
            if (options.EnableStartTime && options.EnableEndTime)
            {
                var startSeconds = options.StartHour * 3600 + options.StartMinute * 60 + options.StartSecond;
                var endSeconds = options.EndHour * 3600 + options.EndMinute * 60 + options.EndSecond;
                
                if (endSeconds <= startSeconds)
                {
                    await _messageService.Error("结束时间必须大于开始时间");
                    return false;
                }
                
                // 检查结束时间是否超过视频总长度
                var totalSeconds = (int)metadata.Duration.TotalSeconds;
                if (endSeconds > totalSeconds)
                {
                    await _messageService.Warning($"结束时间超过视频总长度 ({totalSeconds / 3600}:{(totalSeconds % 3600) / 60}:{totalSeconds % 60})");
                }
            }

            // 执行FFmpeg命令
            var inputFile = new InputFile(options.VideoPath);
            var outputFile = new OutputFile(options.OutputPath);
            
            var conversionOptions = new ConversionOptions();
            await _videoService.FFmpeg.ConvertAsync(inputFile, outputFile, conversionOptions, cancellationToken ?? CancellationToken.None);
            
            await _messageService.Success("视频分割完成");
            return true;
        }
        catch (Exception ex)
        {
            await _messageService.Error($"视频分割失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 提取音频
    /// </summary>
    /// <param name="options">音频提取选项</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果</returns>
    public async Task<bool> ExtractAudioAsync(AudioExtractionOptions options, IProgress<double>? progressCallback = null, CancellationToken? cancellationToken = null)
    {
        try
        {
            // 验证参数
            if (string.IsNullOrWhiteSpace(options.VideoPath))
            {
                await _messageService.Error("未选择文件");
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.OutputDir))
            {
                await _messageService.Error("没有选择输出目录");
                return false;
            }

            // 获取视频元数据
            var metadata = await GetVideoMetadataAsync(options.VideoPath, cancellationToken);
            if (metadata == null)
            {
                await _messageService.Error("视频读取失败");
                return false;
            }

            // 生成命令参数
            var args = _commandGenerator.GenerateAudioExtractionArguments(options);
            if (string.IsNullOrWhiteSpace(args))
            {
                await _messageService.Error("无法生成ffmpeg命令参数");
                return false;
            }

            // 执行FFmpeg命令
            var inputFile = new InputFile(options.VideoPath);
            var outputFile = new OutputFile(options.OutputPath);
            
            var conversionOptions = new ConversionOptions
            {
                ExtraArguments = "-map a -c:a copy" // 提取音频并复制编码
            };
            
            await _videoService.FFmpeg.ConvertAsync(inputFile, outputFile, conversionOptions, cancellationToken ?? CancellationToken.None);
            
            await _messageService.Success("音频提取完成");
            return true;
        }
        catch (Exception ex)
        {
            await _messageService.Error($"音频提取失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取视频元数据
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>视频元数据</returns>
    public async Task<MetaData?> GetVideoMetadataAsync(string videoPath, CancellationToken? cancellationToken = null)
    {
        try
        {
            var inputFile = new InputFile(videoPath);
            return await _videoService.FFmpeg.GetMetaDataAsync(inputFile, cancellationToken ?? CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }
} 