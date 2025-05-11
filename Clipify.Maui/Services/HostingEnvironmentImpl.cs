using Clipify.Core.Interfaces;

namespace Clipify.Maui.Services;

/// <summary>
/// MAUI环境服务实现
/// </summary>
public class HostingEnvironmentImpl : IHostingEnvironment
{
    /// <summary>
    /// 初始化环境服务
    /// </summary>
    public HostingEnvironmentImpl()
    {
        // 在MAUI中，我们使用FileSystem.AppDataDirectory作为根目录
        WebRootPath = Path.Combine(FileSystem.AppDataDirectory, "wwwroot");
        
        // 确保目录存在
        if (!Directory.Exists(WebRootPath))
        {
            Directory.CreateDirectory(WebRootPath);
        }
    }

    /// <summary>
    /// 获取Web根目录的物理路径
    /// </summary>
    public string WebRootPath { get; }
} 