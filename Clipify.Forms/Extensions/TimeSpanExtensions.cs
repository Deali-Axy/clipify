namespace Clipify.Forms.Extensions;

public static class TimeSpanExtensions {
    public static string ToFriendlyString(this TimeSpan timeSpan, string locale = "zh-cn") {
        var parts = new List<string>();

        switch (locale) {
            case "zh-cn":
                if (timeSpan.Days > 0)
                    parts.Add($"{timeSpan.Days}天");

                if (timeSpan.Hours > 0)
                    parts.Add($"{timeSpan.Hours}小时");

                if (timeSpan.Minutes > 0)
                    parts.Add($"{timeSpan.Minutes}分钟");

                if (timeSpan.Seconds > 0)
                    parts.Add($"{timeSpan.Seconds}秒");

                // 如果没有天、小时、分钟或秒的部分，显示为 0 秒
                if (parts.Count == 0)
                    return "0 秒";
                
                break;
            default:
                if (timeSpan.Days > 0)
                    parts.Add($"{timeSpan.Days} day{(timeSpan.Days > 1 ? "s" : "")}");

                if (timeSpan.Hours > 0)
                    parts.Add($"{timeSpan.Hours} hour{(timeSpan.Hours > 1 ? "s" : "")}");

                if (timeSpan.Minutes > 0)
                    parts.Add($"{timeSpan.Minutes} minute{(timeSpan.Minutes > 1 ? "s" : "")}");

                if (timeSpan.Seconds > 0)
                    parts.Add($"{timeSpan.Seconds} second{(timeSpan.Seconds > 1 ? "s" : "")}");

                // 如果没有天、小时、分钟或秒的部分，显示为 0 秒
                if (parts.Count == 0)
                    return "0 seconds";
                break;
        }


        return string.Join(", ", parts);
    }
}