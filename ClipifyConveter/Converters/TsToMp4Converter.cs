namespace ClipifyConveter.Converters;

/// <summary>
/// TS 转 MP4 转换器
/// </summary>
public class TsToMp4Converter : BaseConverter {
    public override string Name => "TS转MP4转换器";
    public override string SourceExtension => ".ts";
    public override string TargetExtension => ".mp4";

    protected override string GenerateFfmpegArguments(string sourceFile, string targetFile) {
        // -y 表示自动覆盖同名文件
        // -c copy 只做封装转换，不做重新编码
        return $"-i \"{sourceFile}\" -c copy -y \"{targetFile}\"";
    }
}