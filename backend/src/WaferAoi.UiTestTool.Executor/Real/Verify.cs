using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 步骤内断言。失败=业务断言不成立（Failed）；阻塞=前提或控件不可用（Blocked）。
/// 语义与源工程 StepRecorder 的 Assert/Inconclusive 划分一致，但不再依赖 MSTest。
/// </summary>
internal static class Verify
{
    /// <summary>断言条件成立，否则本步骤失败。</summary>
    /// <param name="condition">断言条件。</param>
    /// <param name="message">失败原因。</param>
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new StepVerdictException(StepStatus.Failed, message);
        }
    }

    /// <summary>断言字符串逐字相等，否则本步骤失败。</summary>
    /// <param name="expected">期望值。</param>
    /// <param name="actual">实际值。</param>
    /// <param name="message">失败原因。</param>
    public static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new StepVerdictException(StepStatus.Failed, $"{message}（期望“{expected}”，实际“{actual}”）");
        }
    }

    /// <summary>断言整数相等。</summary>
    /// <param name="expected">期望值。</param>
    /// <param name="actual">实际值。</param>
    /// <param name="message">失败原因。</param>
    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new StepVerdictException(StepStatus.Failed, $"{message}（期望 {expected}，实际 {actual}）");
        }
    }

    /// <summary>断言条件成立，否则本步骤阻塞；用于控件、模式、环境前提缺失。</summary>
    /// <param name="condition">前提条件。</param>
    /// <param name="message">阻塞原因，必须写明缺少什么。</param>
    public static void Available(bool condition, string message)
    {
        if (!condition)
        {
            throw new StepVerdictException(StepStatus.Blocked, message);
        }
    }

    /// <summary>直接以阻塞结束本步骤。</summary>
    /// <param name="message">阻塞原因。</param>
    public static Exception Blocked(string message) => new StepVerdictException(StepStatus.Blocked, message);

    /// <summary>直接以失败结束本步骤。</summary>
    /// <param name="message">失败原因。</param>
    public static Exception Failed(string message) => new StepVerdictException(StepStatus.Failed, message);
}
