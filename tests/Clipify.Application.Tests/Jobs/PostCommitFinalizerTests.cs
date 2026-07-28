using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Application.Tests;

public class PostCommitFinalizerTests
{
    [Fact]
    public async Task Finalize_retries_completed_progress_without_duplicating_artifact()
    {
        var store = new InMemoryMediaJobStore();
        var jobId = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        await store.InsertAsync(
            MediaJobSnapshot.CreateQueued(
                jobId,
                new FakeDelayJobDefinition { Delay = TimeSpan.Zero },
                now));
        await store.TransitionAsync(jobId, MediaJobState.Queued, MediaJobState.Running, now, startedAt: now);

        var failCompletedLeft = 1;
        var addCount = 0;
        var context = new MediaJobExecutionContext
        {
            Snapshot = (await store.GetAsync(jobId))!,
            ReportProgressAsync = async (progress, ct) =>
            {
                if (string.Equals(progress.Stage, "completed", StringComparison.Ordinal)
                    && failCompletedLeft > 0)
                {
                    failCompletedLeft--;
                    throw new InvalidOperationException("first completed progress failed");
                }

                await store.UpdateProgressAsync(jobId, progress, ct);
            },
            AddArtifactAsync = async (artifact, ct) =>
            {
                addCount++;
                await store.AddArtifactAsync(artifact, ct);
            },
        };
        context.MarkOutputCommitted();

        var artifact = MediaArtifact.Create(
            jobId,
            kind: "fake_output",
            path: "fake://stable",
            createdAt: now,
            artifactId: "stable-id-1");

        await PostCommitFinalizer.FinalizeAsync(context, artifact, totalDuration: null, TimeProvider.System);

        Assert.Equal(1, addCount);
        Assert.Single(await store.ListArtifactsAsync(jobId));
        Assert.Null(context.PostCommitWarning);
        Assert.Equal(0, failCompletedLeft);
    }

    [Fact]
    public async Task Finalize_records_warning_when_completed_progress_permanently_fails()
    {
        var store = new InMemoryMediaJobStore();
        var jobId = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        await store.InsertAsync(
            MediaJobSnapshot.CreateQueued(
                jobId,
                new FakeDelayJobDefinition { Delay = TimeSpan.Zero },
                now));
        await store.TransitionAsync(jobId, MediaJobState.Queued, MediaJobState.Running, now, startedAt: now);

        var context = new MediaJobExecutionContext
        {
            Snapshot = (await store.GetAsync(jobId))!,
            ReportProgressAsync = (progress, _) =>
            {
                if (string.Equals(progress.Stage, "completed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("completed progress always fails");
                }

                return store.UpdateProgressAsync(jobId, progress);
            },
            AddArtifactAsync = (artifact, ct) => store.AddArtifactAsync(artifact, ct),
        };
        context.MarkOutputCommitted();

        var artifact = MediaArtifact.Create(
            jobId,
            kind: "fake_output",
            path: "fake://warn",
            createdAt: now,
            artifactId: "stable-id-2");

        await PostCommitFinalizer.FinalizeAsync(context, artifact, totalDuration: null, TimeProvider.System, maxAttempts: 2);

        Assert.Single(await store.ListArtifactsAsync(jobId));
        Assert.Contains("final progress", context.PostCommitWarning ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalize_records_warning_and_throws_when_artifact_permanently_fails()
    {
        var store = new InMemoryMediaJobStore();
        var jobId = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        await store.InsertAsync(
            MediaJobSnapshot.CreateQueued(
                jobId,
                new FakeDelayJobDefinition { Delay = TimeSpan.Zero },
                now));
        await store.TransitionAsync(jobId, MediaJobState.Queued, MediaJobState.Running, now, startedAt: now);

        var context = new MediaJobExecutionContext
        {
            Snapshot = (await store.GetAsync(jobId))!,
            ReportProgressAsync = (progress, ct) => store.UpdateProgressAsync(jobId, progress, ct),
            AddArtifactAsync = (_, _) => throw new InvalidOperationException("artifact always fails"),
        };
        context.MarkOutputCommitted();

        var artifact = MediaArtifact.Create(
            jobId,
            kind: "fake_output",
            path: "fake://fail",
            createdAt: now,
            artifactId: "stable-id-3");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostCommitFinalizer.FinalizeAsync(context, artifact, totalDuration: null, TimeProvider.System, maxAttempts: 2));

        Assert.Empty(await store.ListArtifactsAsync(jobId));
        Assert.Contains("artifact", context.PostCommitWarning ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
