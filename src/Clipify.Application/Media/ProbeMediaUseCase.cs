using Clipify.Application.Abstractions;
using Clipify.Domain.Media;

namespace Clipify.Application.Media;

/// <summary>
/// Caller-facing media probe use case. Does not enqueue a job.
/// </summary>
public interface IProbeMediaUseCase
{
    Task<Result<MediaInfo>> ExecuteAsync(string inputPath, CancellationToken cancellationToken = default);
}

public sealed class ProbeMediaUseCase : IProbeMediaUseCase
{
    private readonly IFFprobeClient _ffprobe;

    public ProbeMediaUseCase(IFFprobeClient ffprobe)
    {
        _ffprobe = ffprobe;
    }

    public async Task<Result<MediaInfo>> ExecuteAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Result<MediaInfo>.Failure(ClipifyError.Validation("Input path is required."));
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            return Result<MediaInfo>.Failure(ClipifyError.NotFound($"Input file not found: {fullPath}"));
        }

        try
        {
            var info = await _ffprobe.ProbeAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return Result<MediaInfo>.Success(info);
        }
        catch (ClipifyException ex)
        {
            return Result<MediaInfo>.Failure(ex.ToError());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<MediaInfo>.Failure(
                ClipifyError.MediaProbeFailed("Failed to probe media.", ex.Message));
        }
    }
}
