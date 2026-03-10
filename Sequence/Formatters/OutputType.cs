#if NET9_0_OR_GREATER
#endif

namespace Juner.AspNetCore.Sequence.Formatters;

/// <summary>
/// 
/// </summary>
public enum OutputType
{
    /// <summary>
    /// 
    /// </summary>
    AsyncEnumerable = 0,

    /// <summary>
    /// 
    /// </summary>
    Enumerable = 1,
}