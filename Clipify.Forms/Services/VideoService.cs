using FFmpeg.NET;

namespace Clipify.Forms.Services;

public class VideoService {
    public Engine FFmpeg { get; }

    public VideoService() {
        FFmpeg = new Engine();
    }
}