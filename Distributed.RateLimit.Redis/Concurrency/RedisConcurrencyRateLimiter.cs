using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Distributed.RateLimit.Redis.Concurrency
{
    public class RedisConcurrencyRateLimiter<TKey> : RateLimiter
    {
        private readonly RedisConcurrencyManager _redisManager;
        private readonly RedisConcurrencyRateLimiterOptions _options;
        private readonly ConcurrentQueue<Request> _queue = new();

        private readonly PeriodicTimer? _periodicTimer;

        private bool _disposed;

        private readonly ConcurrencyLease _failedLease = new(false, null, null);

        private int _activeRequestsCount;
        private long _idleSince = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Interlocked.CompareExchange(ref _activeRequestsCount, 0, 0) > 0
            ? null
            : Stopwatch.GetElapsedTime(_idleSince);

        public RedisConcurrencyRateLimiter(TKey partitionKey, RedisConcurrencyRateLimiterOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.PermitLimit <= 0)
            {
                throw new ArgumentException($"{nameof(options.PermitLimit)} must be set to a value greater than 0.", nameof(options));
            }
            if (options.QueueLimit < 0)
            {
                throw new ArgumentException($"{nameof(options.QueueLimit)} must be set to a value greater than 0.", nameof(options));
            }
            if (options.ConnectionMultiplexerFactory is null)
            {
                throw new ArgumentException($"{nameof(options.ConnectionMultiplexerFactory)} must not be null.", nameof(options));
            }

            _options = new RedisConcurrencyRateLimiterOptions
            {
                ConnectionMultiplexerFactory = options.ConnectionMultiplexerFactory,
                PermitLimit = options.PermitLimit,
                QueueLimit = options.QueueLimit,
                TryDequeuePeriod = options.TryDequeuePeriod,
            };

            _redisManager = new RedisConcurrencyManager(partitionKey?.ToString() ?? string.Empty, _options);

            if (_options.QueueLimit > 0)
            {
                _periodicTimer = new PeriodicTimer(_options.TryDequeuePeriod);

                _ = StartDequeueTimerAsync(_periodicTimer);
            }
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
                return await AcquireAsyncCoreInternal(cancellationToken);
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

        private async ValueTask<RateLimitLease> AcquireAsyncCoreInternal(CancellationToken cancellationToken)
        {
            var leaseContext = new ConcurrencyLeaseContext
            {
                Limit = _options.PermitLimit,
                RequestId = Guid.NewGuid().ToString(),
            };

            var response = await _redisManager.TryAcquireLeaseAsync(leaseContext.RequestId, tryEnqueue: true);

            leaseContext.Count = response.Count;

            if (response.Allowed)
            {
                return new ConcurrencyLease(isAcquired: true, this, leaseContext);
            }

            if (response.Queued)
            {
                Request request = new()
                {
                    CancellationToken = cancellationToken,
                    LeaseContext = leaseContext,
                    TaskCompletionSource = new TaskCompletionSource<RateLimitLease>(),
                };

                if (cancellationToken.CanBeCanceled)
                {
                    request.CancellationTokenRegistration = cancellationToken.Register(static obj =>
                    {
                        // When the request gets canceled
                        var request = (Request)obj!;
                        request.TaskCompletionSource!.TrySetCanceled(request.CancellationToken);

                    }, request);
                }

                _queue.Enqueue(request);

                return await request.TaskCompletionSource.Task;
            }

            return new ConcurrencyLease(isAcquired: false, this, leaseContext);
        }

        private void Release(ConcurrencyLeaseContext leaseContext)
        {
            if (leaseContext.RequestId is null) return;

            _ = _redisManager.ReleaseLeaseAsync(leaseContext.RequestId);
        }

        private async Task StartDequeueTimerAsync(PeriodicTimer periodicTimer)
        {
            while (await periodicTimer.WaitForNextTickAsync())
            {
                await TryDequeueRequestsAsync();
            }
        }

        private async Task TryDequeueRequestsAsync()
        {
            try
            {
                while (_queue.TryPeek(out var request))
                {
                    if (request.TaskCompletionSource!.Task.IsCompleted)
                    {
                        try
                        {
                            // The request was canceled while in the pending queue
                            await _redisManager.ReleaseQueueLeaseAsync(request.LeaseContext!.RequestId!);
                        }
                        finally
                        {
                            await request.CancellationTokenRegistration.DisposeAsync();

                            _queue.TryDequeue(out _);
                        }

                        continue;
                    }

                    var response = await _redisManager.TryAcquireLeaseAsync(request.LeaseContext!.RequestId!);

                    request.LeaseContext.Count = response.Count;

                    if (response.Allowed)
                    {
                        var pendingLease = new ConcurrencyLease(isAcquired: true, this, request.LeaseContext);

                        try
                        {
                            if (request.TaskCompletionSource?.TrySetResult(pendingLease) == false)
                            {
                                // The request was canceled after we acquired the lease
                                await _redisManager.ReleaseLeaseAsync(request.LeaseContext!.RequestId!);
                            }
                        }
                        finally
                        {
                            await request.CancellationTokenRegistration.DisposeAsync();

                            _queue.TryDequeue(out _);
                        }
                    }
                    else
                    {
                        // Try next time
                        break;
                    }
                }
            }
            catch
            {
                // Try next time
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _periodicTimer?.Dispose();

            while (_queue.TryDequeue(out var request))
            {
                request.CancellationTokenRegistration.Dispose();
                request.TaskCompletionSource?.TrySetResult(_failedLease);
            }

            base.Dispose(disposing);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Dispose(true);

            return default;
        }

        private sealed class ConcurrencyLeaseContext
        {
            public string? RequestId { get; init; }

            public long Count { get; set; }

            public long Limit { get; init; }
        }

        private sealed class ConcurrencyLease(
            bool isAcquired,
            RedisConcurrencyRateLimiter<TKey>? limiter,
            ConcurrencyLeaseContext? context)
            : RateLimitLease
        {
            private bool _disposed;

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
                    metadata = context.Limit - context.Count;
                    return true;
                }

                metadata = null;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (context != null)
                {
                    limiter?.Release(context);
                }
            }
        }

        private sealed class Request
        {
            public CancellationToken CancellationToken { get; init; }

            public ConcurrencyLeaseContext? LeaseContext { get; init; }

            public TaskCompletionSource<RateLimitLease>? TaskCompletionSource { get; init; }

            public CancellationTokenRegistration CancellationTokenRegistration { get; set; }
        }
    }
}
