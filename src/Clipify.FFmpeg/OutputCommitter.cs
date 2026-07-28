using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;

namespace Clipify.FFmpeg;

public sealed class OutputCommitter : IOutputCommitter
{
    public OutputPreparation Prepare(OutputCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FinalOutputPath);

        var finalPath = Path.GetFullPath(request.FinalOutputPath);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new ClipifyException(ClipifyErrorCode.Validation, "Output path has no directory.");

        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileNameWithoutExtension(finalPath);
        var extension = Path.GetExtension(finalPath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".bin";
        }

        var tempName = $".{fileName}.clipify-{request.JobId.Value}.partial{extension}";
        var temporaryPath = Path.Combine(directory, tempName);

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        var exists = File.Exists(finalPath);
        switch (request.ConflictPolicy)
        {
            case OutputConflictPolicy.Fail when exists:
                throw new ClipifyException(
                    ClipifyErrorCode.OutputConflict,
                    $"Output already exists: {finalPath}");

            case OutputConflictPolicy.Skip when exists:
                return new OutputPreparation
                {
                    JobId = request.JobId,
                    FinalOutputPath = finalPath,
                    TemporaryOutputPath = temporaryPath,
                    ConflictPolicy = request.ConflictPolicy,
                    SkipExecution = true,
                    ExistingOutputPath = finalPath,
                };

            case OutputConflictPolicy.Rename when exists:
                finalPath = AllocateRenamePath(finalPath);
                break;

            case OutputConflictPolicy.Overwrite:
            case OutputConflictPolicy.Fail:
            case OutputConflictPolicy.Skip:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.ConflictPolicy, "Unknown conflict policy.");
        }

        return new OutputPreparation
        {
            JobId = request.JobId,
            FinalOutputPath = finalPath,
            TemporaryOutputPath = temporaryPath,
            ConflictPolicy = request.ConflictPolicy,
            SkipExecution = false,
        };
    }

    public ValueTask<OutputCommitResult> CommitAsync(
        OutputPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (preparation.SkipExecution)
        {
            long? existingSize = null;
            if (preparation.ExistingOutputPath is not null && File.Exists(preparation.ExistingOutputPath))
            {
                existingSize = new FileInfo(preparation.ExistingOutputPath).Length;
            }

            return ValueTask.FromResult(new OutputCommitResult(
                preparation.ExistingOutputPath ?? preparation.FinalOutputPath,
                Skipped: true,
                existingSize));
        }

        if (!File.Exists(preparation.TemporaryOutputPath))
        {
            throw new ClipifyException(
                ClipifyErrorCode.FfmpegFailed,
                "Temporary output missing after FFmpeg run.");
        }

        var tempInfo = new FileInfo(preparation.TemporaryOutputPath);
        if (tempInfo.Length <= 0)
        {
            Cleanup(preparation);
            throw new ClipifyException(
                ClipifyErrorCode.FfmpegFailed,
                "Temporary output is empty.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (preparation.ConflictPolicy == OutputConflictPolicy.Overwrite
            && File.Exists(preparation.FinalOutputPath))
        {
            File.Delete(preparation.FinalOutputPath);
        }

        if (File.Exists(preparation.FinalOutputPath)
            && preparation.ConflictPolicy != OutputConflictPolicy.Overwrite)
        {
            // Race: another writer created the file after Prepare.
            if (preparation.ConflictPolicy == OutputConflictPolicy.Fail)
            {
                Cleanup(preparation);
                throw new ClipifyException(
                    ClipifyErrorCode.OutputConflict,
                    $"Output already exists: {preparation.FinalOutputPath}");
            }

            if (preparation.ConflictPolicy == OutputConflictPolicy.Rename)
            {
                var renamed = AllocateRenamePath(preparation.FinalOutputPath);
                File.Move(preparation.TemporaryOutputPath, renamed);
                return ValueTask.FromResult(new OutputCommitResult(renamed, Skipped: false, tempInfo.Length));
            }

            if (preparation.ConflictPolicy == OutputConflictPolicy.Skip)
            {
                Cleanup(preparation);
                var size = new FileInfo(preparation.FinalOutputPath).Length;
                return ValueTask.FromResult(new OutputCommitResult(preparation.FinalOutputPath, Skipped: true, size));
            }
        }

        File.Move(preparation.TemporaryOutputPath, preparation.FinalOutputPath);
        return ValueTask.FromResult(new OutputCommitResult(preparation.FinalOutputPath, Skipped: false, tempInfo.Length));
    }

    public void Cleanup(OutputPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        try
        {
            if (File.Exists(preparation.TemporaryOutputPath))
            {
                File.Delete(preparation.TemporaryOutputPath);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string AllocateRenamePath(string finalPath)
    {
        var directory = Path.GetDirectoryName(finalPath)!;
        var fileName = Path.GetFileNameWithoutExtension(finalPath);
        var extension = Path.GetExtension(finalPath);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(
                directory,
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{fileName} ({i}){extension}"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new ClipifyException(
            ClipifyErrorCode.OutputConflict,
            "Could not allocate a unique rename path.");
    }
}
