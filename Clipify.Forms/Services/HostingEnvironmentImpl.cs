using Clipify.Core.Interfaces;

namespace Clipify.Forms.Services;

/// <summary>
/// 宿主环境实现
/// </summary>
public class HostingEnvironmentImpl : IHostingEnvironment
{
    /// <summary>
    /// 初始化宿主环境
    /// </summary>
    public HostingEnvironmentImpl()
    {
        // 定义 wwwroot 的路径，可以根据实际情况修改
#if DEBUG
        WebRootPath =
            Path.Combine(
                Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName,
                "wwwroot"
            );
#else
        WebRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
#endif
    }

    /// <summary>
    /// 获取Web根目录的物理路径
    /// </summary>
    public string WebRootPath { get; }
} 