namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// net472 的 <see cref="string"/> 没有 Contains(string, StringComparison) 重载，
/// 源工程用 IndexOf 写法；这里统一成扩展方法，避免逐处改写并保持序数比较语义。
/// </summary>
internal static class StringCompat
{
    /// <summary>按指定比较方式判断是否包含子串。</summary>
    /// <param name="value">源字符串。</param>
    /// <param name="search">子串。</param>
    /// <param name="comparison">比较方式。</param>
    /// <returns>包含时为 true。</returns>
    public static bool Contains(this string value, string search, StringComparison comparison) =>
        value != null && value.IndexOf(search, comparison) >= 0;
}
