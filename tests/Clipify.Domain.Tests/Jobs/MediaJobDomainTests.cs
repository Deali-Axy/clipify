using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.Domain.Tests;

public class MediaJobStateTransitionsTests
{
    [Theory]
    [InlineData(MediaJobState.Queued, MediaJobState.Running)]
    [InlineData(MediaJobState.Queued, MediaJobState.Canceled)]
    [InlineData(MediaJobState.Running, MediaJobState.Succeeded)]
    [InlineData(MediaJobState.Running, MediaJobState.Failed)]
    [InlineData(MediaJobState.Running, MediaJobState.Canceling)]
    [InlineData(MediaJobState.Running, MediaJobState.Interrupted)]
    [InlineData(MediaJobState.Canceling, MediaJobState.Canceled)]
    [InlineData(MediaJobState.Canceling, MediaJobState.Interrupted)]
    [InlineData(MediaJobState.Canceling, MediaJobState.Failed)]
    public void Allows_legal_transitions(MediaJobState from, MediaJobState to)
    {
        Assert.True(MediaJobStateTransitions.CanTransition(from, to));
        MediaJobStateTransitions.EnsureCanTransition(from, to);
    }

    [Theory]
    [InlineData(MediaJobState.Succeeded, MediaJobState.Queued)]
    [InlineData(MediaJobState.Succeeded, MediaJobState.Running)]
    [InlineData(MediaJobState.Failed, MediaJobState.Queued)]
    [InlineData(MediaJobState.Failed, MediaJobState.Running)]
    [InlineData(MediaJobState.Canceled, MediaJobState.Queued)]
    [InlineData(MediaJobState.Canceled, MediaJobState.Running)]
    [InlineData(MediaJobState.Interrupted, MediaJobState.Queued)]
    [InlineData(MediaJobState.Interrupted, MediaJobState.Running)]
    [InlineData(MediaJobState.Queued, MediaJobState.Succeeded)]
    [InlineData(MediaJobState.Queued, MediaJobState.Failed)]
    [InlineData(MediaJobState.Running, MediaJobState.Queued)]
    public void Rejects_illegal_transitions(MediaJobState from, MediaJobState to)
    {
        Assert.False(MediaJobStateTransitions.CanTransition(from, to));
        Assert.Throws<InvalidMediaJobTransitionException>(
            () => MediaJobStateTransitions.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(MediaJobState.Succeeded)]
    [InlineData(MediaJobState.Failed)]
    [InlineData(MediaJobState.Canceled)]
    [InlineData(MediaJobState.Interrupted)]
    public void Terminal_states_are_marked(MediaJobState state)
    {
        Assert.True(MediaJobStateTransitions.IsTerminal(state));
    }

    [Theory]
    [InlineData(MediaJobState.Failed)]
    [InlineData(MediaJobState.Interrupted)]
    [InlineData(MediaJobState.Canceled)]
    public void Retry_allowed_from_failed_interrupted_canceled(MediaJobState state)
    {
        Assert.True(MediaJobStateTransitions.CanRetryFrom(state));
    }

    [Theory]
    [InlineData(MediaJobState.Queued)]
    [InlineData(MediaJobState.Running)]
    [InlineData(MediaJobState.Canceling)]
    [InlineData(MediaJobState.Succeeded)]
    public void Retry_not_allowed_from_active_or_succeeded(MediaJobState state)
    {
        Assert.False(MediaJobStateTransitions.CanRetryFrom(state));
    }
}

public class MediaJobSnapshotTests
{
    [Fact]
    public void CreateQueued_sets_retry_link_and_definition_kind()
    {
        var originalId = MediaJobId.New();
        var retryId = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        var definition = new FakeDelayJobDefinition { Label = "retry-me", Priority = 2 };

        var snapshot = MediaJobSnapshot.CreateQueued(retryId, definition, now, retryOfJobId: originalId);

        Assert.Equal(MediaJobState.Queued, snapshot.State);
        Assert.Equal(FakeDelayJobDefinition.Discriminator, snapshot.DefinitionKind);
        Assert.Equal(originalId, snapshot.RetryOfJobId);
        Assert.Equal(2, snapshot.Priority);
        Assert.False(snapshot.IsTerminal);
    }

    [Fact]
    public void WithState_rejects_terminal_to_queued()
    {
        var snapshot = MediaJobSnapshot.CreateQueued(
            MediaJobId.New(),
            new FakeDelayJobDefinition(),
            DateTimeOffset.UtcNow);

        var failed = snapshot.WithState(MediaJobState.Running, DateTimeOffset.UtcNow)
            .WithState(MediaJobState.Failed, DateTimeOffset.UtcNow, completedAt: DateTimeOffset.UtcNow);

        Assert.Throws<InvalidMediaJobTransitionException>(
            () => failed.WithState(MediaJobState.Queued, DateTimeOffset.UtcNow));
    }
}

public class TimeRangeTests
{
    [Fact]
    public void Accepts_valid_range()
    {
        var range = TimeRange.FromMilliseconds(1000, 5000);
        Assert.Equal(TimeSpan.FromSeconds(1), range.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), range.End);
        Assert.Equal(TimeSpan.FromSeconds(4), range.Duration);
    }

    [Fact]
    public void Rejects_negative_start()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Rejects_end_not_after_start()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));
    }
}

public class MediaJobProgressTests
{
    [Fact]
    public void Rejects_fraction_outside_0_1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaJobProgress.Create("run", DateTimeOffset.UtcNow, fraction: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaJobProgress.Create("run", DateTimeOffset.UtcNow, fraction: -0.1));
    }

    [Fact]
    public void Accepts_valid_progress()
    {
        var progress = MediaJobProgress.Create(
            "encode",
            DateTimeOffset.UtcNow,
            fraction: 0.5,
            message: "halfway");

        Assert.Equal(0.5, progress.Fraction);
        Assert.Equal("encode", progress.Stage);
    }
}

public class MediaJobDefinitionSerializerTests
{
    [Fact]
    public void Roundtrips_fake_delay_definition()
    {
        var definition = new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromMilliseconds(250),
            Fail = true,
            Label = "unit",
            Priority = 1,
        };

        var json = MediaJobDefinitionSerializer.Serialize(definition);
        var restored = MediaJobDefinitionSerializer.Deserialize(FakeDelayJobDefinition.Discriminator, json);

        var fake = Assert.IsType<FakeDelayJobDefinition>(restored);
        Assert.Equal(definition.Delay, fake.Delay);
        Assert.True(fake.Fail);
        Assert.Equal("unit", fake.Label);
        Assert.Equal(1, fake.Priority);
    }

    [Fact]
    public void Rejects_unknown_kind()
    {
        Assert.Throws<ArgumentException>(() =>
            MediaJobDefinitionSerializer.Deserialize("unknown_op", """{"kind":"unknown_op"}"""));
    }
}
