﻿using Distributed.RateLimit.Redis.Concurrency;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Distributed.RateLimit.Redis
{
    public class RedisTokenBucketRateLimiter<TKey> : RateLimiter
    {
        private readonly RedisTokenBucketManager _redisManager;
        private readonly RedisTokenBucketRateLimiterOptions _options;

        private readonly TokenBucketLease FailedLease = new(isAcquired: false, null);

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
                throw new ArgumentException(string.Format("{0} must be set to a value greater than 0.", nameof(options.TokenLimit)), nameof(options));
            }
            if (options.TokensPerPeriod <= 0)
            {
                throw new ArgumentException(string.Format("{0} must be set to a value greater than 0.", nameof(options.TokensPerPeriod)), nameof(options));
            }
            if (options.ReplenishmentPeriod <= TimeSpan.Zero)
            {
                throw new ArgumentException(string.Format("{0} must be set to a value greater than TimeSpan.Zero.", nameof(options.ReplenishmentPeriod)), nameof(options));
            }
            
            if (options.ConnectionMultiplexerFactory is null)
            {
                throw new ArgumentException(string.Format("{0} must not be null.", nameof(options.ConnectionMultiplexerFactory)), nameof(options));
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
                throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, string.Format("{0} permit(s) exceeds the permit limit of {1}.", permitCount, _options.TokenLimit));
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
            // https://github.com/cristipufu/aspnetcore-redis-rate-limiting/issues/66
            return FailedLease;
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

            public long Limit { get; set; }

            public int RetryAfter { get; set; }

            public bool Allowed { get; set; }
        }

        private sealed class TokenBucketLease : RateLimitLease
        {
            private static readonly string[] s_allMetadataNames = new[] { RateLimitMetadataName.Limit.Name, RateLimitMetadataName.Remaining.Name };

            private readonly TokenBucketLeaseContext? _context;

            public TokenBucketLease(bool isAcquired, TokenBucketLeaseContext? context)
            {
                IsAcquired = isAcquired;
                _context = context;
            }

            public override bool IsAcquired { get; }

            public override IEnumerable<string> MetadataNames => s_allMetadataNames;

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                if (metadataName == RateLimitMetadataName.Limit.Name && _context is not null)
                {
                    metadata = _context.Limit.ToString();
                    return true;
                }

                if (metadataName == RateLimitMetadataName.Remaining.Name && _context is not null)
                {
                    metadata = _context.Count;
                    return true;
                }

                if (metadataName == RateLimitMetadataName.RetryAfter.Name && _context is not null)
                {
                    metadata = _context.RetryAfter;
                    return true;
                }

                metadata = default;
                return false;
            }
        }
    }
}