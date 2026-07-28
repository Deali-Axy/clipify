using System.Text.Json;
using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Cli.Parsing;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Cli.Tests;

public class ExitCodeMapperTests
{
    [Theory]
    [InlineData(ClipifyErrorCode.Validation, CliExitCode.ValidationError)]
    [InlineData(ClipifyErrorCode.NotFound, CliExitCode.FileError)]
    [InlineData(ClipifyErrorCode.FfmpegUnavailable, CliExitCode.ToolUnavailable)]
    [InlineData(ClipifyErrorCode.FfmpegFailed, CliExitCode.JobFailed)]
    [InlineData(ClipifyErrorCode.Canceled, CliExitCode.Canceled)]
    [InlineData(ClipifyErrorCode.Interrupted, CliExitCode.Interrupted)]
    [InlineData(ClipifyErrorCode.Internal, CliExitCode.InternalError)]
    public void Maps_error_codes_without_message_parsing(ClipifyErrorCode code, int expected)
    {
        Assert.Equal(expected, ExitCodeMapper.FromError(new ClipifyError(code, "ignored text")));
    }

    [Theory]
    [InlineData(MediaJobState.Succeeded, CliExitCode.Success)]
    [InlineData(MediaJobState.Failed, CliExitCode.JobFailed)]
    [InlineData(MediaJobState.Canceled, CliExitCode.Canceled)]
    [InlineData(MediaJobState.Interrupted, CliExitCode.Interrupted)]
    public void Maps_job_states(MediaJobState state, int expected)
    {
        Assert.Equal(expected, ExitCodeMapper.FromJobState(state));
    }
}

public class CliParserTests
{
    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("00:00:01.500", 1500)]
    [InlineData("0:01:00", 60000)]
    public void Parses_time_values(string text, long expectedMs)
    {
        Assert.True(CliParsers.TryParseTime(text, out var value, out var error));
        Assert.Null(error);
        Assert.Equal(expectedMs, (long)value.TotalMilliseconds);
    }

    [Fact]
    public void Rejects_invalid_time()
    {
        Assert.False(CliParsers.TryParseTime("not-a-time", out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    [Fact]
    public void Parses_job_id_guid()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.True(CliParsers.TryParseJobId(id, out var jobId, out _));
        Assert.Equal(id, jobId.Value);
    }

    [Fact]
    public void Rejects_non_guid_job_id()
    {
        Assert.False(CliParsers.TryParseJobId("not-a-guid", out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    [Theory]
    [InlineData("fail", OutputConflictPolicy.Fail)]
    [InlineData("overwrite", OutputConflictPolicy.Overwrite)]
    [InlineData("rename", OutputConflictPolicy.Rename)]
    [InlineData("skip", OutputConflictPolicy.Skip)]
    public void Parses_conflict_policy_whitelist(string text, OutputConflictPolicy expected)
    {
        Assert.True(CliParsers.TryParseConflictPolicy(text, out var policy, out _));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public void Rejects_unknown_conflict_policy()
    {
        Assert.False(CliParsers.TryParseConflictPolicy("obliterate", out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }
}

public class HelpAndValidationTests
{
    [Fact]
    public async Task Help_returns_success_and_lists_commands()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["--help"], data, writers);
        Assert.Equal(0, code);
        Assert.Contains("doctor", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trim", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("detach", writers.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trim_missing_required_options_is_validation_error()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["trim", "in.mp4"], data, writers);
        Assert.Equal(CliExitCode.ValidationError, code);
        Assert.True(
            writers.StdErr.Contains("start", StringComparison.OrdinalIgnoreCase)
            || writers.StdErr.Contains("Required", StringComparison.OrdinalIgnoreCase)
            || writers.StdOut.Contains("Validation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Json_missing_required_options_returns_structured_validation_error()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["trim", "in.mp4", "--json"], data, writers);
        Assert.Equal(CliExitCode.ValidationError, code);

        var stdout = writers.StdOut.Trim();
        Assert.StartsWith("{", stdout, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Validation", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Usage:", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("用法:", writers.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_time_maps_to_exit_code_2()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(
            ["trim", "in.mp4", "--start", "bad", "--end", "1000", "--output", "out.mp4"],
            data,
            writers);
        Assert.Equal(CliExitCode.ValidationError, code);
    }

    [Fact]
    public async Task Invalid_job_id_maps_to_exit_code_2()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["jobs", "get", "not-a-guid"], data, writers);
        Assert.Equal(CliExitCode.ValidationError, code);
    }

    [Fact]
    public async Task Jobs_wait_missing_job_returns_not_found_json()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var missingId = Guid.NewGuid().ToString("N");
        var code = await CliTestHost.RunAsync(["jobs", "wait", missingId, "--json"], data, writers);
        Assert.Equal(CliExitCode.FileError, code);

        var stdout = writers.StdOut.Trim();
        Assert.False(string.IsNullOrWhiteSpace(stdout));
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("NotFound", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}

public class OutputModeTests
{
    [Fact]
    public async Task Json_stdout_is_single_object_without_progress_pollution()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["doctor", "--json"], data, writers);
        Assert.True(code is CliExitCode.Success or CliExitCode.ToolUnavailable or CliExitCode.InternalError);

        var stdout = writers.StdOut.Trim();
        Assert.False(string.IsNullOrWhiteSpace(stdout));
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("ok", out _));
        Assert.True(doc.RootElement.TryGetProperty("doctor", out _));
        Assert.DoesNotContain("MediaJobWorker", writers.StdOut, StringComparison.Ordinal);
        Assert.StartsWith("{", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jsonl_lines_are_valid_json_objects()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["doctor", "--jsonl"], data, writers);
        Assert.True(code is CliExitCode.Success or CliExitCode.ToolUnavailable or CliExitCode.InternalError);

        var lines = writers.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("event", out _));
        }
    }

    [Fact]
    public async Task Json_and_jsonl_are_mutually_exclusive()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["doctor", "--json", "--jsonl"], data, writers);
        Assert.Equal(CliExitCode.ValidationError, code);
    }
}

public class DoctorCommandTests
{
    [Fact]
    public async Task Doctor_reports_paths_and_platform()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["doctor", "--json", "--data-dir", data.Path], data, writers);
        Assert.True(code is CliExitCode.Success or CliExitCode.ToolUnavailable);

        using var doc = JsonDocument.Parse(writers.StdOut.Trim());
        var doctor = doc.RootElement.GetProperty("doctor");
        Assert.Equal(data.Path, doctor.GetProperty("paths").GetProperty("root").GetString());
        Assert.True(doctor.GetProperty("platform").TryGetProperty("os", out _));
        Assert.Contains(
            doctor.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()),
            name => name is "ffmpeg" or "ffprobe" or "sqlite" or "data_directory");
    }

    [Fact]
    public async Task Doctor_reports_sqlite_failure_when_database_is_corrupt()
    {
        using var data = new TempDataDirectory();
        Directory.CreateDirectory(data.Path);
        await File.WriteAllTextAsync(Path.Combine(data.Path, "jobs.db"), "this is not a sqlite database");

        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["doctor", "--json", "--data-dir", data.Path], data, writers);
        Assert.NotEqual(CliExitCode.Success, code);

        var stdout = writers.StdOut.Trim();
        Assert.False(string.IsNullOrWhiteSpace(stdout), $"stdout empty; stderr={writers.StdErr}");
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var sqlite = doc.RootElement.GetProperty("doctor").GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "sqlite");
        Assert.False(sqlite.GetProperty("ok").GetBoolean());
    }
}

public class ProbeAndMediaCommandTests
{
    [Fact]
    public async Task Probe_missing_file_maps_to_file_error()
    {
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var missing = Path.Combine(data.Path, "missing-media.mp4");
        var code = await CliTestHost.RunAsync(["probe", missing, "--json"], data, writers);
        Assert.Equal(CliExitCode.FileError, code);
        using var doc = JsonDocument.Parse(writers.StdOut.Trim());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Trim_extract_thumbnail_succeed_and_write_artifacts()
    {
        await CliTestHost.EnsureMediaFixtureAsync();
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();

        var trimOut = Path.Combine(data.Path, "trim.mp4");
        var trimCode = await CliTestHost.RunAsync(
            [
                "trim", CliTestHost.SourceVideo,
                "--start", "0", "--end", "1000",
                "--output", trimOut, "--json",
            ],
            data,
            writers);
        Assert.Equal(CliExitCode.Success, trimCode);
        Assert.True(File.Exists(trimOut));
        Assert.Empty(Directory.GetFiles(data.Path, "*.partial*"));

        using var writers2 = new CapturingWriters();
        var audioOut = Path.Combine(data.Path, "audio.mp3");
        var audioCode = await CliTestHost.RunAsync(
            [
                "extract-audio", CliTestHost.SourceVideo,
                "--output", audioOut, "--format", "mp3", "--json",
            ],
            data,
            writers2);
        Assert.Equal(CliExitCode.Success, audioCode);
        Assert.True(File.Exists(audioOut));

        using var writers3 = new CapturingWriters();
        var thumbOut = Path.Combine(data.Path, "thumb.jpg");
        var thumbCode = await CliTestHost.RunAsync(
            [
                "thumbnail", CliTestHost.SourceVideo,
                "--output", thumbOut, "--at", "500", "--json",
            ],
            data,
            writers3);
        Assert.Equal(CliExitCode.Success, thumbCode);
        Assert.True(File.Exists(thumbOut));
    }

    [Fact]
    public async Task Chinese_path_trim_succeeds()
    {
        await CliTestHost.EnsureMediaFixtureAsync();
        using var data = new TempDataDirectory();
        var chineseDir = Path.Combine(data.Path, "中文目录");
        Directory.CreateDirectory(chineseDir);
        var input = Path.Combine(chineseDir, "源视频.mp4");
        File.Copy(CliTestHost.SourceVideo, input);
        var output = Path.Combine(chineseDir, "裁剪.mp4");

        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(
            ["trim", input, "--start", "0", "--end", "800", "--output", output, "--json"],
            data,
            writers);
        Assert.Equal(CliExitCode.Success, code);
        Assert.True(File.Exists(output));
    }
}

public class JobsCommandTests
{
    [Fact]
    public async Task Jobs_list_get_wait_across_same_sqlite()
    {
        await CliTestHost.EnsureMediaFixtureAsync();
        using var data = new TempDataDirectory();
        var output = Path.Combine(data.Path, "job-out.mp4");

        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(
            ["trim", CliTestHost.SourceVideo, "--start", "0", "--end", "700", "--output", output, "--json"],
            data,
            writers);
        Assert.Equal(CliExitCode.Success, code);

        using var doc = JsonDocument.Parse(writers.StdOut.Trim());
        var jobId = doc.RootElement.GetProperty("job").GetProperty("job_id").GetString()!;

        using var listWriters = new CapturingWriters();
        var listCode = await CliTestHost.RunAsync(["jobs", "list", "--json"], data, listWriters);
        Assert.Equal(CliExitCode.Success, listCode);
        using var listDoc = JsonDocument.Parse(listWriters.StdOut.Trim());
        Assert.Contains(
            listDoc.RootElement.GetProperty("jobs").EnumerateArray(),
            j => j.GetProperty("job_id").GetString() == jobId);

        using var getWriters = new CapturingWriters();
        var getCode = await CliTestHost.RunAsync(["jobs", "get", jobId, "--json"], data, getWriters);
        Assert.Equal(CliExitCode.Success, getCode);

        using var waitWriters = new CapturingWriters();
        var waitCode = await CliTestHost.RunAsync(["jobs", "wait", jobId, "--json"], data, waitWriters);
        Assert.Equal(CliExitCode.Success, waitCode);
    }

    [Fact]
    public async Task Two_cli_waiters_claim_preseeded_job_only_once()
    {
        using var data = new TempDataDirectory();

        // Seed a Queued job without starting any Worker so two CLI processes race to Claim.
        var seedOptions = new ClipifyHostOptions
        {
            DataDirectory = data.Path,
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
        };
        using (var seedHost = ClipifyHostFactory.BuildForDiagnostics(seedOptions))
        {
            await seedHost.Services.MigrateClipifyDatabaseAsync();
            var jobs = seedHost.Services.GetRequiredService<IMediaJobService>();
            var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.FromMilliseconds(600),
                Label = "cli-claim-race",
            });

            using var writersA = new CapturingWriters();
            using var writersB = new CapturingWriters();
            var args = new[] { "jobs", "wait", jobId.Value, "--json", "--data-dir", data.Path };
            var taskA = CliTestHost.RunAsync(args, data, writersA);
            var taskB = CliTestHost.RunAsync(args, data, writersB);
            var codes = await Task.WhenAll(taskA, taskB);

            Assert.Equal(CliExitCode.Success, codes[0]);
            Assert.Equal(CliExitCode.Success, codes[1]);

            using var docA = JsonDocument.Parse(writersA.StdOut.Trim());
            using var docB = JsonDocument.Parse(writersB.StdOut.Trim());
            Assert.Equal("succeeded", docA.RootElement.GetProperty("job").GetProperty("state").GetString());
            Assert.Equal("succeeded", docB.RootElement.GetProperty("job").GetProperty("state").GetString());

            var snapshot = await jobs.GetAsync(jobId);
            Assert.NotNull(snapshot);
            Assert.Equal(MediaJobState.Succeeded, snapshot!.State);
            Assert.Single(snapshot.Artifacts);
            Assert.Equal("fake_output", snapshot.Artifacts[0].Kind);
        }
    }
}

public class ConverterAuditTests
{
    [Fact]
    public async Task Cli_help_does_not_expose_convert_merge_batch_or_detach()
    {
        // Compile-time / help-level guard: Phase 5 must not migrate Converter batch/interactive convert.
        using var data = new TempDataDirectory();
        using var writers = new CapturingWriters();
        var code = await CliTestHost.RunAsync(["--help"], data, writers);
        Assert.Equal(0, code);
        Assert.DoesNotContain("convert", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("merge", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("batch", writers.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--detach", writers.StdOut, StringComparison.OrdinalIgnoreCase);
    }
}
