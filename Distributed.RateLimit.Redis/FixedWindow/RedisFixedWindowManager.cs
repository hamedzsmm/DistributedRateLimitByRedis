using StackExchange.Redis;

namespace Distributed.RateLimit.Redis.FixedWindow
{
    internal class RedisFixedWindowManager(
        string partitionKey,
        RedisFixedWindowRateLimiterOptions options)
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer = options.ConnectionMultiplexerFactory!.Invoke();
        private readonly RedisKey _rateLimitKey = new($"rl:fw:{{{partitionKey}}}");
        private readonly RedisKey _rateLimitExpireKey = new($"rl:fw:{{{partitionKey}}}:exp");

        private static readonly LuaScript Script = LuaScript.Prepare(
          @"local expires_at = tonumber(redis.call(""get"", @expires_at_key))
            local limit = tonumber(@permit_limit)
            local inc = tonumber(@increment_amount)

            if not expires_at or expires_at < tonumber(@current_time) then
                -- this is either a brand new window,
                -- or this window has closed, but redis hasn't cleaned up the key yet
                -- (redis will clean it up in one more second)
                -- initialize a new rate limit window
                redis.call(""set"", @rate_limit_key, 0)
                redis.call(""set"", @expires_at_key, @next_expires_at)
                -- tell Redis to clean this up _one second after_ the expires_at time (clock differences).
                -- (Redis will only clean up these keys long after the window has passed)
                redis.call(""expireat"", @rate_limit_key, @next_expires_at + 1)
                redis.call(""expireat"", @expires_at_key, @next_expires_at + 1)
                -- since the database was updated, return the new value
                expires_at = @next_expires_at
            end

            -- now that the window either already exists or it was freshly initialized
            -- increment the counter(`incrby` returns a number)

            local current = redis.call(""get"", @rate_limit_key)

            if not current then
                current = 0
            else
                current = tonumber(current)
            end

            local allowed = current + inc <= limit

            if allowed then
                current = redis.call(""incrby"", @rate_limit_key, inc)
            end 

            return { current, expires_at, allowed } 
        ");

        internal async Task<RedisFixedWindowResponse> TryAcquireLeaseAsync(int permitCount)
        {
            var now = DateTimeOffset.UtcNow;
            var nowUnixTimeSeconds = now.ToUnixTimeSeconds();

            var database = _connectionMultiplexer.GetDatabase();

            var response = (RedisValue[]?)await database.ScriptEvaluateAsync(
                Script,
                new
                {
                    rate_limit_key = _rateLimitKey,
                    expires_at_key = _rateLimitExpireKey,
                    next_expires_at = (RedisValue)now.Add(options.Window).ToUnixTimeSeconds(),
                    current_time = (RedisValue)nowUnixTimeSeconds,
                    permit_limit = (RedisValue)options.PermitLimit,
                    increment_amount = (RedisValue)permitCount,
                });

            var result = new RedisFixedWindowResponse();

            if (response != null)
            {
                result.Count = (long)response[0];
                result.ExpiresAt = (long)response[1];
                result.Allowed = (bool)response[2];
                result.RetryAfter = TimeSpan.FromSeconds(result.ExpiresAt - nowUnixTimeSeconds);
            }

            return result;
        }

        internal RedisFixedWindowResponse TryAcquireLease()
        {
            var now = DateTimeOffset.UtcNow;
            var nowUnixTimeSeconds = now.ToUnixTimeSeconds();

            var database = _connectionMultiplexer.GetDatabase();

            var response = (RedisValue[]?)database.ScriptEvaluate(
                Script,
                new
                {
                    rate_limit_key = _rateLimitKey,
                    expires_at_key = _rateLimitExpireKey,
                    next_expires_at = (RedisValue)now.Add(options.Window).ToUnixTimeSeconds(),
                    current_time = (RedisValue)nowUnixTimeSeconds,
                    increment_amount = (RedisValue)1D,
                });

            var result = new RedisFixedWindowResponse();

            if (response != null)
            {
                result.Count = (long)response[0];
                result.ExpiresAt = (long)response[1];
                result.RetryAfter = TimeSpan.FromSeconds(result.ExpiresAt - nowUnixTimeSeconds);
            }

            return result;
        }
    }

    internal class RedisFixedWindowResponse
    {
        internal long ExpiresAt { get; set; }
        internal TimeSpan RetryAfter { get; set; }
        internal long Count { get; set; }
        internal bool Allowed { get; set; }
    }
}
