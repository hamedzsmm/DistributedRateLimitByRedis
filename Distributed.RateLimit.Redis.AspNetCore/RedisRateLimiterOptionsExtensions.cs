using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Distributed.RateLimit.Redis.AspNetCore
{
    public static class RedisRateLimiterOptionsExtensions
    {
        /// <summary>
        /// Adds a new <see cref="RedisConcurrencyRateLimiter{TKey}"/> with the given configuration to the specified <see cref="RateLimiterOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="RateLimiterOptions"/> to which the limiter will be added.</param>
        /// <param name="policyName">The unique name associated with this limiter policy.</param>
        /// <param name="configureOptions">An action that configures the options used to initialize the <see cref="RedisConcurrencyRateLimiter{TKey}"/>.</param>
        /// <param name="partitionKeySelector">A function that extracts a key from the <see cref="HttpContext"/> for partitioning rate limits.</param>
        /// <returns>The updated <see cref="RateLimiterOptions"/> instance.</returns>
        public static RateLimiterOptions AddRedisConcurrencyLimiter(this RateLimiterOptions options,
            string policyName,
            Action<RedisConcurrencyRateLimiterOptions> configureOptions,
            Func<HttpContext, string> partitionKeySelector)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var key = new PolicyNameKey { PolicyName = policyName };
            var concurrencyRateLimiterOptions = new RedisConcurrencyRateLimiterOptions();
            configureOptions.Invoke(concurrencyRateLimiterOptions);

            return options.AddPolicy(policyName, context =>
            {
                var partition = partitionKeySelector(context);
                return RedisRateLimitPartition.GetConcurrencyRateLimiter($"{key}-{partition}", _ => concurrencyRateLimiterOptions);
            });
        }

        /// <summary>
        /// Adds a new <see cref="RedisFixedWindowRateLimiter{TKey}"/> with the given configuration to the specified <see cref="RateLimiterOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="RateLimiterOptions"/> to which the limiter will be added.</param>
        /// <param name="policyName">The unique name associated with this limiter policy.</param>
        /// <param name="configureOptions">An action that configures the options used to initialize the <see cref="RedisFixedWindowRateLimiter{TKey}"/>.</param>
        /// <param name="partitionKeySelector">A function that extracts a key from the <see cref="HttpContext"/> for partitioning rate limits.</param>
        /// <returns>The updated <see cref="RateLimiterOptions"/> instance.</returns>
        public static RateLimiterOptions AddRedisFixedWindowLimiter(
            this RateLimiterOptions options,
            string policyName,
            Action<RedisFixedWindowRateLimiterOptions> configureOptions,
            Func<HttpContext, string> partitionKeySelector)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);
            ArgumentNullException.ThrowIfNull(partitionKeySelector);

            var fixedWindowRateLimiterOptions = new RedisFixedWindowRateLimiterOptions();
            configureOptions.Invoke(fixedWindowRateLimiterOptions);

            return options.AddPolicy(policyName, context =>
            {
                var partition = partitionKeySelector(context);
                return RedisRateLimitPartition.GetFixedWindowRateLimiter(
                    partition,
                    _ => fixedWindowRateLimiterOptions);
            });
        }

        /// <summary>
        /// Adds a new <see cref="RedisSlidingWindowRateLimiter{TKey}"/> with the given configuration to the specified <see cref="RateLimiterOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="RateLimiterOptions"/> to which the limiter will be added.</param>
        /// <param name="policyName">The unique name associated with this limiter policy.</param>
        /// <param name="configureOptions">An action that configures the options used to initialize the <see cref="RedisSlidingWindowRateLimiter{TKey}"/>.</param>
        /// <param name="partitionKeySelector">A function that extracts a key from the <see cref="HttpContext"/> for partitioning rate limits.</param>
        /// <returns>The updated <see cref="RateLimiterOptions"/> instance.</returns>
        public static RateLimiterOptions AddRedisSlidingWindowLimiter(this RateLimiterOptions options,
            string policyName,
            Action<RedisSlidingWindowRateLimiterOptions> configureOptions,
            Func<HttpContext, string> partitionKeySelector)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var key = new PolicyNameKey() { PolicyName = policyName };
            var slidingWindowRateLimiterOptions = new RedisSlidingWindowRateLimiterOptions();
            configureOptions.Invoke(slidingWindowRateLimiterOptions);

            return options.AddPolicy(policyName, context =>
            {
                var partition = partitionKeySelector(context);
                return RedisRateLimitPartition.GetSlidingWindowRateLimiter($"{key}-{partition}", _ => slidingWindowRateLimiterOptions);
            });
        }

        /// <summary>
        /// Adds a new <see cref="RedisTokenBucketRateLimiter{TKey}"/> with the given configuration to the specified <see cref="RateLimiterOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="RateLimiterOptions"/> to which the limiter will be added.</param>
        /// <param name="policyName">The unique name associated with this limiter policy.</param>
        /// <param name="configureOptions">An action that configures the options used to initialize the <see cref="RedisTokenBucketRateLimiter{TKey}"/>.</param>
        /// <param name="partitionKeySelector">A function that extracts a key from the <see cref="HttpContext"/> for partitioning rate limits.</param>
        /// <returns>The updated <see cref="RateLimiterOptions"/> instance.</returns>
        public static RateLimiterOptions AddRedisTokenBucketLimiter(this RateLimiterOptions options,
            string policyName,
            Action<RedisTokenBucketRateLimiterOptions> configureOptions,
            Func<HttpContext, string> partitionKeySelector)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            var key = new PolicyNameKey() { PolicyName = policyName };
            var tokenBucketRateLimiterOptions = new RedisTokenBucketRateLimiterOptions();
            configureOptions.Invoke(tokenBucketRateLimiterOptions);

            return options.AddPolicy(policyName, context =>
            {
                var partition = partitionKeySelector(context);
                return RedisRateLimitPartition.GetTokenBucketRateLimiter($"{key}-{partition}", _ => tokenBucketRateLimiterOptions);
            });
        }
    }
}
