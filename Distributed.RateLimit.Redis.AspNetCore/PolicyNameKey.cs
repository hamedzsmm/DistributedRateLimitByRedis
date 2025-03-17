namespace Distributed.RateLimit.Redis.AspNetCore
{
    internal sealed class PolicyNameKey
    {
        public required string PolicyName { get; init; }
        public required string UniqueKey { get; init; }

        public override bool Equals(object? obj)
        {
            if (obj is PolicyNameKey key)
            {
                return PolicyName == key.PolicyName &&
                       UniqueKey == key.UniqueKey;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return $"{PolicyName}-{UniqueKey}".GetHashCode();
        }

        public override string ToString()
        {
            return $"{PolicyName}-{UniqueKey}";
        }
    }
}
