using System.Text.Json;
using System.Text.Json.Serialization;
using Clipify.Domain.Media;

namespace Clipify.Domain.Jobs;

/// <summary>
/// Job definition base. Persistence stores a whitelist discriminator plus JSON payload,
/// never arbitrary CLR type names.
/// </summary>
public abstract record MediaJobDefinition
{
    public abstract string Kind { get; }

    public int Priority { get; init; }

    public MediaWorkflowId? WorkflowId { get; init; }

    public MediaJobId? ParentJobId { get; init; }
}

/// <summary>
/// Phase-3 test/no-op definition. Delays then optionally fails or produces a fake artifact.
/// </summary>
public sealed record FakeDelayJobDefinition : MediaJobDefinition
{
    public const string Discriminator = "fake_delay";

    [JsonIgnore]
    public override string Kind => Discriminator;

    /// <summary>How long the fake handler should wait before completing.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>When true, the handler throws after the delay.</summary>
    public bool Fail { get; init; }

    /// <summary>Optional message echoed into progress / artifact metadata.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Trim/cut a media file to a time range. Business fields only — no raw FFmpeg args.
/// </summary>
public sealed record TrimMediaJobDefinition : MediaJobDefinition
{
    public const string Discriminator = "trim_media";

    [JsonIgnore]
    public override string Kind => Discriminator;

    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public required TimeRange Range { get; init; }

    public OutputConflictPolicy ConflictPolicy { get; init; } = OutputConflictPolicy.Fail;
}

/// <summary>
/// Extract audio from a media file into a limited output format.
/// </summary>
public sealed record ExtractAudioJobDefinition : MediaJobDefinition
{
    public const string Discriminator = "extract_audio";

    [JsonIgnore]
    public override string Kind => Discriminator;

    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Mp3;

    public OutputConflictPolicy ConflictPolicy { get; init; } = OutputConflictPolicy.Fail;
}

/// <summary>
/// Capture a single thumbnail frame at a point in time.
/// </summary>
public sealed record ThumbnailJobDefinition : MediaJobDefinition
{
    public const string Discriminator = "thumbnail";

    [JsonIgnore]
    public override string Kind => Discriminator;

    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public TimeSpan At { get; init; }

    public ThumbnailImageFormat Format { get; init; } = ThumbnailImageFormat.Jpg;

    public OutputConflictPolicy ConflictPolicy { get; init; } = OutputConflictPolicy.Fail;
}

public static class MediaJobDefinitionSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new MediaJobIdJsonConverter());
        options.Converters.Add(new MediaWorkflowIdJsonConverter());
        options.Converters.Add(new TimeSpanMillisecondsJsonConverter());
        return options;
    }

    private static readonly HashSet<string> AllowedKinds =
    [
        FakeDelayJobDefinition.Discriminator,
        TrimMediaJobDefinition.Discriminator,
        ExtractAudioJobDefinition.Discriminator,
        ThumbnailJobDefinition.Discriminator,
    ];

    public static bool IsAllowedKind(string kind) => AllowedKinds.Contains(kind);

    public static string Serialize(MediaJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IsAllowedKind(definition.Kind))
        {
            throw new ArgumentException(
                $"Definition kind '{definition.Kind}' is not in the whitelist.",
                nameof(definition));
        }

        return definition switch
        {
            FakeDelayJobDefinition fake => JsonSerializer.Serialize(fake, Options),
            TrimMediaJobDefinition trim => JsonSerializer.Serialize(trim, Options),
            ExtractAudioJobDefinition extract => JsonSerializer.Serialize(extract, Options),
            ThumbnailJobDefinition thumbnail => JsonSerializer.Serialize(thumbnail, Options),
            _ => throw new ArgumentException(
                $"Definition kind '{definition.Kind}' has no serializer.",
                nameof(definition)),
        };
    }

    public static MediaJobDefinition Deserialize(string kind, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (!IsAllowedKind(kind))
        {
            throw new ArgumentException($"Definition kind '{kind}' is not in the whitelist.", nameof(kind));
        }

        MediaJobDefinition? definition = kind switch
        {
            FakeDelayJobDefinition.Discriminator =>
                JsonSerializer.Deserialize<FakeDelayJobDefinition>(json, Options),
            TrimMediaJobDefinition.Discriminator =>
                JsonSerializer.Deserialize<TrimMediaJobDefinition>(json, Options),
            ExtractAudioJobDefinition.Discriminator =>
                JsonSerializer.Deserialize<ExtractAudioJobDefinition>(json, Options),
            ThumbnailJobDefinition.Discriminator =>
                JsonSerializer.Deserialize<ThumbnailJobDefinition>(json, Options),
            _ => null,
        };

        return definition
            ?? throw new InvalidOperationException($"Definition JSON for kind '{kind}' deserialized to null.");
    }
}

file sealed class MediaJobIdJsonConverter : JsonConverter<MediaJobId>
{
    public override MediaJobId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        MediaJobId.Parse(reader.GetString() ?? throw new JsonException("MediaJobId expected a string."));

    public override void Write(Utf8JsonWriter writer, MediaJobId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

file sealed class MediaWorkflowIdJsonConverter : JsonConverter<MediaWorkflowId>
{
    public override MediaWorkflowId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("MediaWorkflowId expected a string."));

    public override void Write(Utf8JsonWriter writer, MediaWorkflowId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

file sealed class TimeSpanMillisecondsJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeSpan.FromMilliseconds(reader.GetInt64());

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)value.TotalMilliseconds);
}
