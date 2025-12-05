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

    protected override List<string> GenerateFfmpegArguments(string sourceFile, string targetFile) {
        var args = new List<string> {
            "-i",
            sourceFile
        };
            
        if (_options.CopyCodec) {
            // 只做封装转换，不做重新编码
            args.Add("-c");
            args.Add("copy");
        }
        else {
            // 重新编码
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-c:a");
            args.Add("aac");
        }
            
        // 覆盖标志
        args.Add(_options.AutoOverwrite ? "-y" : "-n");
            
        // 目标文件
        args.Add(targetFile);
            
        return args;
    }
}