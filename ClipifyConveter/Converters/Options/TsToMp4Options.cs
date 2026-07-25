namespace ClipifyConveter.Converters.Options;

/// <summary>
/// TS 转 MP4 转换选项
/// </summary>
public class TsToMp4Options : ConverterOptions {
    /// <summary>
    /// 是否只做封装转换（不重新编码）
    /// </summary>
    public bool CopyCodec { get; set; } = true;
}