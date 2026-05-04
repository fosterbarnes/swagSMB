using System;
using System.Collections.Concurrent;
using System.Threading;

namespace swagSMB.Security
{
    public static class UnlockRetryGuard
    {
        public const string ScopeUnlock = "unlock";
        public const string ScopeVerify = "verify";

        private const int FailureLimit = 5;
        private const int FailureDelayMs = 1500;

        private sealed class ScopeState
        {
            public int Failures;
            public long CooldownUntilUtcTicks;
        }

        private static readonly ConcurrentDictionary<string, ScopeState> s_states =
            new ConcurrentDictionary<string, ScopeState>(StringComparer.Ordinal);

        private static ScopeState GetState(string scope)
        {
            return s_states.GetOrAdd(scope ?? string.Empty, _ => new ScopeState());
        }

        public static int FailuresRemaining(string scope)
        {
            return Math.Max(0, FailureLimit - Volatile.Read(ref GetState(scope).Failures));
        }

        public static bool IsLimitReached(string scope)
        {
            return Volatile.Read(ref GetState(scope).Failures) >= FailureLimit;
        }

        public static void RegisterFailure(string scope)
        {
            ScopeState state = GetState(scope);
            Interlocked.Increment(ref state.Failures);
            Interlocked.Exchange(
                ref state.CooldownUntilUtcTicks,
                DateTime.UtcNow.AddMilliseconds(FailureDelayMs).Ticks);
        }

        public static void Reset(string scope)
        {
            ScopeState state = GetState(scope);
            Interlocked.Exchange(ref state.Failures, 0);
            Interlocked.Exchange(ref state.CooldownUntilUtcTicks, 0);
        }

        public static TimeSpan RemainingCooldown(string scope)
        {
            long until = Interlocked.Read(ref GetState(scope).CooldownUntilUtcTicks);
            long delta = until - DateTime.UtcNow.Ticks;
            return delta <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(delta);
        }
    }
}
