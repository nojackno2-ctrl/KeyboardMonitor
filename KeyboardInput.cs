using System;

namespace KeyboardDiagnostic
{
    public static class KeyboardInput
    {
        private const uint ExtendedKeyFlag = 0x01;

        public static string ParseKey(uint virtualKey, uint scanCode, uint flags)
        {
            bool isExtended = (flags & ExtendedKeyFlag) != 0;

            if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x30 && virtualKey <= 0x39)
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x70 && virtualKey <= 0x7B)
            {
                return "F" + (virtualKey - 0x70 + 1);
            }

            if (virtualKey >= 0x60 && virtualKey <= 0x69)
            {
                return "NUM_" + (virtualKey - 0x60);
            }

            switch (virtualKey)
            {
                case 0x08: return "BACKSPACE";
                case 0x09: return "TAB";
                case 0x0C: return "NUM_5";
                case 0x0D: return isExtended ? "NUM_ENTER" : "ENTER";
                case 0x10: return scanCode == 0x36 ? "SHIFT_R" : "SHIFT_L";
                case 0x11: return isExtended ? "CTRL_R" : "CTRL_L";
                case 0x12: return isExtended ? "ALT_R" : "ALT_L";
                case 0x13: return "PAUSE";
                case 0x14: return "CAPSLOCK";
                case 0x1B: return "ESC";
                case 0x20: return "SPACE";
                case 0x21: return isExtended ? "PGUP" : "NUM_9";
                case 0x22: return isExtended ? "PGDN" : "NUM_3";
                case 0x23: return isExtended ? "END" : "NUM_1";
                case 0x24: return isExtended ? "HOME" : "NUM_7";
                case 0x25: return isExtended ? "←" : "NUM_4";
                case 0x26: return isExtended ? "↑" : "NUM_8";
                case 0x27: return isExtended ? "→" : "NUM_6";
                case 0x28: return isExtended ? "↓" : "NUM_2";
                case 0x2C: return "PRTSC";
                case 0x2D: return isExtended ? "INSERT" : "NUM_0";
                case 0x2E: return isExtended ? "DELETE" : "NUM_.";
                case 0x5B:
                case 0x5C: return "WIN";
                case 0x5D: return "MENU";
                case 0x6A: return "NUM_*";
                case 0x6B: return "NUM_+";
                case 0x6D: return "NUM_-";
                case 0x6E: return "NUM_.";
                case 0x6F: return "NUM_/";
                case 0x90: return "NUMLOCK";
                case 0x91: return "SCROLL";
                case 0xA0: return "SHIFT_L";
                case 0xA1: return "SHIFT_R";
                case 0xA2: return "CTRL_L";
                case 0xA3: return "CTRL_R";
                case 0xA4: return "ALT_L";
                case 0xA5: return "ALT_R";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                default: return null;
            }
        }

        public static int CalculateWordsPerMinute(int characterCount, TimeSpan elapsed)
        {
            if (characterCount <= 0 || elapsed.TotalMinutes < 0.01)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(
                (characterCount / 5.0) / elapsed.TotalMinutes,
                MidpointRounding.AwayFromZero));
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
