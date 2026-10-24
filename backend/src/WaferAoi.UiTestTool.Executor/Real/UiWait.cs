using System.Diagnostics;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 有界条件轮询：每 100 毫秒复查一次，条件满足立即返回，不做事先固定延时。
/// 迁移自源工程 Drivers/UiWait.cs，保留“终止型等待”与“普通超时”的区分。
/// </summary>
internal static class UiWait
{
    /// <summary>轮询间隔；条件是业务事实，不能靠睡眠代替。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>等待工厂返回非空对象。</summary>
    /// <typeparam name="T">引用类型。</typeparam>
    /// <param name="factory">反复求值的工厂。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="description">超时信息中的业务描述。</param>
    /// <returns>第一个非空结果。</returns>
    public static T UntilNotNull<T>(Func<T?> factory, TimeSpan timeout, string description)
        where T : class
    {
        T? value = null;
        Until(
            () =>
            {
                value = factory();
                return value != null;
            },
            timeout,
            description);
        return value!;
    }

    /// <summary>等待布尔条件成立，并保留最后一次瞬时异常。</summary>
    /// <param name="condition">轮询条件。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="description">超时信息中的业务描述。</param>
    public static void Until(Func<bool> condition, TimeSpan timeout, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (Exception exception) when (
                exception is not UiWaitTerminatedException && exception is not StepVerdictException)
            {
                // WPF 替换窗口或虚拟化列表行时 UIA 元素会失效；这类异常按瞬时状态重试。
                // 业务结论（失败/阻塞）不是瞬时状态，不能重试。
                lastException = exception;
            }

            Task.Delay(PollInterval).GetAwaiter().GetResult();
        }

        throw new TimeoutException(
            $"等待 {description} 超过 {timeout.TotalSeconds:0} 秒仍未满足。",
            lastException);
    }
}

/// <summary>等待已不可能成功，必须立即停止，而不是当作瞬时 UIA 状态重试。</summary>
internal sealed class UiWaitTerminatedException : Exception
{
    /// <summary>创建终止型等待异常。</summary>
    /// <param name="message">业务可读原因。</param>
    public UiWaitTerminatedException(string message)
        : base(message)
    {
    }
}

/// <summary>步骤已有明确业务结论，直接结束本步骤并保留原始结论。</summary>
internal sealed class StepVerdictException : Exception
{
    /// <summary>创建带业务结论的异常。</summary>
    /// <param name="status">结论状态。</param>
    /// <param name="message">结论原因。</param>
    public StepVerdictException(Contracts.StepStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    /// <summary>结论状态。</summary>
    public Contracts.StepStatus Status { get; }
}
