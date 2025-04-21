using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Distributed.RateLimit.Redis.FixedWindow
{
    public class RedisFixedWindowRateLimiter<TKey> : RateLimiter
    {
        private readonly RedisFixedWindowManager _redisManager;
        private readonly RedisFixedWindowRateLimiterOptions _options;

        private readonly FixedWindowLease _failedLease = new(isAcquired: false, null);

        private int _activeRequestsCount;
        private long _idleSince = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Interlocked.CompareExchange(ref _activeRequestsCount, 0, 0) > 0
            ? null
            : Stopwatch.GetElapsedTime(_idleSince);

        public RedisFixedWindowRateLimiter(TKey partitionKey, RedisFixedWindowRateLimiterOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.PermitLimit <= 0)
            {
                throw new ArgumentException($"{nameof(options.PermitLimit)} must be set to a value greater than 0.", nameof(options));
            }
            if (options.Window <= TimeSpan.Zero)
            {
                throw new ArgumentException($"{nameof(options.Window)} must be set to a value greater than TimeSpan.Zero.", nameof(options));
            }
            if (options.ConnectionMultiplexerFactory is null)
            {
                throw new ArgumentException($"{nameof(options.ConnectionMultiplexerFactory)} must not be null.", nameof(options));
            }

            _options = new RedisFixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = options.Window,
                ConnectionMultiplexerFactory = options.ConnectionMultiplexerFactory,
            };

            _redisManager = new RedisFixedWindowManager(partitionKey?.ToString() ?? string.Empty, _options);
        }

        public override RateLimiterStatistics? GetStatistics()
        {
            throw new NotImplementedException();
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            if (permitCount > _options.PermitLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount,
                    $"{permitCount} permit(s) exceeds the permit limit of {_options.PermitLimit}.");
            }

            return AcquireAsyncCoreInternal(permitCount);
        }

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            return _failedLease;
        }

        private async ValueTask<RateLimitLease> AcquireAsyncCoreInternal(int permitCount)
        {
            var leaseContext = new FixedWindowLeaseContext
            {
                Limit = _options.PermitLimit,
                Window = _options.Window,
            };

            RedisFixedWindowResponse response;
            Interlocked.Increment(ref _activeRequestsCount);
            try
            {
                response = await _redisManager.TryAcquireLeaseAsync(permitCount);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequestsCount);
                _idleSince = Stopwatch.GetTimestamp();
            }

            leaseContext.Count = response.Count;
            leaseContext.RetryAfter = response.RetryAfter;
            leaseContext.ExpiresAt = response.ExpiresAt;

            return new FixedWindowLease(isAcquired: response.Allowed, leaseContext);
        }

        private sealed class FixedWindowLeaseContext
        {
            public long Count { get; set; }

            public long Limit { get; init; }

            public TimeSpan Window { get; set; }

            public TimeSpan? RetryAfter { get; set; }

            public long? ExpiresAt { get; set; }
        }

        private sealed class FixedWindowLease(bool isAcquired, FixedWindowLeaseContext? context) : RateLimitLease
        {
            public override bool IsAcquired { get; } = isAcquired;

            public override IEnumerable<string> MetadataNames => [RateLimitMetadataName.Limit.Name, RateLimitMetadataName.Remaining.Name, RateLimitMetadataName.RetryAfter.Name];

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                if (metadataName == RateLimitMetadataName.Limit.Name && context is not null)
                {
                    metadata = context.Limit.ToString();
                    return true;
                }

                if (metadataName == RateLimitMetadataName.Remaining.Name && context is not null)
                {
                    metadata = Math.Max(context.Limit - context.Count, 0);
                    return true;
                }

                if (metadataName == RateLimitMetadataName.RetryAfter.Name && context?.RetryAfter is not null)
                {
                    metadata = (int)context.RetryAfter.Value.TotalSeconds;
                    return true;
                }

                if (metadataName == RateLimitMetadataName.Reset.Name && context?.ExpiresAt is not null)
                {
                    metadata = context.ExpiresAt.Value;
                    return true;
                }

                metadata = null;
                return false;
            }
        }
    }
}