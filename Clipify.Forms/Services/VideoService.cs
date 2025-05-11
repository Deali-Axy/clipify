using Clipify.Core.Interfaces;

namespace Clipify.Forms.Services;

/// <summary>
/// 视频服务（兼容性类）
/// </summary>
public class VideoService : VideoServiceImpl
{
    /// <summary>
    /// 初始化视频服务
    /// </summary>
    /// <param name="environment">环境服务</param>
    public VideoService(IHostingEnvironment environment) : base(environment)
    {
    }
} 