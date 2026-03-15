using Juner.AspNetCore.Sequence.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;

#pragma warning disable IDE0130 // Namespace がフォルダー構造と一致しません
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 // Namespace がフォルダー構造と一致しません

public static class SequenceExtensions
{
    public static IMvcBuilder AddSequenceFormatter(this IMvcBuilder builder)
    {
        builder.AddSequenceInputFormatter();
        builder.AddJsonSequenceOutputFormatter();
        builder.AddNdJsonOutputFormatter();
        builder.AddJsonLineOutputFormatter();
        return builder;
    }
    public static IMvcBuilder AddSequenceInputFormatter(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcOptions>(options =>
        {
            if (options.InputFormatters.Any(v => v is SequenceInputFormatter)) return;
            options.InputFormatters.Add(new SequenceInputFormatter());
        });
        return builder;
    }
    public static IMvcBuilder AddJsonSequenceOutputFormatter(this IMvcBuilder builder) => builder.AddOutputFormatter<JsonSequenceOutputFormatter>();
    public static IMvcBuilder AddNdJsonOutputFormatter(this IMvcBuilder builder) => builder.AddOutputFormatter<JsonSequenceOutputFormatter>();
    public static IMvcBuilder AddJsonLineOutputFormatter(this IMvcBuilder builder) => builder.AddOutputFormatter<JsonLineOutputFormatter>();
    static IMvcBuilder AddOutputFormatter<T>(this IMvcBuilder builder)
        where T : TextOutputFormatter, new()
    {
        builder.Services.Configure<MvcOptions>(options =>
        {
            if (options.OutputFormatters.Any(v => v is T)) return;
            var insert =
                options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>().FirstOrDefault() is { } formatter
                ? options.OutputFormatters.IndexOf(formatter) : 0;
            options.OutputFormatters.Insert(
                insert,
                new T());
        });

        return builder;
    }
}