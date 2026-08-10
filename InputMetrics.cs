using System;

namespace KeyboardDiagnostic
{
    public sealed class TypingMetrics
    {
        private readonly object _sync = new object();
        private readonly IMonotonicClock _clock;
        private long? _startedAt;

        public TypingMetrics(IMonotonicClock clock = null)
        {
            _clock = clock ?? StopwatchClock.System;
        }

        public void ObserveCharacterCount(int characterCount)
        {
            lock (_sync)
            {
                if (characterCount <= 0)
                {
                    _startedAt = null;
                }
                else if (!_startedAt.HasValue)
                {
                    _startedAt = _clock.GetTimestamp();
                }
            }
        }

        public int CalculateWordsPerMinute(int characterCount)
        {
            long? startedAt;
            lock (_sync)
            {
                startedAt = _startedAt;
            }

            if (!startedAt.HasValue || characterCount <= 0)
            {
                return 0;
            }

            double elapsedMilliseconds = Math.Max(0, _clock.GetElapsedMilliseconds(startedAt.Value));
            return KeyboardInput.CalculateWordsPerMinute(
                characterCount,
                TimeSpan.FromMilliseconds(elapsedMilliseconds));
        }

        public void Reset()
        {
            lock (_sync)
            {
                _startedAt = null;
            }
        }
    }

    public sealed class KeyRateCounter
    {
        private readonly object _sync = new object();
        private int _pending;
        private int _lastSample;
        private int _peak;

        public int LastSample
        {
            get
            {
                lock (_sync)
                {
                    return _lastSample;
                }
            }
        }

        public int Peak
        {
            get
            {
                lock (_sync)
                {
                    return _peak;
                }
            }
        }

        public void RecordPress()
        {
            lock (_sync)
            {
                _pending++;
            }
        }

        public int Sample()
        {
            lock (_sync)
            {
                _lastSample = _pending;
                _pending = 0;
                _peak = Math.Max(_peak, _lastSample);
                return _lastSample;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _pending = 0;
                _lastSample = 0;
                _peak = 0;
            }
        }
    }
}
