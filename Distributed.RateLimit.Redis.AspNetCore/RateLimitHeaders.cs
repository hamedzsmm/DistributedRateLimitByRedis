﻿namespace Distributed.RateLimit.Redis.AspNetCore
{
    public static class RateLimitHeaders
    {
        public const string Limit = "X-RateLimit-Limit";
        public const string Remaining = "X-RateLimit-Remaining";
        public const string Reset = "X-RateLimit-Reset";
        public const string RetryAfter = "Retry-After";
    }
}
