#if NET9_0_OR_GREATER
#endif

namespace Juner.AspNetCore.Sequence;

/// <summary>
/// 
/// </summary>
public enum EnumerableType
{
    /// <summary>
    /// 
    /// </summary>
    AsyncEnumerable = 0,

    /// <summary>
    /// 
    /// </summary>
    Enumerable = 1,
    /// <summary>
    /// 
    /// </summary>
    ChannelReader = 2,
}