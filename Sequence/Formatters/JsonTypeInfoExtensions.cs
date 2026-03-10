using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

#if NET9_0_OR_GREATER
#endif

namespace Juner.AspNetCore.Sequence.Formatters;

internal static class JsonTypeInfoExtensions
{
    public static bool HasKnownPolymorphism(this JsonTypeInfo jsonTypeInfo)
       => jsonTypeInfo.Type.IsSealed || jsonTypeInfo.Type.IsValueType || jsonTypeInfo.PolymorphismOptions is not null;
    public static bool ShouldUseWith(this JsonTypeInfo jsonTypeInfo, [NotNullWhen(false)] Type? runtimeType)
       => runtimeType is null || jsonTypeInfo.Type == runtimeType || jsonTypeInfo.HasKnownPolymorphism();
}
