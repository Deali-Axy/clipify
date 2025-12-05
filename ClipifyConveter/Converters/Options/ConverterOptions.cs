namespace ClipifyConveter.Converters.Options;

/// <summary>
/// 转换器选项基类
/// </summary>
public abstract class ConverterOptions {
    /// <summary>
    /// 是否自动覆盖同名文件
    /// </summary>
    public bool AutoOverwrite { get; set; } = true;
}