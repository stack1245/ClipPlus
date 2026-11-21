using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardExtensionClean
{
    public enum PinState { Unknown, Unpinned, Pinned }

    public static class ActionRules
    {
        public const int DoublePressMilliseconds=200;
        public static bool ValidPinBounds(double x,double y,double width,double height)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y)
                && !double.IsNaN(width) && !double.IsInfinity(width) && !double.IsNaN(height) && !double.IsInfinity(height)
                && width>0 && height>0 && width<=256 && height<=128;
        }
        public static bool SameControlRoot(long expected,long actual)
        {
            return expected!=0 && actual==expected;
        }
        public static long Now
        {
            get
            {
                long ticks = Stopwatch.GetTimestamp();
                long rate = Stopwatch.Frequency;
                return (ticks / rate) * 1000L + (ticks % rate) * 1000L / rate;
            }
        }

        public static bool Fresh(long timestamp, long now, int maxMs)
        {
            return timestamp >= 0 && maxMs >= 0 && now >= timestamp
                && now - timestamp <= maxMs;
        }

        private static bool MatchingItem(string requestedId, string currentId)
        {
            return !string.IsNullOrWhiteSpace(requestedId)
                && !string.IsNullOrWhiteSpace(currentId)
                && string.Equals(requestedId, currentId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanDelete(string requestedId, string currentId, PinState pin)
        {
            return pin == PinState.Unpinned && MatchingItem(requestedId, currentId);
        }

        public static bool CanToggle(string requestedId, string currentId, PinState pin)
        {
            return (pin == PinState.Unpinned || pin == PinState.Pinned)
                && MatchingItem(requestedId, currentId);
        }

        public static async Task<T> Within<T>(Task<T> task, int milliseconds)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if (task.IsCompleted) return await task.ConfigureAwait(false);

            using (var timerCancellation = new CancellationTokenSource())
            {
                Task timer = Task.Delay(milliseconds, timerCancellation.Token);
                Task first = await Task.WhenAny(task, timer).ConfigureAwait(false);
                if (ReferenceEquals(first, task))
                {
                    timerCancellation.Cancel();
                    return await task.ConfigureAwait(false);
                }

                // A timed-out read may still complete. Its result is never returned
                // to the action caller; a later fault is observed without mutation.
                _ = task.ContinueWith(late => { var error = late.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new TimeoutException("Clipboard read exceeded its time limit.");
            }
        }
    }
}
