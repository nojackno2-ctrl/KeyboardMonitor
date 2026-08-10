using System;
using System.Diagnostics;

namespace KeyboardDiagnostic
{
    public interface IMonotonicClock
    {
        long GetTimestamp();

        double GetElapsedMilliseconds(long startTimestamp);
    }

    public sealed class StopwatchClock : IMonotonicClock
    {
        private static readonly StopwatchClock _system = new StopwatchClock();

        public static StopwatchClock System => _system;

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public double GetElapsedMilliseconds(long startTimestamp)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            return elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
