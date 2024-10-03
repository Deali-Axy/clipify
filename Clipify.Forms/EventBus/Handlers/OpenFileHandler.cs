using Clipify.Forms.EventBus.Messages;
using MediatR;

namespace Clipify.Forms.EventBus.Handlers;

public class OpenFileHandler : IRequestHandler<OpenFileMsg, string> {
    public Task<string> Handle(OpenFileMsg request, CancellationToken cancellationToken) {
        Console.WriteLine($"打开文件！！！");

        return Task.FromResult("Pong");
    }
}