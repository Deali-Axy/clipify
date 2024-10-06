namespace Clipify.Forms.Services;

public interface IHostingEnvironment {
    string WebRootPath { get; }
}

public class HostingEnvironment : IHostingEnvironment {
    public HostingEnvironment() {
        // 定义 wwwroot 的路径，可以根据实际情况修改
#if DEBUG
        WebRootPath =
            Path.Combine(
                Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName,
                "wwwroot"
            );
#else
        WebRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
#endif
    }

    public string WebRootPath { get; }
}