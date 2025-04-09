Distributed Rate Limit By Redis
NuGet
License

A distributed rate limiting solution for ASP.NET Core applications using Redis as a backend. This package ensures consistent rate limiting across multiple pods/instances behind a load balancer.

Problem Solved
The standard ASP.NET Core rate limiter doesn't work effectively in distributed environments with multiple pods. When requests are distributed across pods by a load balancer, each pod maintains its own rate limit counter, allowing clients to exceed the intended limit by spreading requests across different pods.

This package solves this by using Redis as a centralized rate limiting store, ensuring consistent enforcement across all instances.

Installation
Install the package via NuGet:

```xml
dotnet add package DistributedRateLimitByRedis
```


Usage
1. Configure the Rate Limiter
In your Program.cs or startup configuration:


```xml
using DistributedRateLimiting.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add Redis connection multiplexer
var connectionMultiplexer = ConnectionMultiplexer.Connect("your_redis_connection_string");

builder.Services.AddRateLimiter(options =>
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

var app = builder.Build();

app.UseRateLimiter();
```

2. Apply Rate Limiting to Endpoints
Apply rate limiting to your controllers or actions using the [EnableRateLimiting] attribute:

```xml

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitationConstants.WeatherForecastRateLimit)]
    public IEnumerable<WeatherForecast> Get()
    {
        // Your action logic
    }
}
```

Configuration Options
The AddRedisFixedWindowLimiter method accepts the following configuration:

ConnectionMultiplexerFactory: Factory function to get Redis connection

PermitLimit: Maximum number of requests allowed in the time window

Window: Time window duration

QueueLimit (optional): Maximum number of requests that can be queued when the limit is reached

AutoReplenishment (optional): Whether to auto-replenish permits

The partition key function determines how to identify clients. The example uses the X-Forwarded-For header (common in load-balanced environments) with a fallback to the remote IP address.

Rate Limiting Algorithms
Currently supports:

Fixed Window: Limits the number of requests per fixed time window (e.g., 10 requests per minute)

Performance Considerations
Redis operations are fast, but network latency should be considered

For high-traffic applications, ensure your Redis instance is properly scaled

The library minimizes Redis operations by using efficient Lua scripts

Contributing
Contributions are welcome! Please open an issue or submit a pull request.

License
MIT License. See LICENSE for details.
