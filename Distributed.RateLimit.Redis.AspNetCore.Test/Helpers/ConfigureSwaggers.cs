using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Distributed.RateLimit.Redis.AspNetCore.Test.Helpers;

public static class ConfigureSwagger
{
    public static void ConfigureSwaggers(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.DefaultSecurityDefinition();

            options.SwaggerDoc("v1", new OpenApiInfo()
            {
                Title = "Distributed RateLimit Redis AspNetCore V1",
                Version = "v1",
            });
        });
    }

    public static void DefaultSecurityDefinition(this SwaggerGenOptions option)
    {
        option.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Please enter token",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            Scheme = "bearer"
        });

        option.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type=ReferenceType.SecurityScheme,
                            Id="Bearer"
                        }
                    },
                    []
                }
            });
    }
}