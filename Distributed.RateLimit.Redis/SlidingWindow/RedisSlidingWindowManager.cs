using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Distributed.RateLimit.Redis.SlidingWindow
{
    internal class RedisSlidingWindowManager(
        string partitionKey,
        RedisSlidingWindowRateLimiterOptions options)
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer = options.ConnectionMultiplexerFactory!.Invoke();
        private readonly RedisKey _rateLimitKey = new($"rl:sw:{{{partitionKey}}}");
        private readonly RedisKey _statsRateLimitKey = new($"rl:sw:{{{partitionKey}}}:stats");

        private static readonly LuaScript Script = LuaScript.Prepare(
          @"local limit = tonumber(@permit_limit)
            local timestamp = tonumber(@current_time)
            local window = tonumber(@window)

            -- remove all requests outside current window
            redis.call(""zremrangebyscore"", @rate_limit_key, '-inf', timestamp - window)

            local count = redis.call(""zcard"", @rate_limit_key)
            local allowed = count < limit

            if allowed
            then
                redis.call(""zadd"", @rate_limit_key, timestamp, @unique_id)
            end

            local expireAtMilliseconds = math.floor((timestamp + window) * 1000 + 1);
            redis.call(""pexpireat"", @rate_limit_key, expireAtMilliseconds)

            if allowed
            then
                redis.call(""hincrby"", @stats_key, 'total_successful', 1)
            else
                redis.call(""hincrby"", @stats_key, 'total_failed', 1)
            end

            redis.call(""pexpireat"", @stats_key, expireAtMilliseconds)

            return { allowed, count }");

        private static readonly LuaScript StatisticsScript = LuaScript.Prepare(
            @"local count = redis.call(""zcard"", @rate_limit_key)
            local total_successful_count = redis.call(""hget"", @stats_key, 'total_successful')
            local total_failed_count = redis.call(""hget"", @stats_key, 'total_failed')

            return { count, total_successful_count, total_failed_count }");

        internal async Task<RedisSlidingWindowResponse> TryAcquireLeaseAsync(string requestId)
        {
            var now = DateTimeOffset.UtcNow;
            double nowUnixTimeSeconds = now.ToUnixTimeMilliseconds() / 1000.0;

            var database = _connectionMultiplexer.GetDatabase();

            var response = (RedisValue[]?)await database.ScriptEvaluateAsync(
                Script,
                new
                {
                    rate_limit_key = _rateLimitKey,
                    stats_key = _statsRateLimitKey,
                    permit_limit = (RedisValue)options.PermitLimit,
                    window = (RedisValue)options.Window.TotalSeconds,
                    current_time = (RedisValue)nowUnixTimeSeconds,
                    unique_id = (RedisValue)requestId,
                });

            var result = new RedisSlidingWindowResponse();

            if (response != null)
            {
                result.Allowed = (bool)response[0];
                result.Count = (long)response[1];
            }

            return result;
        }

        internal RedisSlidingWindowResponse TryAcquireLease(string requestId)
        {
            var now = DateTimeOffset.UtcNow;
            double nowUnixTimeSeconds = now.ToUnixTimeMilliseconds() / 1000.0;

            var database = _connectionMultiplexer.GetDatabase();

            var response = (RedisValue[]?)database.ScriptEvaluate(
               Script,
               new
               {
                   rate_limit_key = _rateLimitKey,
                   stats_key = _statsRateLimitKey,
                   permit_limit = (RedisValue)options.PermitLimit,
                   window = (RedisValue)options.Window.TotalSeconds,
                   current_time = (RedisValue)nowUnixTimeSeconds,
                   unique_id = (RedisValue)requestId,
               });

            var result = new RedisSlidingWindowResponse();

            if (response != null)
            {
                result.Allowed = (bool)response[0];
                result.Count = (long)response[1];
            }

            return result;
        }

        internal RateLimiterStatistics? GetStatistics()
        {
            var database = _connectionMultiplexer.GetDatabase();

            var response = (RedisValue[]?)database.ScriptEvaluate(
                StatisticsScript,
                new
                {
                    rate_limit_key = _rateLimitKey,
                    stats_key = _statsRateLimitKey,
                });

            if (response == null)
            {
                return null;
            }

            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = options.PermitLimit - (long)response[0],
                TotalSuccessfulLeases = (long)response[1],
                TotalFailedLeases = (long)response[2],
            };
        }
    }

    internal class RedisSlidingWindowResponse
    {
        internal bool Allowed { get; set; }
        internal long Count { get; set; }
    }
}
