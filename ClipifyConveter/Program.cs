using ClipifyConveter.Converters;

Console.WriteLine("=== Clipify 视频转换工具 ===");
Console.WriteLine();

// 可以通过命令行参数指定处理目录，如果没有则使用当前目录
var targetDirectory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

Console.WriteLine($"目标目录：{targetDirectory}");
Console.WriteLine();

// 注册所有可用的转换器
var converters = new List<IConverter> {
    new TsToMp4Converter(),
    new MkvToMp4Converter()
};

// 显示可用的转换器
Console.WriteLine("可用的转换器：");
for (int i = 0; i < converters.Count; i++) {
    Console.WriteLine(
        $"{i + 1}. {converters[i].Name} ({converters[i].SourceExtension} -> {converters[i].TargetExtension})");
}

Console.WriteLine();

// 选择转换器
Console.Write("请选择转换器编号（直接回车执行所有转换器）：");
var input = Console.ReadLine();
Console.WriteLine();

List<IConverter> selectedConverters;
if (string.IsNullOrWhiteSpace(input)) {
    // 执行所有转换器
    selectedConverters = converters;
    Console.WriteLine("将执行所有转换器...");
}
else if (int.TryParse(input, out var index) && index > 0 && index <= converters.Count) {
    // 执行选定的转换器
    selectedConverters = new List<IConverter> { converters[index - 1] };
    Console.WriteLine($"将执行：{converters[index - 1].Name}");
}
else {
    Console.WriteLine("无效的选择！");
    Console.Read();
    return;
}

Console.WriteLine();

// 执行转换
foreach (var converter in selectedConverters) {
    Console.WriteLine($"===== 开始执行：{converter.Name} =====");
    
    // 配置转换器（如果支持）
    converter.Configure();
    
    await converter.ConvertAsync(targetDirectory);
    Console.WriteLine();
}

Console.WriteLine("所有转换任务已完成！");
Console.WriteLine("按任意键退出...");
Console.Read();