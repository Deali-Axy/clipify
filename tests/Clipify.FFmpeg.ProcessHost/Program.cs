using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Clipify.FFmpeg.ProcessHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: ProcessHost <mode> [args...]");
            return 2;
        }

        return args[0] switch
        {
            "exit" => int.Parse(args[1], CultureInfo.InvariantCulture),
            "progress" => await ProgressAsync(args),
            "stderr-flood" => StderrFlood(args),
            "dual-stream" => await DualStreamAsync(args),
            "child-tree" => await ChildTreeAsync(args),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine($"unknown mode: {mode}");
        return 2;
    }

    private static async Task<int> ProgressAsync(string[] args)
    {
        var frames = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        var delayMs = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 50;
        for (var i = 1; i <= frames; i++)
        {
            var us = i * 500_000L;
            Console.Out.Write($"frame={i}\nout_time_us={us}\nspeed=1.0x\nprogress=continue\n");
            await Console.Out.FlushAsync();
            await Task.Delay(delayMs);
        }

        Console.Out.Write("progress=end\n");
        await Console.Out.FlushAsync();
        return 0;
    }

    private static int StderrFlood(string[] args)
    {
        var bytes = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 200_000;
        var chunk = new string('x', 1024);
        var written = 0;
        while (written < bytes)
        {
            Console.Error.Write(chunk);
            written += chunk.Length;
        }

        Console.Error.Flush();
        return 0;
    }

    private static async Task<int> DualStreamAsync(string[] args)
    {
        var iterations = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 100;
        var stdout = Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                Console.Out.Write(string.Create(CultureInfo.InvariantCulture, $"frame={i}\nout_time_us={i * 1000}\nprogress=continue\n"));
                await Console.Out.FlushAsync();
            }

            Console.Out.Write("progress=end\n");
            await Console.Out.FlushAsync();
        });

        var stderr = Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                Console.Error.Write(string.Create(CultureInfo.InvariantCulture, $"diag line {i}\n"));
                await Console.Error.FlushAsync();
            }
        });

        await Task.WhenAll(stdout, stderr);
        return 0;
    }

    private static async Task<int> ChildTreeAsync(string[] args)
    {
        var holdMs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30_000;
        var self = Environment.ProcessPath
            ?? throw new InvalidOperationException("ProcessPath unavailable.");

        var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = self,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        child.StartInfo.ArgumentList.Add("progress");
        child.StartInfo.ArgumentList.Add("200");
        child.StartInfo.ArgumentList.Add("200");

        if (!child.Start())
        {
            return 3;
        }

        try
        {
            await Task.Delay(holdMs);
            return 0;
        }
        finally
        {
            if (!child.HasExited)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }
            }

            child.Dispose();
        }
    }
}
