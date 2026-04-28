using System;
using System.Threading;

namespace swagSMB.Security
{
    public static class UnlockRetryGuard
    {
        private const int FailureLimit = 5;
        private const int FailureDelayMs = 1500;

        private static int s_failures;

        public static int FailuresRemaining => Math.Max(0, FailureLimit - s_failures);

        public static bool LimitReached => s_failures >= FailureLimit;

        public static void RegisterFailure()
        {
            Interlocked.Increment(ref s_failures);
            Thread.Sleep(FailureDelayMs);
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref s_failures, 0);
        }
    }
}
