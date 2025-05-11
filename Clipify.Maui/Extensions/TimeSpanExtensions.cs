namespace Clipify.Maui.Extensions;

/// <summary>
/// TimeSpan扩展方法
/// </summary>
public static class TimeSpanExtensions
{
    /// <summary>
    /// 将TimeSpan转换为友好的字符串格式
    /// </summary>
    /// <param name="timeSpan">时间跨度</param>
    /// <returns>友好的字符串</returns>
    public static string ToFriendlyString(this TimeSpan timeSpan)
    {
        return $"{(int)timeSpan.TotalHours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
    }
}