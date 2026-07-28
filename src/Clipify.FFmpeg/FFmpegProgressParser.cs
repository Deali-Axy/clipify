using System.Globalization;
using System.Text;
using Clipify.Application.Media;

namespace Clipify.FFmpeg;

/// <summary>
/// Incremental parser for ffmpeg <c>-progress pipe:1</c> key=value stdout.
/// Emits a snapshot when <c>progress=continue</c> or <c>progress=end</c> is seen.
/// </summary>
public sealed class FFmpegProgressParser : IFFmpegProgressParser
{
    private readonly StringBuilder _pending = new();
    private readonly Queue<FFmpegProgressSnapshot> _snapshots = new();

    private TimeSpan? _outTime;
    private double? _speed;
    private double? _fps;
    private long? _frame;
    private long? _totalSize;

    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        _pending.Append(chunk);
        DrainCompleteLines();
    }

    public bool TryDequeueSnapshot(out FFmpegProgressSnapshot snapshot)
    {
        if (_snapshots.Count == 0)
        {
            snapshot = default!;
            return false;
        }

        snapshot = _snapshots.Dequeue();
        return true;
    }

    private void DrainCompleteLines()
    {
        while (true)
        {
            var text = _pending.ToString();
            var newline = text.IndexOfAny(['\r', '\n']);
            if (newline < 0)
            {
                return;
            }

            var line = text[..newline];
            var consume = newline + 1;
            if (text[newline] == '\r'
                && consume < text.Length
                && text[consume] == '\n')
            {
                consume++;
            }

            _pending.Remove(0, consume);
            if (line.Length > 0)
            {
                HandleLine(line);
            }
        }
    }

    private void HandleLine(string line)
    {
        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        if (key.Length == 0)
        {
            return;
        }

        switch (key)
        {
            case "out_time_us":
                if (TryParseInt64(value, out var us) && us >= 0)
                {
                    _outTime = TimeSpan.FromTicks(us * 10); // 1 us = 10 ticks
                }

                break;
            case "out_time_ms":
                // Compatibility: some builds emit out_time_ms (milliseconds).
                if (_outTime is null && TryParseInt64(value, out var ms) && ms >= 0)
                {
                    _outTime = TimeSpan.FromMilliseconds(ms);
                }

                break;
            case "speed":
                if (value.EndsWith('x') || value.EndsWith('X'))
                {
                    value = value[..^1];
                }

                if (TryParseDouble(value, out var speed))
                {
                    _speed = speed;
                }

                break;
            case "fps":
                if (TryParseDouble(value, out var fps))
                {
                    _fps = fps;
                }

                break;
            case "frame":
                if (TryParseInt64(value, out var frame))
                {
                    _frame = frame;
                }

                break;
            case "total_size":
                if (TryParseInt64(value, out var size) && size >= 0)
                {
                    _totalSize = size;
                }

                break;
            case "progress":
                if (string.Equals(value, "continue", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "end", StringComparison.OrdinalIgnoreCase))
                {
                    var isEnd = string.Equals(value, "end", StringComparison.OrdinalIgnoreCase);
                    _snapshots.Enqueue(new FFmpegProgressSnapshot(
                        _outTime,
                        _speed,
                        _fps,
                        _frame,
                        _totalSize,
                        isEnd));
                }

                break;
            default:
                // Ignore unknown / malformed keys.
                break;
        }
    }

    private static bool TryParseInt64(string value, out long result) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
