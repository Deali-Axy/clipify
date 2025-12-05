namespace Clipify.Core.Interfaces;

/// <summary>
/// 提供应用程序环境信息的接口
/// </summary>
public interface IHostingEnvironment
{
    /// <summary>
    /// 获取Web根目录的物理路径
    /// </summary>
    string WebRootPath { get; }
} 