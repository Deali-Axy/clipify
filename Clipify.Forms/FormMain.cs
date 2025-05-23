﻿using Clipify.Core.Extensions;
using Clipify.Core.Interfaces;
using Clipify.Forms.Services;
using CoreHostingEnvironment = Clipify.Core.Interfaces.IHostingEnvironment;
using CoreMessageService = Clipify.Core.Interfaces.IMessageService;

namespace Clipify.Forms;

using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public partial class FormMain : Form {
    public FormMain() {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddLogging(c => {
            c.AddDebug();
            // Enable maximum logging for BlazorWebView
            c.AddFilter("Microsoft.AspNetCore.Components.WebView", LogLevel.Trace);
        });
        
        services.AddAntDesign();
        services.AddMediatR(cfg => { cfg.RegisterServicesFromAssemblyContaining<FormMain>(); });
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddSingleton(this);
        
        // 注册Core项目中的服务
        services.AddClipifyCore();
        
        // 注册服务实现
        services.AddScoped<IDialogService, DialogServiceImpl>();
        services.AddTransient<IVideoService, VideoServiceImpl>();
        services.AddScoped<CoreMessageService, MessageServiceImpl>();
        
        // 注册原有服务（兼容性）
        services.AddScoped<DialogService>();
        services.AddTransient<VideoService>();
        // 添加Forms项目自己的IHostingEnvironment接口
        services.AddSingleton<Services.IHostingEnvironment, HostingEnvironmentImpl>();
        
        blazorWebView1.HostPage = "wwwroot\\index.html";
        blazorWebView1.Services = services.BuildServiceProvider();
        blazorWebView1.RootComponents.Add<App>("#app");
    }

    // 处理拖动进入事件，检测是否为文件
    private void blazorWebView1_DragEnter(object sender, DragEventArgs e) {
        Console.WriteLine("drag enter");
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            // 改变鼠标图标，表示可以拖放
            e.Effect = DragDropEffects.Copy;
        }
        else {
            e.Effect = DragDropEffects.None;
        }
    }

    // 处理拖放事件，获取文件路径
    private void blazorWebView1_DragDrop(object sender, DragEventArgs e) {
        Console.WriteLine("drag drop");
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);

            // 这里只处理单个文件，当然你也可以处理多个文件
            if (files?.Length > 0) {
                var filePath = files[0]; // 获取拖放的文件路径
                MessageBox.Show($"文件路径: {filePath}");

                // 在这里你可以将文件路径传递给 Blazor 或其他处理逻辑
            }
        }
    }

    // 处理 DragOver 事件，防止系统默认行为
    private void blazorWebView1_DragOver(object sender, DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effect = DragDropEffects.Copy; // 明确允许拖放文件
        }
        else {
            e.Effect = DragDropEffects.None;
        }
    }
}