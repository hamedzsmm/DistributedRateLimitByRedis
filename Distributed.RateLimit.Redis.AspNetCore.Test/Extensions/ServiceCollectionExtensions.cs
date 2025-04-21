using Distributed.RateLimit.Redis.AspNetCore.Test.Constants;
using Distributed.RateLimit.Redis.AspNetCore.Test.Helpers;
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
                        var authHeader = context.Request.Headers.TryGetValue("Authorization", out var value)
                            ? value.ToString()
                            : string.Empty;

                        return authHeader.GetSha256Hash();
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
