using Juner.AspNetCore.Sequence.Formatters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Juner.AspNetCore.Sequence;

public static class JsonSequenceExtensions
{
    public static IMvcBuilder AddJsonSequence(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcOptions>(options =>
        {
            var jsonOptions = builder.Services
                .BuildServiceProvider()
                .GetRequiredService<IOptions<JsonOptions>>();
            var insert =
                options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>().FirstOrDefault() is { } formatter
                ? options.OutputFormatters.IndexOf(formatter) : 0;
            options.OutputFormatters.Insert(
                insert,
                JsonSequenceOutputFormatter.CreateFormatter(jsonOptions.Value));
        });

        return builder;
    }

}