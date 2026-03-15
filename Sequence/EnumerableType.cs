#if NET9_0_OR_GREATER
#endif

namespace Juner.AspNetCore.Sequence;

/// <summary>
/// enumerable type
/// </summary>
public enum EnumerableType
{
    /// <summary>
    /// use <see cref="IAsyncEnumerable{T}"/>
    /// </summary>
    AsyncEnumerable = 0,

    /// <summary>
    /// use <see cref="IEnumerable{T}"/>
    /// </summary>
    Enumerable = 1,
    /// <summary>
    /// use <see cref="System.Threading.Channels.ChannelReader{T}"/>
    /// </summary>
    ChannelReader = 2,
    /// <summary>
    /// use <see cref="Array"/>
    /// </summary>
    Array = 3,
    /// <summary>
    /// use <see cref="List{T}"/>
    /// </summary>
    List = 4,
}