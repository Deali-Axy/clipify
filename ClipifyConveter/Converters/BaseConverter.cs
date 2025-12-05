using System.Diagnostics;
using ClipifyConveter.Converters.Options;

namespace ClipifyConveter.Converters;

/// <summary>
/// 转换器基类
/// </summary>
public abstract class BaseConverter : IConverter {
    public abstract string Name { get; }
    public abstract string SourceExtension { get; }
    public abstract string TargetExtension { get; }
    public abstract ConverterOptions Options { get; }

    /// <summary>
    /// 生成 FFmpeg 命令参数
    /// </summary>
    /// <param name="sourceFile">源文件路径</param>
    /// <param name="targetFile">目标文件路径</param>
    /// <returns>FFmpeg 命令参数</returns>
    protected abstract string GenerateFfmpegArguments(string sourceFile, string targetFile);

    /// <summary>
    /// 配置转换器选项
    /// </summary>
    public virtual void Configure() {
        // 子类可以重写此方法实现交互式配置
    }

    public virtual async Task<bool> ConvertAsync(string targetDirectory) {
        if (!Directory.Exists(targetDirectory)) {
            Console.WriteLine($"目录不存在：{targetDirectory}");
            return false;
        }

        var sourceFiles = Directory.GetFiles(targetDirectory, $"*{SourceExtension}", SearchOption.TopDirectoryOnly);
        Console.WriteLine($"找到 {sourceFiles.Length} 个 {SourceExtension} 文件");

        if (sourceFiles.Length == 0) {
            Console.WriteLine("没有需要转换的文件。");
            return true;
        }

        var successCount = 0;
        foreach (var sourceFile in sourceFiles) {
            Console.WriteLine($"处理文件：{sourceFile}");

            var targetFile = Path.ChangeExtension(sourceFile, TargetExtension);
            var arguments = GenerateFfmpegArguments(sourceFile, targetFile);

            if (await ExecuteFfmpegAsync(arguments)) {
                Console.WriteLine($"转换完成：{targetFile}");
                successCount++;
            }
            else {
                Console.WriteLine($"转换失败：{sourceFile}");
            }
        }

        Console.WriteLine($"转换完成：成功 {successCount}/{sourceFiles.Length}");
        return successCount == sourceFiles.Length;
    }

    /// <summary>
    /// 执行 FFmpeg 命令
    /// </summary>
    protected virtual async Task<bool> ExecuteFfmpegAsync(string arguments) {
        var psi = new ProcessStartInfo("ffmpeg", arguments) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}