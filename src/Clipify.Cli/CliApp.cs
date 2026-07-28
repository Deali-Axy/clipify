using System.CommandLine;
using Clipify.Application.Abstractions;
using Clipify.Cli.Commands;
using Clipify.Cli.Output;

namespace Clipify.Cli;

public static class CliApp
{
    public static async Task<int> RunAsync(
        string[] args,
        CliRuntimeOptions? runtime = null,
        CancellationToken cancellationToken = default)
    {
        runtime ??= new CliRuntimeOptions();
        var output = runtime.Output ?? Console.Out;
        var error = runtime.Error ?? Console.Error;

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit a single final JSON object on stdout.",
            Recursive = true,
        };
        var jsonlOption = new Option<bool>("--jsonl")
        {
            Description = "Emit JSON Lines events on stdout (progress + result).",
            Recursive = true,
        };
        var dataDirOption = new Option<string?>("--data-dir")
        {
            Description = "Override Clipify application data directory (also CLIPIFY_DATA_DIR).",
            Recursive = true,
        };

        var root = new RootCommand("clipify — cross-platform media CLI")
        {
            jsonOption,
            jsonlOption,
            dataDirOption,
        };

        CliCommandContext? context = null;

        CliCommandContext CreateContext(ParseResult parseResult)
        {
            var useJson = parseResult.GetValue(jsonOption);
            var useJsonl = parseResult.GetValue(jsonlOption);
            if (useJson && useJsonl)
            {
                throw new InvalidOperationException("Mutually exclusive output modes.");
            }

            var mode = useJsonl ? OutputMode.Jsonl : useJson ? OutputMode.Json : OutputMode.Text;
            var dataDir = parseResult.GetValue(dataDirOption) ?? runtime.DataDirectory;

            return new CliCommandContext
            {
                Renderer = CliRendererFactory.Create(mode, output, error),
                OutputMode = mode,
                Runtime = runtime,
                DataDirectory = dataDir,
            };
        }

        async Task<CliCommandContext> GetContextAsync(ParseResult parseResult)
        {
            if (context is not null)
            {
                return context;
            }

            try
            {
                context = CreateContext(parseResult);
                return context;
            }
            catch (InvalidOperationException)
            {
                error.WriteLine("error: --json and --jsonl are mutually exclusive.");
                throw;
            }
        }

        int RenderParseErrors(ParseResult parseResult)
        {
            // Prefer bound option values; fall back to raw args when binding is incomplete.
            var useJson = parseResult.GetValue(jsonOption)
                || args.Any(a => string.Equals(a, "--json", StringComparison.Ordinal));
            var useJsonl = parseResult.GetValue(jsonlOption)
                || args.Any(a => string.Equals(a, "--jsonl", StringComparison.Ordinal));
            if (useJson && useJsonl)
            {
                error.WriteLine("error: --json and --jsonl are mutually exclusive.");
                return CliExitCode.ValidationError;
            }

            var mode = useJsonl ? OutputMode.Jsonl : useJson ? OutputMode.Json : OutputMode.Text;
            var renderer = CliRendererFactory.Create(mode, output, error);
            var commandName = parseResult.CommandResult.Command.Name;
            var message = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            var clipifyError = ClipifyError.Validation(
                string.IsNullOrWhiteSpace(message) ? "Invalid arguments." : message);
            renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: commandName,
                Error: CliDtoMapper.FromError(clipifyError)));
            return CliExitCode.ValidationError;
        }

        var doctor = new Command("doctor", "Diagnose FFmpeg, ffprobe, SQLite, and data directories.");
        doctor.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.DoctorAsync(ctx, linked.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        root.Add(doctor);

        var probeInput = new Argument<string>("input") { Description = "Media file to probe." };
        var probe = new Command("probe", "Probe media metadata with ffprobe.") { probeInput };
        probe.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.ProbeAsync(
                        ctx,
                        parseResult.GetValue(probeInput)!,
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        root.Add(probe);

        Option<string?> CreateConflictOption() => new("--conflict")
        {
            Description = "Output conflict policy: fail (default), overwrite, rename, skip.",
        };

        var trimInput = new Argument<string>("input");
        var trimStart = new Option<string>("--start") { Required = true, Description = "Start time (HH:MM:SS[.fff] or ms)." };
        var trimEnd = new Option<string>("--end") { Required = true, Description = "End time (HH:MM:SS[.fff] or ms)." };
        var trimOutput = new Option<string>("--output") { Required = true, Description = "Output file path." };
        var trimConflict = CreateConflictOption();
        var trim = new Command("trim", "Trim media to a time range.")
        {
            trimInput, trimStart, trimEnd, trimOutput, trimConflict,
        };
        trim.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.TrimAsync(
                        ctx,
                        parseResult.GetValue(trimInput)!,
                        parseResult.GetValue(trimStart)!,
                        parseResult.GetValue(trimEnd)!,
                        parseResult.GetValue(trimOutput)!,
                        parseResult.GetValue(trimConflict),
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        root.Add(trim);

        var extractInput = new Argument<string>("input");
        var extractOutput = new Option<string>("--output") { Required = true };
        var extractFormat = new Option<string?>("--format")
        {
            Description = "Audio format: copy, mp3 (default), aac, wav.",
        };
        var extractConflict = CreateConflictOption();
        var extract = new Command("extract-audio", "Extract audio from media.")
        {
            extractInput, extractOutput, extractFormat, extractConflict,
        };
        extract.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.ExtractAudioAsync(
                        ctx,
                        parseResult.GetValue(extractInput)!,
                        parseResult.GetValue(extractOutput)!,
                        parseResult.GetValue(extractFormat),
                        parseResult.GetValue(extractConflict),
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        root.Add(extract);

        var thumbInput = new Argument<string>("input");
        var thumbOutput = new Option<string>("--output") { Required = true };
        var thumbAt = new Option<string?>("--at") { Description = "Timestamp (default 0)." };
        var thumbFormat = new Option<string?>("--format") { Description = "jpg (default) or png." };
        var thumbConflict = CreateConflictOption();
        var thumbnail = new Command("thumbnail", "Capture a thumbnail frame.")
        {
            thumbInput, thumbOutput, thumbAt, thumbFormat, thumbConflict,
        };
        thumbnail.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.ThumbnailAsync(
                        ctx,
                        parseResult.GetValue(thumbInput)!,
                        parseResult.GetValue(thumbOutput)!,
                        parseResult.GetValue(thumbAt),
                        parseResult.GetValue(thumbFormat),
                        parseResult.GetValue(thumbConflict),
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        root.Add(thumbnail);

        var jobs = new Command("jobs", "Inspect and control media jobs.");

        var listState = new Option<string?>("--state");
        var listTake = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        var jobsList = new Command("list", "List recent jobs.") { listState, listTake };
        jobsList.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.JobsListAsync(
                        ctx,
                        parseResult.GetValue(listState),
                        parseResult.GetValue(listTake),
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        jobs.Add(jobsList);

        var getId = new Argument<string>("job-id");
        var jobsGet = new Command("get", "Get a job snapshot.") { getId };
        jobsGet.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.JobsGetAsync(ctx, parseResult.GetValue(getId)!, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        jobs.Add(jobsGet);

        var waitId = new Argument<string>("job-id");
        var jobsWait = new Command("wait", "Wait until a job reaches a terminal state.") { waitId };
        jobsWait.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.JobsWaitAsync(ctx, parseResult.GetValue(waitId)!, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        jobs.Add(jobsWait);

        var cancelId = new Argument<string>("job-id");
        var jobsCancel = new Command("cancel", "Request cancellation of a job.") { cancelId };
        jobsCancel.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.JobsCancelAsync(ctx, parseResult.GetValue(cancelId)!, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        jobs.Add(jobsCancel);

        var retryId = new Argument<string>("job-id");
        var jobsRetry = new Command("retry", "Retry a failed/interrupted job and wait for completion.") { retryId };
        jobsRetry.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var ctx = await GetContextAsync(parseResult).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                return await CommandHandlers.JobsRetryAsync(ctx, parseResult.GetValue(retryId)!, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return CliExitCode.ValidationError;
            }
        });
        jobs.Add(jobsRetry);

        root.Add(jobs);

        var parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            return RenderParseErrors(parseResult);
        }

        var invocation = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await parseResult.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return CliExitCode.InternalError;
        }
        finally
        {
            if (context is not null)
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
