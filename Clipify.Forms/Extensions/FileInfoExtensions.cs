namespace Clipify.Forms.Extensions;

public static class FileInfoExtensions {
    public static string GetFriendlySize(this FileInfo fileInfo) {
        string[] sizeUnits = { "Bytes", "KB", "MB", "GB", "TB" };
        double fileSize = fileInfo.Length;
        int unitIndex = 0;

        while (fileSize >= 1024 && unitIndex < sizeUnits.Length - 1) {
            fileSize /= 1024;
            unitIndex++;
        }

        return $"{fileSize:F2} {sizeUnits[unitIndex]}";
    }
}