using Distributed.RateLimit.Redis.AspNetCore.Test.Constants;
using Distributed.RateLimit.Redis.AspNetCore.Test.Helpers;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;

namespace Distributed.RateLimit.Redis.AspNetCore.Test.Extensions
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection serviceCollection,
            IConfiguration configuration)
        {
            serviceCollection.AddControllers();
            serviceCollection.AddEndpointsApiExplorer();
            serviceCollection.ConfigureSwaggers();

            var redisConnectionString = configuration.GetConnectionString("RedisConnectionString");
            var connectionMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString!);
            serviceCollection.AddSingleton<IConnectionMultiplexer>(sp => connectionMultiplexer);

            serviceCollection.AddRateLimiter(options =>
            {
                options.AddRedisFixedWindowLimiter(RateLimitationConstants.WeatherForecastRateLimit,
                    (opt) =>
                    {
                        opt.ConnectionMultiplexerFactory = () => connectionMultiplexer;
                        opt.PermitLimit = 1;
                        opt.Window = TimeSpan.FromSeconds(10);
                    }, context =>
                    {
                        var hashKey = "";

                        //Create hash from Bearer token
                        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
                            !StringValues.IsNullOrEmpty(authHeader))
                        {
                            var authHeaderValue = authHeader.ToString();
                            if (authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            {
                                hashKey = authHeaderValue.GetSha256Hash();
                            }
                        }
                        else
                        {
                            // Fall back to remote IP if header not present
                            hashKey = (context.Connection.RemoteIpAddress?.ToString() ?? "unknown").GetSha256Hash();
                        }

                        return hashKey;
                    });

                options.OnRejected = (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return new ValueTask();
                };
            });

            return serviceCollection;
        }
    }
}
