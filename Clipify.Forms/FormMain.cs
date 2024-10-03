namespace Clipify.Forms;

using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

public partial class FormMain : Form
{
    public FormMain()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssemblyContaining<FormMain>();
        });
        services.AddWindowsFormsBlazorWebView();

#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        blazorWebView1.HostPage = "wwwroot\\index.html";
        blazorWebView1.Services = services.BuildServiceProvider();
        blazorWebView1.RootComponents.Add<App>("#app");
        // blazorWebView1.RootComponents.Add<Counter>("#app");
    }

    private void openFileDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
    {

    }

    private void folderBrowserDialog_HelpRequest(object sender, EventArgs e)
    {

    }
}