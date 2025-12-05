using ClipifyConveter.Converters.Options;

namespace ClipifyConveter.Converters;

/// <summary>
/// TS 转 MP4 转换器
/// </summary>
public class TsToMp4Converter : BaseConverter {
    private readonly TsToMp4Options _options = new();
    
    public override string Name => "TS转MP4转换器";
    public override string SourceExtension => ".ts";
    public override string TargetExtension => ".mp4";
    public override ConverterOptions Options => _options;

    protected override string GenerateFfmpegArguments(string sourceFile, string targetFile) {
        var overwriteFlag = _options.AutoOverwrite ? "-y" : "-n";
        
        if (_options.CopyCodec) {
            // 只做封装转换，不做重新编码
            return $"-i \"{sourceFile}\" -c copy {overwriteFlag} \"{targetFile}\"";
        }
        else {
            // 重新编码
            return $"-i \"{sourceFile}\" -c:v libx264 -c:a aac {overwriteFlag} \"{targetFile}\"";
        }
    }
}