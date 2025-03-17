using System;

namespace Distributed.RateLimit.Redis
{
    /// <summary>
    /// Options to specify the behavior of a <see cref="RedisFixedWindowRateLimiter"/>.
    /// </summary>
    public sealed class RedisFixedWindowRateLimiterOptions : RedisRateLimiterOptions
    {
        /// <summary>
        /// Specifies the time window that takes in the requests.
        /// Must be set to a value greater than <see cref="TimeSpan.Zero" /> by the time these options are passed to the constructor of <see cref="RedisFixedWindowRateLimiter{TKey}"/>.
        /// </summary>
        public TimeSpan Window { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Maximum number of permit counters that can be allowed in a window.
        /// Must be set to a value > 0 by the time these options are passed to the constructor of <see cref="RedisFixedWindowRateLimiter{TKey}"/>.
        /// </summary>
        public int PermitLimit { get; set; }

        /// <summary>
        /// Determines whether the rate limiter should consider a specific token when checking the request limit within the fixed window.
        /// If set to <c>true</c>, the rate limiter will evaluate the request count for the specified token separately.
        /// If set to <c>false</c>, the rate limiter will evaluate the request count globally across all tokens.
        /// Default is <c>false</c>.
        /// </summary>
        public bool CheckWithToken { get; set; } = false;
    }
}
