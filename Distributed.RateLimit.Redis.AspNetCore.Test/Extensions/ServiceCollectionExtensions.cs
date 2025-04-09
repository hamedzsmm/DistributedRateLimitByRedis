using Distributed.RateLimit.Redis.AspNetCore.Test.Constants;
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
            serviceCollection.AddSwaggerGen();

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
                        string partitionKey;

                        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
                            !StringValues.IsNullOrEmpty(forwardedFor))
                        {
                            partitionKey = forwardedFor.ToString();
                        }
                        else
                        {
                            // Fall back to remote IP if header not present
                            partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        }

                        return partitionKey;
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
