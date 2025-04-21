using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Distributed.RateLimit.Redis.SlidingWindow
{
    public class RedisSlidingWindowRateLimiter<TKey> : RateLimiter
    {
        private readonly RedisSlidingWindowManager _redisManager;
        private readonly RedisSlidingWindowRateLimiterOptions _options;

        private readonly SlidingWindowLease _failedLease = new(isAcquired: false, null);

        private int _activeRequestsCount;
        private long _idleSince = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Interlocked.CompareExchange(ref _activeRequestsCount, 0, 0) > 0
            ? null
            : Stopwatch.GetElapsedTime(_idleSince);

        public RedisSlidingWindowRateLimiter(TKey partitionKey, RedisSlidingWindowRateLimiterOptions options)
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

            _options = new RedisSlidingWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = options.Window,
                ConnectionMultiplexerFactory = options.ConnectionMultiplexerFactory,
            };

            _redisManager = new RedisSlidingWindowManager(partitionKey?.ToString() ?? string.Empty, _options);
        }

        public override RateLimiterStatistics? GetStatistics()
        {
            return _redisManager.GetStatistics();
        }

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            _idleSince = Stopwatch.GetTimestamp();
            if (permitCount > _options.PermitLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount,
                    $"{permitCount} permit(s) exceeds the permit limit of {_options.PermitLimit}.");
            }

            Interlocked.Increment(ref _activeRequestsCount);
            try
            {
                return await AcquireAsyncCoreInternal();
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

        private async ValueTask<RateLimitLease> AcquireAsyncCoreInternal()
        {
            var leaseContext = new SlidingWindowLeaseContext
            {
                Limit = _options.PermitLimit,
                Window = _options.Window,
                RequestId = Guid.NewGuid().ToString(),
            };

            var response = await _redisManager.TryAcquireLeaseAsync(leaseContext.RequestId);

            leaseContext.Count = response.Count;
            leaseContext.Allowed = response.Allowed;

            if (leaseContext.Allowed)
            {
                return new SlidingWindowLease(isAcquired: true, leaseContext);
            }

            return new SlidingWindowLease(isAcquired: false, leaseContext);
        }

        private sealed class SlidingWindowLeaseContext
        {
            public long Count { get; set; }

            public long Limit { get; init; }

            public TimeSpan Window { get; set; }

            public bool Allowed { get; set; }

            public string? RequestId { get; init; }
        }

        private sealed class SlidingWindowLease(bool isAcquired, SlidingWindowLeaseContext? context) : RateLimitLease
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
                    metadata = Math.Max(context.Limit - context.Count, 0);
                    return true;
                }

                metadata = null;
                return false;
            }
        }
    }
}