namespace Clipify.Core.Models;

/// <summary>
/// 视频处理选项
/// </summary>
public class VideoProcessingOptions
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
    public string OutputFormat { get; set; } = "mp4";
    
    /// <summary>
    /// 是否启用开始时间
    /// </summary>
    public bool EnableStartTime { get; set; }
    
    /// <summary>
    /// 是否启用结束时间
    /// </summary>
    public bool EnableEndTime { get; set; }
    
    /// <summary>
    /// 开始时间（小时）
    /// </summary>
    public int StartHour { get; set; }
    
    /// <summary>
    /// 开始时间（分钟）
    /// </summary>
    public int StartMinute { get; set; }
    
    /// <summary>
    /// 开始时间（秒）
    /// </summary>
    public int StartSecond { get; set; }
    
    /// <summary>
    /// 结束时间（小时）
    /// </summary>
    public int EndHour { get; set; }
    
    /// <summary>
    /// 结束时间（分钟）
    /// </summary>
    public int EndMinute { get; set; }
    
    /// <summary>
    /// 结束时间（秒）
    /// </summary>
    public int EndSecond { get; set; }
    
    /// <summary>
    /// 是否使用相同质量
    /// </summary>
    public bool UseSameQuality { get; set; } = true;
    
    /// <summary>
    /// 获取输出文件路径
    /// </summary>
    public string? OutputPath => string.IsNullOrWhiteSpace(OutputDir) || string.IsNullOrWhiteSpace(VideoPath) 
        ? null 
        : Path.Combine(OutputDir, $"{Path.GetFileNameWithoutExtension(VideoPath)}_cut.{OutputFormat}");
} 