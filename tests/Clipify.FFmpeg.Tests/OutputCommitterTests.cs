using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.FFmpeg;

namespace Clipify.FFmpeg.Tests;

public class OutputCommitterTests
{
    private readonly OutputCommitter _committer = new();

    [Fact]
    public async Task Fail_policy_rejects_existing_output()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        await File.WriteAllTextAsync(finalPath, "existing");

        var ex = Assert.Throws<ClipifyException>(() =>
            _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Fail)));
        Assert.Equal(ClipifyErrorCode.OutputConflict, ex.Code);
    }

    [Fact]
    public async Task Overwrite_replaces_existing_file()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        await File.WriteAllTextAsync(finalPath, "old");

        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Overwrite));
        await File.WriteAllTextAsync(prep.TemporaryOutputPath, "new-content");
        var result = await _committer.CommitAsync(prep);

        Assert.False(result.Skipped);
        Assert.Equal("new-content", await File.ReadAllTextAsync(finalPath));
        Assert.False(File.Exists(prep.TemporaryOutputPath));
    }

    [Fact]
    public async Task Rename_allocates_unique_path()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        await File.WriteAllTextAsync(finalPath, "existing");

        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Rename));
        Assert.NotEqual(finalPath, prep.FinalOutputPath);
        await File.WriteAllTextAsync(prep.TemporaryOutputPath, "renamed");
        var result = await _committer.CommitAsync(prep);

        Assert.True(File.Exists(finalPath));
        Assert.Equal("renamed", await File.ReadAllTextAsync(result.CommittedPath));
        Assert.False(File.Exists(prep.TemporaryOutputPath));
    }

    [Fact]
    public async Task Skip_does_not_run_and_keeps_existing()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        await File.WriteAllTextAsync(finalPath, "keep");

        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Skip));
        Assert.True(prep.SkipExecution);
        var result = await _committer.CommitAsync(prep);

        Assert.True(result.Skipped);
        Assert.Equal("keep", await File.ReadAllTextAsync(finalPath));
        Assert.False(File.Exists(prep.TemporaryOutputPath));
    }

    [Fact]
    public async Task Temporary_file_is_same_directory_and_cleaned_on_failure()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "clip.mp4");
        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Fail));

        Assert.Equal(dir.Path, Path.GetDirectoryName(prep.TemporaryOutputPath));
        Assert.Contains(".partial", prep.TemporaryOutputPath, StringComparison.Ordinal);
        await File.WriteAllTextAsync(prep.TemporaryOutputPath, "");

        await Assert.ThrowsAsync<ClipifyException>(() => _committer.CommitAsync(prep).AsTask());
        Assert.False(File.Exists(prep.TemporaryOutputPath));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task Overwrite_replace_failure_keeps_original_when_destination_locked()
    {
        // FileShare.None is mandatory on Windows; POSIX locks are advisory and do not block
        // File.Replace/rename, so this recovery path cannot be exercised there.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        var tempPath = Path.Combine(dir.Path, ".out.clipify-job.partial.mp4");
        await File.WriteAllTextAsync(finalPath, "original");
        await File.WriteAllTextAsync(tempPath, "replacement");

        await using (var locked = new FileStream(finalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _ = locked;
            Assert.ThrowsAny<IOException>(() =>
                OutputCommitter.ReplaceAtomically(tempPath, finalPath, MediaJobId.New()));
        }

        Assert.Equal("original", await File.ReadAllTextAsync(finalPath));
    }

    [Fact]
    public async Task Cleanup_after_commit_does_not_delete_final_output()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "out.mp4");
        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Fail));
        await File.WriteAllTextAsync(prep.TemporaryOutputPath, "final-bytes");
        var result = await _committer.CommitAsync(prep);

        Assert.True(prep.Committed);
        _committer.Cleanup(prep);

        Assert.True(File.Exists(result.CommittedPath));
        Assert.Equal("final-bytes", await File.ReadAllTextAsync(result.CommittedPath));
    }

    [Fact]
    public async Task Cleanup_removes_temp_after_cancel()
    {
        using var dir = new TempDir();
        var finalPath = Path.Combine(dir.Path, "clip.mp4");
        var prep = _committer.Prepare(new OutputCommitRequest(MediaJobId.New(), finalPath, OutputConflictPolicy.Fail));
        await File.WriteAllTextAsync(prep.TemporaryOutputPath, "partial");

        _committer.Cleanup(prep);

        Assert.False(File.Exists(prep.TemporaryOutputPath));
        Assert.False(File.Exists(finalPath));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("clipify-out-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
