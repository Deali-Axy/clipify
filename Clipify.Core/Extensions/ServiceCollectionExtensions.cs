using Clipify.Core.Interfaces;
using Clipify.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Core.Extensions;

/// <summary>
/// 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加Clipify核心服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddClipifyCore(this IServiceCollection services)
    {
        // 注册核心服务
        services.AddSingleton<IFFmpegCommandGenerator, FFmpegCommandGenerator>();
        services.AddTransient<IVideoProcessor, VideoProcessor>();
        
        return services;
    }
} 