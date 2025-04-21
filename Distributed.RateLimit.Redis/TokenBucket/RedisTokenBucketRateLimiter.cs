using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Distributed.RateLimit.Redis.TokenBucket
{
    public class RedisTokenBucketRateLimiter<TKey> : RateLimiter
    {
        private readonly RedisTokenBucketManager _redisManager;
        private readonly RedisTokenBucketRateLimiterOptions _options;

        private readonly TokenBucketLease _failedLease = new(isAcquired: false, null);

        private int _activeRequestsCount;
        private long _idleSince = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Interlocked.CompareExchange(ref _activeRequestsCount, 0, 0) > 0
            ? null
            : Stopwatch.GetElapsedTime(_idleSince);

        public RedisTokenBucketRateLimiter(TKey partitionKey, RedisTokenBucketRateLimiterOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.TokenLimit <= 0)
            {
                throw new ArgumentException($"{nameof(options.TokenLimit)} must be set to a value greater than 0.", nameof(options));
            }
            if (options.TokensPerPeriod <= 0)
            {
                throw new ArgumentException($"{nameof(options.TokensPerPeriod)} must be set to a value greater than 0.", nameof(options));
            }
            if (options.ReplenishmentPeriod <= TimeSpan.Zero)
            {
                throw new ArgumentException($"{nameof(options.ReplenishmentPeriod)} must be set to a value greater than TimeSpan.Zero.", nameof(options));
            }
            
            if (options.ConnectionMultiplexerFactory is null)
            {
                throw new ArgumentException($"{nameof(options.ConnectionMultiplexerFactory)} must not be null.", nameof(options));
            }
            _options = new RedisTokenBucketRateLimiterOptions
            {
                ConnectionMultiplexerFactory = options.ConnectionMultiplexerFactory,
                TokenLimit = options.TokenLimit,
                ReplenishmentPeriod = options.ReplenishmentPeriod,
                TokensPerPeriod = options.TokensPerPeriod,
            };

            _redisManager = new RedisTokenBucketManager(partitionKey?.ToString() ?? string.Empty, _options);
        }

        public override RateLimiterStatistics? GetStatistics()
        {
            throw new NotImplementedException();
        }

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            _idleSince = Stopwatch.GetTimestamp();
            if (permitCount > _options.TokenLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount,
                    $"{permitCount} permit(s) exceeds the permit limit of {_options.TokenLimit}.");
            }

            Interlocked.Increment(ref _activeRequestsCount);
            try
            {
                return await AcquireAsyncCoreInternal(permitCount);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequestsCount);
                _idleSince = Stopwatch.GetTimestamp();
            }
        }

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            return _failedLease;
        }

        private async ValueTask<RateLimitLease> AcquireAsyncCoreInternal(int permitCount)
        {
            var leaseContext = new TokenBucketLeaseContext
            {
                Limit = _options.TokenLimit,
            };

            var response = await _redisManager.TryAcquireLeaseAsync(permitCount);

            leaseContext.Allowed = response.Allowed;
            leaseContext.Count = response.Count;
            leaseContext.RetryAfter = response.RetryAfter;

            if (leaseContext.Allowed)
            {
                return new TokenBucketLease(isAcquired: true, leaseContext);
            }

            return new TokenBucketLease(isAcquired: false, leaseContext);
        }

        private sealed class TokenBucketLeaseContext
        {
            public long Count { get; set; }

            public long Limit { get; init; }

            public int RetryAfter { get; set; }

            public bool Allowed { get; set; }
        }

        private sealed class TokenBucketLease(bool isAcquired, TokenBucketLeaseContext? context) : RateLimitLease
        {
            public override bool IsAcquired { get; } = isAcquired;

            public override IEnumerable<string> MetadataNames => [RateLimitMetadataName.Limit.Name, RateLimitMetadataName.Remaining.Name];

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                if (metadataName == RateLimitMetadataName.Limit.Name && context is not null)
                {
                    metadata = context.Limit.ToString();
                    return true;
                }

                if (metadataName == RateLimitMetadataName.Remaining.Name && context is not null)
                {
                    metadata = context.Count;
                    return true;
                }

                if (metadataName == RateLimitMetadataName.RetryAfter.Name && context is not null)
                {
                    metadata = context.RetryAfter;
                    return true;
                }

                metadata = null;
                return false;
            }
        }
    }
}