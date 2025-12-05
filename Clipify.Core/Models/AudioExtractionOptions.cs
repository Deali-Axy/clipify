namespace Clipify.Core.Models;

/// <summary>
/// 音频提取选项
/// </summary>
public class AudioExtractionOptions
{
    /// <summary>
    /// 视频文件路径
    /// </summary>
    public string? VideoPath { get; set; }
    
    /// <summary>
    /// 输出目录
    /// </summary>
    public string? OutputDir { get; set; }
    
    /// <summary>
    /// 输出格式
    /// </summary>
    public string OutputFormat { get; set; } = "mp3";
    
    /// <summary>
    /// 获取输出文件路径
    /// </summary>
    public string? OutputPath => string.IsNullOrWhiteSpace(OutputDir) || string.IsNullOrWhiteSpace(VideoPath) 
        ? null 
        : Path.Combine(OutputDir, $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}");
} 