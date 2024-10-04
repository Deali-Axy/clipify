using Clipify.Forms.Services;
using MediatR;

namespace Clipify.Forms.EventBus.Notification;

public class FileSelectedNoti : INotification {
    public string FilePath { get; set; }
}

public class FileSelectedHandler : INotificationHandler<FileSelectedNoti> {
    private readonly FileDialogService _fileDialogService;

    public FileSelectedHandler(FileDialogService fileDialogService) {
        _fileDialogService = fileDialogService;
    }

    public Task Handle(FileSelectedNoti notification, CancellationToken cancellationToken) {
        _fileDialogService.NotifyFileSelected(notification.FilePath);
        return Task.CompletedTask;
    }
}