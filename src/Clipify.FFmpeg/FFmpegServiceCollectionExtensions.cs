using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.FFmpeg.Commands;
using Clipify.FFmpeg.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.FFmpeg;

public static class FFmpegServiceCollectionExtensions
{
    public static IServiceCollection AddClipifyFFmpeg(
        this IServiceCollection services,
        Action<ClipifyFFmpegOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<ClipifyFFmpegOptions>(_ => { });
        }

        services.AddSingleton<IFFmpegLocator, FFmpegLocator>();
        services.AddSingleton<IFFmpegVersionProbe, FFmpegVersionProbe>();
        services.AddSingleton<IFFmpegProcessFactory, SystemFFmpegProcessFactory>();
        services.AddSingleton<IFFmpegProcessRunner, FFmpegProcessRunner>();
        services.AddSingleton<IFFprobeClient, FFprobeClient>();
        services.AddSingleton<IOutputCommitter, OutputCommitter>();
        services.AddSingleton<IFFmpegCommandBuilder<TrimMediaJobDefinition>, TrimMediaCommandBuilder>();
        services.AddSingleton<IFFmpegCommandBuilder<ExtractAudioJobDefinition>, ExtractAudioCommandBuilder>();
        services.AddSingleton<IFFmpegCommandBuilder<ThumbnailJobDefinition>, ThumbnailCommandBuilder>();
        services.AddSingleton<IMediaJobHandler<TrimMediaJobDefinition>, TrimMediaJobHandler>();
        services.AddSingleton<IMediaJobHandler<ExtractAudioJobDefinition>, ExtractAudioJobHandler>();
        services.AddSingleton<IMediaJobHandler<ThumbnailJobDefinition>, ThumbnailJobHandler>();
        services.AddSingleton<IProbeMediaUseCase, ProbeMediaUseCase>();

        return services;
    }
}
