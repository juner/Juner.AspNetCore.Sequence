using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Juner.AspNetCore.Sequence.Internals;

internal static class JsonSerializerOptionsExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0060 // 未使用のパラメーターを削除します
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options, T value) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
#pragma warning restore IDE0060 // 未使用のパラメーターを削除します

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options, JsonTypeInfo<T> otherTypeInfo) =>
        otherTypeInfo?.Options == options ? otherTypeInfo : options.GetTypeInfo<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}