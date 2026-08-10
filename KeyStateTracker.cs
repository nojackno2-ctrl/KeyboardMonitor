using System;
using System.Collections.Generic;

namespace KeyboardDiagnostic
{
    public enum TrackedKeyStatus
    {
        Pressed,
        Stuck
    }

    public readonly struct KeyReleaseResult
    {
        public KeyReleaseResult(bool wasPressed, double durationMilliseconds)
        {
            WasPressed = wasPressed;
            DurationMilliseconds = durationMilliseconds;
        }

        public bool WasPressed { get; }

        public double DurationMilliseconds { get; }
    }

    public readonly struct KeyStateSnapshot
    {
        public KeyStateSnapshot(int activeKeyCount, IReadOnlyList<string> stuckKeys)
        {
            ActiveKeyCount = activeKeyCount;
            StuckKeys = stuckKeys;
        }

        public int ActiveKeyCount { get; }

        public IReadOnlyList<string> StuckKeys { get; }
    }

    public sealed class KeyStateTracker
    {
        private sealed class KeyState
        {
            public TrackedKeyStatus Status { get; set; }

            public long PressedAt { get; set; }
        }

        private readonly Dictionary<string, KeyState> _states = new Dictionary<string, KeyState>();
        private readonly object _sync = new object();
        private readonly IMonotonicClock _clock;

        public KeyStateTracker(IMonotonicClock clock = null)
        {
            _clock = clock ?? StopwatchClock.System;
        }

        public bool Press(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
            {
                throw new ArgumentException("Key name is required.", nameof(keyName));
            }

            lock (_sync)
            {
                if (_states.ContainsKey(keyName))
                {
                    return false;
                }

                _states[keyName] = new KeyState
                {
                    Status = TrackedKeyStatus.Pressed,
                    PressedAt = _clock.GetTimestamp()
                };
                return true;
            }
        }

        public KeyReleaseResult Release(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
            {
                throw new ArgumentException("Key name is required.", nameof(keyName));
            }

            lock (_sync)
            {
                if (!_states.TryGetValue(keyName, out KeyState state))
                {
                    return new KeyReleaseResult(false, 0);
                }

                _states.Remove(keyName);
                double durationMilliseconds = Math.Max(0, _clock.GetElapsedMilliseconds(state.PressedAt));
                return new KeyReleaseResult(true, durationMilliseconds);
            }
        }

        public IReadOnlyList<string> MarkStuck(TimeSpan threshold)
        {
            if (threshold <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be positive.");
            }

            List<string> stuckKeys = new List<string>();

            lock (_sync)
            {
                foreach (KeyValuePair<string, KeyState> pair in _states)
                {
                    if (pair.Value.Status == TrackedKeyStatus.Pressed &&
                        _clock.GetElapsedMilliseconds(pair.Value.PressedAt) >= threshold.TotalMilliseconds)
                    {
                        pair.Value.Status = TrackedKeyStatus.Stuck;
                        stuckKeys.Add(pair.Key);
                    }
                }
            }

            return stuckKeys.AsReadOnly();
        }

        public KeyStateSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                List<string> stuckKeys = new List<string>();
                foreach (KeyValuePair<string, KeyState> pair in _states)
                {
                    if (pair.Value.Status == TrackedKeyStatus.Stuck)
                    {
                        stuckKeys.Add(pair.Key);
                    }
                }

                return new KeyStateSnapshot(_states.Count, stuckKeys.AsReadOnly());
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _states.Clear();
            }
        }
    }
}
