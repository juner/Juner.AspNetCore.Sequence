using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Juner.AspNetCore.JsonSequence;

public static class JsonSequenceExtensions
{
    public static IMvcBuilder AddJsonSequence(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcOptions>(options =>
        {
            var jsonOptions = builder.Services
                .BuildServiceProvider()
                .GetRequiredService<IOptions<JsonOptions>>();

            options.OutputFormatters.Insert(
                0,
                JsonSequenceOutputFormatter.CreateFormatter(jsonOptions.Value));
        });

        return builder;
    }

}
