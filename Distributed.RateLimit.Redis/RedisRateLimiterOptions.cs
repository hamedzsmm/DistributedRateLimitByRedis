using StackExchange.Redis;

namespace Distributed.RateLimit.Redis
{
    public abstract class RedisRateLimiterOptions
    {
        /// <summary>
        /// Factory for a Redis ConnectionMultiplexer.
        /// </summary>
        public Func<IConnectionMultiplexer>? ConnectionMultiplexerFactory { get; set; }
    }
}
