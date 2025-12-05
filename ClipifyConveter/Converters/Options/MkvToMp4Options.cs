namespace ClipifyConveter.Converters.Options;

/// <summary>
/// 视频编码器类型
/// </summary>
public enum VideoEncoder {
    /// <summary>
    /// 软件编码 - libx264
    /// </summary>
    Software_X264,

    /// <summary>
    /// NVIDIA 硬件编码 - h264_nvenc
    /// </summary>
    Hardware_NVENC,

    /// <summary>
    /// Intel 硬件编码 - h264_qsv
    /// </summary>
    Hardware_QSV,

    /// <summary>
    /// AMD 硬件编码 - h264_amf
    /// </summary>
    Hardware_AMF
}

/// <summary>
/// 编码预设（速度/质量平衡）
/// </summary>
public enum EncodePreset {
    /// <summary>
    /// 极快（质量较低）
    /// </summary>
    UltraFast,

    /// <summary>
    /// 超快
    /// </summary>
    SuperFast,

    /// <summary>
    /// 很快
    /// </summary>
    VeryFast,

    /// <summary>
    /// 快速
    /// </summary>
    Faster,

    /// <summary>
    /// 快
    /// </summary>
    Fast,

    /// <summary>
    /// 中等（默认，平衡）
    /// </summary>
    Medium,

    /// <summary>
    /// 慢
    /// </summary>
    Slow,

    /// <summary>
    /// 较慢
    /// </summary>
    Slower,

    /// <summary>
    /// 很慢（质量最高）
    /// </summary>
    VerySlow
}

/// <summary>
/// MKV 转 MP4 转换选项
/// </summary>
public class MkvToMp4Options : ConverterOptions {
    /// <summary>
    /// 视频编码器
    /// </summary>
    public VideoEncoder VideoEncoder { get; set; } = VideoEncoder.Software_X264;

    /// <summary>
    /// 音频编码器（默认 AAC）
    /// </summary>
    public string AudioCodec { get; set; } = "aac";

    /// <summary>
    /// 编码预设
    /// </summary>
    public EncodePreset Preset { get; set; } = EncodePreset.Medium;

    /// <summary>
    /// 视频质量控制（CRF 值，0-51，越小质量越好）
    /// 软件编码推荐：18-28，默认 23
    /// 硬件编码此值可能不适用，会使用码率控制
    /// </summary>
    public int Quality { get; set; } = 23;

    /// <summary>
    /// 硬件编码时的码率（kbps）
    /// 为 0 时使用 CRF，否则使用码率控制
    /// </summary>
    public int Bitrate { get; set; } = 0;

    /// <summary>
    /// 获取视频编码器名称
    /// </summary>
    public string GetVideoEncoderName() {
        return VideoEncoder switch {
            VideoEncoder.Software_X264 => "libx264",
            VideoEncoder.Hardware_NVENC => "h264_nvenc",
            VideoEncoder.Hardware_QSV => "h264_qsv",
            VideoEncoder.Hardware_AMF => "h264_amf",
            _ => "libx264"
        };
    }

    /// <summary>
    /// 获取预设名称
    /// </summary>
    public string GetPresetName() {
        return Preset switch {
            EncodePreset.UltraFast => "ultrafast",
            EncodePreset.SuperFast => "superfast",
            EncodePreset.VeryFast => "veryfast",
            EncodePreset.Faster => "faster",
            EncodePreset.Fast => "fast",
            EncodePreset.Medium => "medium",
            EncodePreset.Slow => "slow",
            EncodePreset.Slower => "slower",
            EncodePreset.VerySlow => "veryslow",
            _ => "medium"
        };
    }

    /// <summary>
    /// 是否使用硬件编码
    /// </summary>
    public bool IsHardwareEncoder() {
        return VideoEncoder != VideoEncoder.Software_X264;
    }
}