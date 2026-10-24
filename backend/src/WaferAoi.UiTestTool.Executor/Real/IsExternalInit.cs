// net472 不包含 IsExternalInit，C# 9 的 init 访问器需要该类型；
// 源工程用同名文件提供兼容声明，这里保持同样的做法。
namespace System.Runtime.CompilerServices
{
    /// <summary>编译器用于标记 init 访问器的内部类型。</summary>
    internal static class IsExternalInit
    {
    }
}
