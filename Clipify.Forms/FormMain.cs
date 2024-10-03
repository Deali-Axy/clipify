namespace Clipify.Forms;

using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

public partial class FormMain : Form {
    public FormMain() {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();

#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        blazorWebView1.HostPage = "wwwroot\\index.html";
        blazorWebView1.Services = services.BuildServiceProvider();
        // blazorWebView1.RootComponents.Add<App>("#app");
        blazorWebView1.RootComponents.Add<Counter>("#app");
    }
}