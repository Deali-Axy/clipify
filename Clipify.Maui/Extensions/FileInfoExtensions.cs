namespace Clipify.Maui.Extensions;

/// <summary>
/// FileInfo扩展方法
/// </summary>
public static class FileInfoExtensions
{
    /// <summary>
    /// 获取文件大小的友好表示
    /// </summary>
    /// <param name="fileInfo">文件信息</param>
    /// <returns>友好的文件大小表示</returns>
    public static string GetFriendlySize(this FileInfo fileInfo)
    {
        long bytes = fileInfo.Length;
        
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        
        return $"{number:n1} {suffixes[counter]}";
    }
} 