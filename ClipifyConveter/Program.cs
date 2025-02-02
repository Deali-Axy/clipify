// See https://aka.ms/new-console-template for more information

using System.Diagnostics;

// 可以通过命令行参数指定处理目录，如果没有则使用当前目录
var targetDirectory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

// 获取所有 .ts 文件
var tsFiles = Directory.GetFiles(targetDirectory, "*.ts", SearchOption.TopDirectoryOnly);

Console.WriteLine($"Found {tsFiles.Length} ts files");

foreach (var tsFile in tsFiles) {
    Console.WriteLine($"处理文件：{tsFile}");

    // 将.ts扩展名改为.mp4
    var mp4File = Path.ChangeExtension(tsFile, ".mp4");

    // -y 表示自动覆盖同名文件，如果不想自动覆盖可以去掉 -y
    // -c copy 只做封装转换，不做重新编码
    var arguments = $"-i \"{tsFile}\" -c copy -y \"{mp4File}\"";

    // 这里假设本地/环境中已经装好 ffmpeg，并且它在 PATH 中可以直接被调用
    var psi = new ProcessStartInfo("ffmpeg", arguments) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = new Process { StartInfo = psi };
    // 如果想查看输出，可以添加事件处理
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

    process.WaitForExit();
    Console.WriteLine($"转换完成：{mp4File}");
}

Console.WriteLine("所有文件处理完毕。");

Console.Read();