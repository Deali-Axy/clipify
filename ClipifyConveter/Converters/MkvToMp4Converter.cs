namespace ClipifyConveter.Converters;

/// <summary>
/// MKV 转 MP4 转换器（转换为 H.264 编码）
/// </summary>
public class MkvToMp4Converter : BaseConverter {
    public override string Name => "MKV转MP4转换器(H.264)";
    public override string SourceExtension => ".mkv";
    public override string TargetExtension => ".mp4";

    protected override string GenerateFfmpegArguments(string sourceFile, string targetFile) {
        // -c:v libx264 使用 H.264 编码视频
        // -c:a aac 使用 AAC 编码音频
        // -preset medium 编码速度/质量平衡（可选：ultrafast, fast, medium, slow, veryslow）
        // -crf 23 视频质量（0-51，越小质量越好，23是默认值）
        // -y 自动覆盖同名文件
        return $"-i \"{sourceFile}\" -c:v libx264 -c:a aac -preset medium -crf 23 -y \"{targetFile}\"";
    }
}