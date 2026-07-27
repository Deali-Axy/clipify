using System.Text.Json;
using System.Text.Json.Serialization;

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
/// Real media definitions arrive in later phases.
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
