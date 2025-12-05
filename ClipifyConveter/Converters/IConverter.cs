namespace ClipifyConveter.Converters;

/// <summary>
/// 视频转换器接口
/// </summary>
public interface IConverter {
    /// <summary>
    /// 获取转换器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取支持的源文件扩展名
    /// </summary>
    string SourceExtension { get; }

    /// <summary>
    /// 获取目标文件扩展名
    /// </summary>
    string TargetExtension { get; }

    /// <summary>
    /// 执行转换
    /// </summary>
    /// <param name="targetDirectory">目标目录</param>
    /// <returns>转换结果</returns>
    Task<bool> ConvertAsync(string targetDirectory);
}