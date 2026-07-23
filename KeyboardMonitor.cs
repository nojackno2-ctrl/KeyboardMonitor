using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardDiagnostic
{
    public class KeyboardMonitorForm : Form
    {
        // 三種不同的鍵盤佈局定義
        private static readonly string[][] MAIN_LAYOUT = new string[][]
        {
            new string[] { "ESC", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" },
            new string[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "BACKSPACE" },
            new string[] { "TAB", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" },
            new string[] { "CAPSLOCK", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "ENTER" },
            new string[] { "SHIFT_L", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "SHIFT_R" },
            new string[] { "CTRL_L", "WIN", "ALT_L", "SPACE", "ALT_R", "MENU", "CTRL_R" }
        };

        private static readonly string[][] NAV_LAYOUT = new string[][]
        {
            new string[] { "PRTSC", "SCROLL", "PAUSE" },
            new string[] { "INSERT", "HOME", "PGUP" },
            new string[] { "DELETE", "END", "PGDN" },
            new string[] { "", "", "" },
            new string[] { "", "↑", "" },
            new string[] { "←", "↓", "→" }
        };

        private static readonly string[][] NUM_LAYOUT = new string[][]
        {
            new string[] { "", "", "", "" },
            new string[] { "NUMLOCK", "NUM_/", "NUM_*", "NUM_-" },
            new string[] { "NUM_7", "NUM_8", "NUM_9", "NUM_+" },
            new string[] { "NUM_4", "NUM_5", "NUM_6", "" }, // NUM_+ 佔用
            new string[] { "NUM_1", "NUM_2", "NUM_3", "NUM_ENTER" },
            new string[] { "NUM_0", "", "NUM_.", "" } // NUM_0 與 NUM_ENTER 佔用
        };

        // 按鍵狀態資訊類別
        private sealed class KeyStateInfo
        {
            public string Status { get; set; }
            public DateTime PressedTime { get; set; }
        }

        // 狀態字典與執行緒安全鎖
        private readonly Dictionary<string, KeyStateInfo> _keyStates = new Dictionary<string, KeyStateInfo>();
        private readonly object _stateLock = new object();

        // 記錄按鍵按下時間戳，用以計算按壓延遲 (持續時間)
        private readonly Dictionary<string, DateTime> _keyPressStartTimes = new Dictionary<string, DateTime>();

        // UI 元件字典
        private readonly Dictionary<string, KeyControl> _keyControls = new Dictionary<string, KeyControl>();

        // 頂部狀態面板元件與佈局容器
        private Label _statusLight;
        private Label _statusText;
        private Label _countLabel;
        private Label _bottomTips;
        private ComboBox _keyboardTypeSelector;
        private TableLayoutPanel _keyboardContainer;
        private Timer _watchdogTimer;

        // --- 優化新增的 UI 元件 ---
        private TableLayoutPanel _bottomPanelContainer;
        private MouseTesterControl _mouseTester;
        private TextBox _typeTextBox;
        private Label _wpmLabel;
        private Label _kpsLabel;
        private Label _maxKpsLabel;
        private Label _latencyLabel;
        private ListBox _logListBox;
        // 打字速度與按鍵計數統計
        private DateTime? _typingStartTime;
        private readonly KeyRateCounter _keyRateCounter = new KeyRateCounter();
        private double _lastLatencyMs;
        private Timer _kpsTimer;

        // Windows Hook API 宣告
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetKeyboardHook(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetMouseHook(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        private IntPtr _hookID = IntPtr.Zero;
        private IntPtr _mouseHookID = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private LowLevelMouseProc _mouseProc;

        [STAThread]
        public static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new KeyboardMonitorForm())
            {
                Application.Run(form);
            }
        }

        public KeyboardMonitorForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.DoubleBuffered = true;
            // 設定視窗基本樣式
            this.Text = "Windows 11 鍵盤與滑鼠診斷工具";
            this.Width = 1450;
            this.Height = 810;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(18, 18, 22);

            // 1. 頂部控制面板
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(18, 18, 22),
                Padding = new Padding(25, 10, 25, 10)
            };

            Label titleLabel = new Label
            {
                Text = "KEYBOARD & MOUSE DIAGNOSTIC",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 245, 250),
                AutoSize = true,
                Location = new Point(25, 18)
            };
            topPanel.Controls.Add(titleLabel);

            // 狀態列容器
            FlowLayoutPanel statusPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
                Dock = DockStyle.Right,
                Padding = new Padding(0, 15, 0, 0)
            };

            _statusLight = new Label
            {
                Width = 14,
                Height = 14,
                BackColor = Color.FromArgb(0x10, 0xB9, 0x81), // 精緻綠
                Margin = new Padding(5, 6, 5, 0)
            };
            // 繪製圓形指示燈
            _statusLight.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(_statusLight.BackColor))
                {
                    e.Graphics.FillEllipse(b, 0, 0, _statusLight.Width - 1, _statusLight.Height - 1);
                }
            };
            statusPanel.Controls.Add(_statusLight);

            _statusText = new Label
            {
                Text = "系統偵測中 - 正常",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x10, 0xB9, 0x81),
                AutoSize = true,
                Margin = new Padding(5, 4, 15, 0)
            };
            statusPanel.Controls.Add(_statusText);

            _countLabel = new Label
            {
                Text = "當前按下鍵數: 0",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 245, 250),
                AutoSize = true,
                Margin = new Padding(5, 4, 15, 0)
            };
            statusPanel.Controls.Add(_countLabel);

            // 鍵盤種類下拉選單
            _keyboardTypeSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(38, 38, 44),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 28),
                Margin = new Padding(5, 1, 15, 0),
                Cursor = Cursors.Hand,
                TabStop = false,
                AccessibleName = "鍵盤配置"
            };
            _keyboardTypeSelector.Items.AddRange(new object[] { "100% 全尺寸鍵盤", "80% TKL 鍵盤", "60% 緊湊型鍵盤" });
            _keyboardTypeSelector.SelectedIndex = 0;
            _keyboardTypeSelector.SelectedIndexChanged += KeyboardTypeSelector_SelectedIndexChanged;
            statusPanel.Controls.Add(_keyboardTypeSelector);

            // 清除按鈕
            Button resetBtn = new Button
            {
                Text = "清除重設",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(85, 28),
                Margin = new Padding(5, 0, 5, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            resetBtn.FlatAppearance.BorderSize = 0;
            resetBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 99, 235);
            resetBtn.Click += (s, e) => ResetAll();
            statusPanel.Controls.Add(resetBtn);

            topPanel.Controls.Add(statusPanel);
            this.Controls.Add(topPanel);

            // 2. 鍵盤主體卡片面板容器
            _keyboardContainer = new TableLayoutPanel
            {
                BackColor = Color.FromArgb(24, 24, 28),
                Padding = new Padding(15),
                Location = new Point(25, 75),
                Size = new Size(1385, 380),
                RowCount = 1,
                ColumnCount = 3
            };
            // 繪製容器邊框
            _keyboardContainer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(45, 45, 52), 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, _keyboardContainer.Width - 1, _keyboardContainer.Height - 1);
                }
            };
            this.Controls.Add(_keyboardContainer);

            // 3. 下方三大特色版面容器
            _bottomPanelContainer = new TableLayoutPanel
            {
                Location = new Point(25, 470),
                Size = new Size(1385, 235),
                RowCount = 1,
                ColumnCount = 3,
                BackColor = Color.Transparent
            };
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f)); // 滑鼠診斷
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f)); // 打字測試
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f)); // 即時日誌
            this.Controls.Add(_bottomPanelContainer);

            // --- 3.1 滑鼠診斷區 ---
            _mouseTester = new MouseTesterControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0)
            };
            _bottomPanelContainer.Controls.Add(_mouseTester, 0, 0);

            // --- 3.2 打字測試區面板 ---
            Panel typePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 28),
                Padding = new Padding(15),
                Margin = new Padding(5, 0, 5, 0)
            };
            typePanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(45, 45, 52), 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, typePanel.Width - 1, typePanel.Height - 1);
            };

            Label typeTitle = new Label
            {
                Text = "打字與延遲測試 (TYPING & LATENCY TEST)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 210),
                Location = new Point(15, 10),
                AutoSize = true
            };
            typePanel.Controls.Add(typeTitle);

            _typeTextBox = new TextBox
            {
                Multiline = true,
                BackColor = Color.FromArgb(18, 18, 22),
                ForeColor = Color.FromArgb(245, 245, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10.5f),
                Location = new Point(15, 35),
                Size = new Size(625, 95),
                TabStop = false
            };
            _typeTextBox.TextChanged += TypeTextBox_TextChanged;
            _typeTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _typeTextBox.Clear();
                    e.SuppressKeyPress = true;
                }
            };
            typePanel.Controls.Add(_typeTextBox);

            // 速度指標容器
            FlowLayoutPanel speedIndicators = new FlowLayoutPanel
            {
                Location = new Point(15, 140),
                Size = new Size(625, 80),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _wpmLabel = CreateIndicatorLabel("WPM", () => GetWPMString(), Color.FromArgb(0, 242, 254));
            _kpsLabel = CreateIndicatorLabel("當前 KPS", () => GetKpsString(false), Color.FromArgb(16, 185, 129));
            _maxKpsLabel = CreateIndicatorLabel("最高 KPS", () => GetKpsString(true), Color.FromArgb(245, 158, 11));
            _latencyLabel = CreateIndicatorLabel("按鍵持續時間", () => GetLastLatencyString(), Color.FromArgb(167, 139, 250));

            speedIndicators.Controls.Add(_wpmLabel);
            speedIndicators.Controls.Add(_kpsLabel);
            speedIndicators.Controls.Add(_maxKpsLabel);
            speedIndicators.Controls.Add(_latencyLabel);
            typePanel.Controls.Add(speedIndicators);
            _bottomPanelContainer.Controls.Add(typePanel, 1, 0);

            // --- 3.3 即時日誌面板 ---
            Panel logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 28),
                Padding = new Padding(15),
                Margin = new Padding(10, 0, 0, 0)
            };
            logPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(45, 45, 52), 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, logPanel.Width - 1, logPanel.Height - 1);
            };

            Label logTitle = new Label
            {
                Text = "實時按鍵日誌 (LIVE EVENT LOG)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 210),
                Location = new Point(15, 10),
                AutoSize = true
            };
            logPanel.Controls.Add(logTitle);

            _logListBox = new ListBox
            {
                BackColor = Color.FromArgb(18, 18, 22),
                ForeColor = Color.FromArgb(220, 220, 230),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                Location = new Point(15, 35),
                Size = new Size(385, 180),
                TabStop = false
            };
            _logListBox.DrawItem += LogListBox_DrawItem;
            logPanel.Controls.Add(_logListBox);
            _bottomPanelContainer.Controls.Add(logPanel, 2, 0);

            // 4. 底部提示資訊
            _bottomTips = new Label
            {
                Text = "亮藍色代表目前按下 | 暗青色代表已測試過 | 紅色代表卡鍵 (>2秒) | 支援滑鼠點擊與滾輪檢測 | 按 ESC 可清空打字測試區",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(130, 130, 140),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(25, 712),
                Size = new Size(1385, 25)
            };
            this.Controls.Add(_bottomTips);

            // 5. 初始化與載入預設鍵盤
            UpdateKeyboardLayout("100%");

            // 6. 初始化卡鍵偵測看門狗計時器 (200ms)
            _watchdogTimer = new Timer();
            _watchdogTimer.Interval = 200;
            _watchdogTimer.Tick += StuckWatchdog_Tick;
            _watchdogTimer.Start();

            // 7. 初始化 KPS 每秒統計 Timer
            _kpsTimer = new Timer();
            _kpsTimer.Interval = 1000;
            _kpsTimer.Tick += KpsTimer_Tick;
            _kpsTimer.Start();

        }

        private static Label CreateIndicatorLabel(string name, Func<string> getValue, Color valueColor)
        {
            Label lbl = new Label
            {
                Size = new Size(150, 70),
                BackColor = Color.FromArgb(18, 18, 22),
                Margin = new Padding(0, 0, 5, 0),
                Padding = new Padding(8)
            };
            lbl.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                // 畫邊框
                using (Pen p = new Pen(Color.FromArgb(40, 40, 48), 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, lbl.Width - 1, lbl.Height - 1);
                }
                // 畫指標名稱
                using (Font nameFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(e.Graphics, name, nameFont, new Rectangle(8, 6, lbl.Width - 16, 20), Color.FromArgb(130, 130, 140));
                }
                // 畫動態讀取的數值
                string valueText = getValue();
                using (Font valFont = new Font("Segoe UI", 13f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, valueText, valFont, new Rectangle(8, 26, lbl.Width - 16, 35), valueColor, TextFormatFlags.VerticalCenter);
                }
            };
            return lbl;
        }

        private static float GetKeySpan(string key)
        {
            key = key.ToUpperInvariant();
            if (key == "SPACE") return 12f;
            if (key == "SHIFT_L" || key == "SHIFT_R") return 5f;
            if (key == "BACKSPACE" || key == "ENTER" || key == "CAPSLOCK") return 4f;
            if (key == "TAB" || key == "CTRL_L" || key == "WIN" || key == "ALT_L" || key == "ALT_R" || key == "MENU" || key == "CTRL_R" || key == "\\") return 3f;
            return 2f;
        }

        // 安裝與解除低階鉤子
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _proc = HookCallback;
            _mouseProc = MouseHookCallback;
            _hookID = SetHook(_proc);
            _mouseHookID = SetHook(_mouseProc);

            if (_hookID == IntPtr.Zero || _mouseHookID == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                ReleaseHooks();
                MessageBox.Show(
                    this,
                    $"無法安裝全域輸入監控 Hook（Win32 錯誤 {errorCode}）。程式將關閉。",
                    "初始化失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                BeginInvoke(new Action(Close));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ReleaseHooks();
            _watchdogTimer?.Stop();
            _kpsTimer?.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            ReleaseHooks();
            if (disposing)
            {
                _watchdogTimer?.Dispose();
                _kpsTimer?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                return SetKeyboardHook(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                return SetMouseHook(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void ReleaseHooks()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }

            if (_mouseHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookID);
                _mouseHookID = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                long message = (long)wParam;
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    string keyName = KeyboardInput.ParseKey(kb.vkCode, kb.scanCode, kb.flags);
                    if (keyName != null)
                    {
                        OnKeyDownEvent(keyName);
                    }
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    string keyName = KeyboardInput.ParseKey(kb.vkCode, kb.scanCode, kb.flags);
                    if (keyName != null)
                    {
                        OnKeyUpEvent(keyName);
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = unchecked((int)(long)wParam);
                MSLLHOOKSTRUCT mouseData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                switch (message)
                {
                    case WM_LBUTTONDOWN:
                        OnMouseChanged("L_BUTTON", true);
                        break;
                    case WM_LBUTTONUP:
                        OnMouseChanged("L_BUTTON", false);
                        break;
                    case WM_RBUTTONDOWN:
                        OnMouseChanged("R_BUTTON", true);
                        break;
                    case WM_RBUTTONUP:
                        OnMouseChanged("R_BUTTON", false);
                        break;
                    case WM_MBUTTONDOWN:
                        OnMouseChanged("M_BUTTON", true);
                        break;
                    case WM_MBUTTONUP:
                        OnMouseChanged("M_BUTTON", false);
                        break;
                    case WM_XBUTTONDOWN:
                    case WM_XBUTTONUP:
                        int xButton = (int)((mouseData.mouseData >> 16) & 0xFFFF);
                        OnMouseChanged(
                            xButton == 1 ? "X1_BUTTON" : "X2_BUTTON",
                            message == WM_XBUTTONDOWN);
                        break;
                    case WM_MOUSEWHEEL:
                        short delta = unchecked((short)((mouseData.mouseData >> 16) & 0xFFFF));
                        OnMouseWheelScrolled(delta);
                        break;
                }
            }

            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private void OnKeyDownEvent(string keyName)
        {
            bool isNewPress = false;
            lock (_stateLock)
            {
                if (!_keyStates.TryGetValue(keyName, out KeyStateInfo state) || state.Status != "pressed")
                {
                    _keyStates[keyName] = new KeyStateInfo
                    {
                        Status = "pressed",
                        PressedTime = DateTime.Now
                    };
                    _keyPressStartTimes[keyName] = DateTime.Now;
                    isNewPress = true;
                    _keyRateCounter.RecordPress();
                }
            }

            if (isNewPress)
            {
                UpdateKeyUI(keyName, KeyControl.KeyState.Pressed);
                AddLog($"[按下] {keyName}");
            }
        }

        private void OnKeyUpEvent(string keyName)
        {
            double durationMs = 0;
            bool isReleased = false;

            lock (_stateLock)
            {
                isReleased = _keyStates.Remove(keyName);

                if (_keyPressStartTimes.TryGetValue(keyName, out DateTime pressTime))
                {
                    durationMs = (DateTime.Now - pressTime).TotalMilliseconds;
                    _keyPressStartTimes.Remove(keyName);
                }
            }

            if (isReleased)
            {
                UpdateKeyUI(keyName, KeyControl.KeyState.Tested);

                string durationStr = durationMs > 0 ? $" (持續 {durationMs:F0}ms)" : "";
                AddLog($"[放開] {keyName}{durationStr}");

                if (durationMs > 0)
                {
                    _lastLatencyMs = durationMs;
                    UpdateLatencyUI(durationMs);
                }
            }
        }

        private void StuckWatchdog_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            List<string> stuckKeys = new List<string>();

            lock (_stateLock)
            {
                foreach (var pair in _keyStates)
                {
                    if (pair.Value.Status == "pressed" && (now - pair.Value.PressedTime).TotalSeconds > 2.0)
                    {
                        stuckKeys.Add(pair.Key);
                    }
                }
            }

            foreach (var keyName in stuckKeys)
            {
                UpdateKeyUI(keyName, KeyControl.KeyState.Stuck);
                lock (_stateLock)
                {
                    if (_keyStates.TryGetValue(keyName, out KeyStateInfo state) && state.Status == "pressed")
                    {
                        state.Status = "stuck";
                        AddLog($"[卡鍵] {keyName} (已按住 >2秒!)");
                    }
                }
            }
        }

        private void KpsTimer_Tick(object sender, EventArgs e)
        {
            int kps = _keyRateCounter.Sample();

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateKpsUI(kps)));
            }
            else
            {
                UpdateKpsUI(kps);
            }
        }

        private void UpdateKpsUI(int kps)
        {
            _kpsLabel.Invalidate();
            _maxKpsLabel.Invalidate();
        }

        private void UpdateLatencyUI(double durationMs)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateLatencyUI(durationMs)));
                return;
            }
            _latencyLabel.Invalidate();
        }

        private void TypeTextBox_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_typeTextBox.Text))
            {
                _typingStartTime = null;
                _wpmLabel.Invalidate();
                return;
            }

            if (_typingStartTime == null)
            {
                _typingStartTime = DateTime.Now;
            }

            _wpmLabel.Invalidate();
        }

        private void UpdateKeyUI(string keyName, KeyControl.KeyState state)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateKeyUI(keyName, state)));
                return;
            }

            if (keyName != null && _keyControls.TryGetValue(keyName, out KeyControl ctrl))
            {
                ctrl.State = state;
            }

            int pressedCount = 0;
            List<string> stuckKeys = new List<string>();
            DateTime now = DateTime.Now;

            lock (_stateLock)
            {
                pressedCount = _keyStates.Count(x => x.Value.Status == "pressed" || x.Value.Status == "stuck");
                foreach (var pair in _keyStates)
                {
                    if ((now - pair.Value.PressedTime).TotalSeconds > 2.0)
                    {
                        stuckKeys.Add(pair.Key);
                    }
                }
            }

            _countLabel.Text = $"當前按下鍵數: {pressedCount}";

            if (stuckKeys.Count > 0)
            {
                _statusLight.BackColor = Color.FromArgb(0xEF, 0x44, 0x44);
                _statusText.Text = $"警告：偵測到卡鍵！({string.Join(", ", stuckKeys)})";
                _statusText.ForeColor = Color.FromArgb(0xEF, 0x44, 0x44);
            }
            else
            {
                if (pressedCount > 0)
                {
                    _statusLight.BackColor = Color.FromArgb(0x10, 0xB9, 0x81);
                    _statusText.Text = $"偵測中 - 同時按下 {pressedCount} 個鍵";
                    _statusText.ForeColor = Color.FromArgb(0x10, 0xB9, 0x81);
                }
                else
                {
                    _statusLight.BackColor = Color.FromArgb(0x10, 0xB9, 0x81);
                    _statusText.Text = "系統偵測中 - 正常";
                    _statusText.ForeColor = Color.FromArgb(0x10, 0xB9, 0x81);
                }
            }
            _statusLight.Invalidate();
        }

        private void ResetAll()
        {
            lock (_stateLock)
            {
                _keyStates.Clear();
                _keyPressStartTimes.Clear();
            }
            _keyRateCounter.Reset();

            _typingStartTime = null;
            _lastLatencyMs = 0;

            foreach (var ctrl in _keyControls.Values)
            {
                ctrl.State = KeyControl.KeyState.Untested;
            }

            _typeTextBox.Clear();
            _logListBox.Items.Clear();
            _mouseTester.ResetMouse();

            _wpmLabel.Invalidate();
            _kpsLabel.Invalidate();
            _maxKpsLabel.Invalidate();
            _latencyLabel.Invalidate();

            UpdateKeyUI(null, KeyControl.KeyState.Untested);
            AddLog("--- 所有診斷狀態已重置 ---");
        }

        private void AddLog(string msg)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => AddLog(msg)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            _logListBox.Items.Add($"[{timestamp}] {msg}");
            _logListBox.TopIndex = _logListBox.Items.Count - 1;

            if (_logListBox.Items.Count > 100)
            {
                _logListBox.Items.RemoveAt(0);
            }
        }

        public void OnMouseChanged(string btnName, bool isPressed)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMouseChanged(btnName, isPressed)));
                return;
            }

            if (btnName == "L_BUTTON") _mouseTester.LPressed = isPressed;
            else if (btnName == "R_BUTTON") _mouseTester.RPressed = isPressed;
            else if (btnName == "M_BUTTON") _mouseTester.MPressed = isPressed;
            else if (btnName == "X1_BUTTON") _mouseTester.X1Pressed = isPressed;
            else if (btnName == "X2_BUTTON") _mouseTester.X2Pressed = isPressed;

            _mouseTester.Invalidate();

            string status = isPressed ? "按下" : "放開";
            AddLog($"[滑鼠] {btnName} {status}");
        }

        public void OnMouseWheelScrolled(int delta)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMouseWheelScrolled(delta)));
                return;
            }

            _mouseTester.RegisterScroll(delta);
            string dir = delta > 0 ? "向上" : "向下";
            AddLog($"[滑鼠] 滾輪 {dir}");
        }

        private void KeyboardTypeSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selected = _keyboardTypeSelector.SelectedItem.ToString();
            string type = "100%";
            if (selected.Contains("80%", StringComparison.Ordinal)) type = "80%";
            else if (selected.Contains("60%", StringComparison.Ordinal)) type = "60%";

            UpdateKeyboardLayout(type);
        }

        private void UpdateKeyboardLayout(string type)
        {
            ResetAll();

            _keyboardContainer.Width = this.ClientSize.Width - 50;
            _bottomPanelContainer.Width = this.ClientSize.Width - 50;
            _bottomTips.Width = this.ClientSize.Width - 50;

            _keyboardContainer.ColumnStyles.Clear();
            _keyboardContainer.Controls.Clear();
            _keyControls.Clear();

            if (type == "60%")
            {
                _keyboardContainer.ColumnCount = 1;
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            }
            else if (type == "80%")
            {
                _keyboardContainer.ColumnCount = 2;
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78f));
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            }
            else
            {
                _keyboardContainer.ColumnCount = 3;
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17f));
                _keyboardContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
            }

            var main = CreateMainKeyboard();
            _keyboardContainer.Controls.Add(main, 0, 0);

            if (type == "80%" || type == "100%")
            {
                var nav = CreateNavKeyboard();
                _keyboardContainer.Controls.Add(nav, 1, 0);
            }

            if (type == "100%")
            {
                var num = CreateNumKeyboard();
                _keyboardContainer.Controls.Add(num, 2, 0);
            }
        }

        private TableLayoutPanel CreateMainKeyboard()
        {
            TableLayoutPanel mainCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = MAIN_LAYOUT.Length,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };

            for (int r = 0; r < MAIN_LAYOUT.Length; r++)
            {
                mainCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / MAIN_LAYOUT.Length));
            }

            for (int r = 0; r < MAIN_LAYOUT.Length; r++)
            {
                string[] rowKeys = MAIN_LAYOUT[r];
                TableLayoutPanel rowPanel = new TableLayoutPanel
                {
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 4, 0, 4),
                    RowCount = 1,
                    ColumnCount = rowKeys.Length
                };
                rowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

                float totalSpan = rowKeys.Sum(k => GetKeySpan(k));

                for (int c = 0; c < rowKeys.Length; c++)
                {
                    string key = rowKeys[c];
                    float span = GetKeySpan(key);
                    float percent = (span / totalSpan) * 100f;
                    rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));

                    KeyControl keyCtrl = new KeyControl
                    {
                        KeyText = key,
                        AccessibleName = key,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(3, 2, 3, 2)
                    };

                    rowPanel.Controls.Add(keyCtrl, c, 0);
                    _keyControls[key] = keyCtrl;
                }

                mainCard.Controls.Add(rowPanel, 0, r);
            }

            return mainCard;
        }

        private TableLayoutPanel CreateNavKeyboard()
        {
            TableLayoutPanel navCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 0, 0),
                RowCount = NAV_LAYOUT.Length,
                ColumnCount = NAV_LAYOUT[0].Length,
                BackColor = Color.Transparent
            };

            for (int r = 0; r < NAV_LAYOUT.Length; r++)
            {
                navCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / NAV_LAYOUT.Length));
            }
            for (int c = 0; c < NAV_LAYOUT[0].Length; c++)
            {
                navCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / NAV_LAYOUT[0].Length));
            }

            for (int r = 0; r < NAV_LAYOUT.Length; r++)
            {
                for (int c = 0; c < NAV_LAYOUT[r].Length; c++)
                {
                    string key = NAV_LAYOUT[r][c];
                    if (string.IsNullOrEmpty(key)) continue;

                    KeyControl keyCtrl = new KeyControl
                    {
                        KeyText = key,
                        AccessibleName = key,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(3, 4, 3, 4)
                    };

                    navCard.Controls.Add(keyCtrl, c, r);
                    _keyControls[key] = keyCtrl;
                }
            }

            return navCard;
        }

        private TableLayoutPanel CreateNumKeyboard()
        {
            TableLayoutPanel numCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(15, 0, 0, 0),
                RowCount = NUM_LAYOUT.Length,
                ColumnCount = NUM_LAYOUT[0].Length,
                BackColor = Color.Transparent
            };

            for (int r = 0; r < NUM_LAYOUT.Length; r++)
            {
                numCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / NUM_LAYOUT.Length));
            }
            for (int c = 0; c < NUM_LAYOUT[0].Length; c++)
            {
                numCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / NUM_LAYOUT[0].Length));
            }

            bool[,] occupied = new bool[NUM_LAYOUT.Length, NUM_LAYOUT[0].Length];

            for (int r = 0; r < NUM_LAYOUT.Length; r++)
            {
                for (int c = 0; c < NUM_LAYOUT[r].Length; c++)
                {
                    if (occupied[r, c]) continue;

                    string key = NUM_LAYOUT[r][c];
                    if (string.IsNullOrEmpty(key)) continue;

                    int rowSpan = 1;
                    int colSpan = 1;

                    if (key == "NUM_+")
                    {
                        rowSpan = 2;
                        occupied[r, c] = true;
                        occupied[r + 1, c] = true;
                    }
                    else if (key == "NUM_ENTER")
                    {
                        rowSpan = 2;
                        occupied[r, c] = true;
                        occupied[r + 1, c] = true;
                    }
                    else if (key == "NUM_0")
                    {
                        colSpan = 2;
                        occupied[r, c] = true;
                        occupied[r, c + 1] = true;
                    }
                    else
                    {
                        occupied[r, c] = true;
                    }

                    KeyControl keyCtrl = new KeyControl
                    {
                        KeyText = key,
                        AccessibleName = key,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(3, 4, 3, 4)
                    };

                    numCard.Controls.Add(keyCtrl, c, r);
                    if (rowSpan > 1) numCard.SetRowSpan(keyCtrl, rowSpan);
                    if (colSpan > 1) numCard.SetColumnSpan(keyCtrl, colSpan);

                    _keyControls[key] = keyCtrl;
                }
            }

            return numCard;
        }

        private void LogListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            string text = _logListBox.Items[e.Index].ToString();
            Color textColor = Color.FromArgb(180, 180, 190);

            if (text.Contains("[按下]", StringComparison.Ordinal))
            {
                textColor = Color.FromArgb(0, 242, 254);
            }
            else if (text.Contains("[放開]", StringComparison.Ordinal))
            {
                textColor = Color.FromArgb(16, 185, 129);
            }
            else if (text.Contains("[卡鍵]", StringComparison.Ordinal))
            {
                textColor = Color.FromArgb(239, 68, 68);
            }
            else if (text.Contains("[滑鼠]", StringComparison.Ordinal))
            {
                textColor = Color.FromArgb(245, 158, 11);
            }

            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                e.Graphics.DrawString(text, e.Font, textBrush, e.Bounds.X + 5, e.Bounds.Y + 2);
            }

            e.DrawFocusRectangle();
        }

        public string GetWPMString()
        {
            if (_typingStartTime == null || string.IsNullOrEmpty(_typeTextBox.Text)) return "0";
            return KeyboardInput.CalculateWordsPerMinute(
                _typeTextBox.Text.Length,
                DateTime.Now - _typingStartTime.Value).ToString(CultureInfo.InvariantCulture);
        }

        public string GetKpsString(bool getMax = false)
        {
            return (getMax ? _keyRateCounter.Peak : _keyRateCounter.LastSample).ToString(CultureInfo.InvariantCulture);
        }

        public string GetLastLatencyString()
        {
            return _lastLatencyMs > 0 ? $"{_lastLatencyMs:F0} ms" : "-- ms";
        }
    }

    public class KeyControl : Control
    {
        public enum KeyState
        {
            Untested,
            Pressed,
            Tested,
            Stuck
        }

        private string _keyText = "";
        private KeyState _state = KeyState.Untested;

        public string KeyText
        {
            get => _keyText;
            set { _keyText = value; Invalidate(); }
        }

        public KeyState State
        {
            get => _state;
            set { _state = value; Invalidate(); }
        }

        public KeyControl()
        {
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RectangleF rect = new RectangleF(1, 1, this.Width - 3, this.Height - 3);
            int cornerRadius = 6;

            Color bgStart, bgEnd, borderColor, textColor;

            switch (_state)
            {
                case KeyState.Untested:
                    bgStart = Color.FromArgb(32, 32, 38);
                    bgEnd = Color.FromArgb(24, 24, 28);
                    borderColor = Color.FromArgb(52, 52, 60);
                    textColor = Color.FromArgb(170, 170, 180);
                    break;
                case KeyState.Pressed:
                    bgStart = Color.FromArgb(0, 242, 254);
                    bgEnd = Color.FromArgb(79, 172, 254);
                    borderColor = Color.FromArgb(0, 190, 255);
                    textColor = Color.White;
                    break;
                case KeyState.Tested:
                    bgStart = Color.FromArgb(16, 45, 50);
                    bgEnd = Color.FromArgb(10, 30, 35);
                    borderColor = Color.FromArgb(14, 116, 144);
                    textColor = Color.FromArgb(34, 211, 238);
                    break;
                case KeyState.Stuck:
                    bgStart = Color.FromArgb(239, 68, 68);
                    bgEnd = Color.FromArgb(185, 28, 28);
                    borderColor = Color.FromArgb(220, 38, 38);
                    textColor = Color.White;
                    break;
                default:
                    bgStart = Color.FromArgb(32, 32, 38);
                    bgEnd = Color.FromArgb(24, 24, 28);
                    borderColor = Color.FromArgb(52, 52, 60);
                    textColor = Color.FromArgb(170, 170, 180);
                    break;
            }

            using (GraphicsPath path = GetRoundPath(rect, cornerRadius))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, bgStart, bgEnd, 45f))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(borderColor, 1.5f))
                {
                    g.DrawPath(pen, path);
                }
            }

            string display = _keyText;
            if (display.StartsWith("NUM_", StringComparison.Ordinal))
            {
                display = display.Substring(4);
            }
            if (display == "SHIFT_L") display = "L Shift";
            if (display == "SHIFT_R") display = "R Shift";
            if (display == "CTRL_L") display = "L Ctrl";
            if (display == "CTRL_R") display = "R Ctrl";
            if (display == "ALT_L") display = "L Alt";
            if (display == "ALT_R") display = "R Alt";
            if (display == "BACKSPACE") display = "Backspace";
            if (display == "CAPSLOCK") display = "Caps";
            if (display == "NUMLOCK") display = "Num";
            if (display == "PRTSC") display = "PrtSc";
            if (display == "SCROLL") display = "ScrLk";
            if (display == "PAUSE") display = "Pause";
            if (display == "INSERT") display = "Ins";
            if (display == "DELETE") display = "Del";
            if (display == "PGUP") display = "PgUp";
            if (display == "PGDN") display = "PgDn";

            if (display == "`") display = "`  ~";
            if (display == "-") display = "-  _";
            if (display == "=") display = "=  +";
            if (display == "[") display = "[  {";
            if (display == "]") display = "]  }";
            if (display == "\\") display = "\\  |";
            if (display == ";") display = ";  :";
            if (display == "'") display = "'  \"";
            if (display == ",") display = ",  <";
            if (display == ".") display = ".  >";
            if (display == "/") display = "/  ?";

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            TextRenderer.DrawText(g, display, this.Font, Rectangle.Round(rect), textColor, flags);
        }

        private static GraphicsPath GetRoundPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius;
            if (rect.Width < r || rect.Height < r)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class MouseTesterControl : Control
    {
        public bool LPressed { get; set; }
        public bool RPressed { get; set; }
        public bool MPressed { get; set; }
        public bool X1Pressed { get; set; }
        public bool X2Pressed { get; set; }

        private string _scrollText = "滾動: 0 (無)";
        private int _scrollCount;
        private string _scrollDir = "";
        private Timer _scrollResetTimer;

        public MouseTesterControl()
        {
            this.DoubleBuffered = true;
            this.Size = new Size(200, 210);

            _scrollResetTimer = new Timer();
            _scrollResetTimer.Interval = 800;
            _scrollResetTimer.Tick += (s, e) =>
            {
                _scrollDir = "";
                Invalidate();
                _scrollResetTimer.Stop();
            };
        }

        public void RegisterScroll(int delta)
        {
            _scrollCount++;
            _scrollDir = delta > 0 ? "▲" : "▼";
            _scrollText = $"滾動: {_scrollCount} ({(_scrollDir == "▲" ? "上" : "下")})";
            _scrollResetTimer.Stop();
            _scrollResetTimer.Start();
            Invalidate();
        }

        public void ResetMouse()
        {
            LPressed = RPressed = MPressed = X1Pressed = X2Pressed = false;
            _scrollCount = 0;
            _scrollDir = "";
            _scrollText = "滾動: 0 (無)";
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scrollResetTimer?.Stop();
                _scrollResetTimer?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 24, 28)))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            using (Pen borderPen = new Pen(Color.FromArgb(45, 45, 52), 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }

            using (Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "滑鼠診斷區 (MOUSE DIAGNOSTICS)", titleFont, new Rectangle(0, 10, this.Width, 25), Color.FromArgb(200, 200, 210), TextFormatFlags.HorizontalCenter);
            }

            int mWidth = 90;
            int mHeight = 135;
            int mX = (this.Width - mWidth) / 2 + 10;
            int mY = 38;

            Color normalColor = Color.FromArgb(32, 32, 38);
            Color activeColor = Color.FromArgb(0, 242, 254);
            Color activeEndColor = Color.FromArgb(79, 172, 254);
            Color borderColor = Color.FromArgb(52, 52, 60);

            int xKeyWidth = 8;
            int xKeyHeight = 22;
            int xKeyX = mX - xKeyWidth + 1;
            int x1Y = mY + 65;
            int x2Y = mY + 38;

            DrawMouseKey(g, new Rectangle(xKeyX, x1Y, xKeyWidth, xKeyHeight), X1Pressed, normalColor, activeColor, activeEndColor, borderColor);
            DrawMouseKey(g, new Rectangle(xKeyX, x2Y, xKeyWidth, xKeyHeight), X2Pressed, normalColor, activeColor, activeEndColor, borderColor);

            Rectangle mRect = new Rectangle(mX, mY, mWidth, mHeight);
            using (GraphicsPath mPath = GetRoundPath(mRect, 30))
            {
                using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(18, 18, 22)))
                {
                    g.FillPath(bodyBrush, mPath);
                }
                using (Pen bodyPen = new Pen(borderColor, 2f))
                {
                    g.DrawPath(bodyPen, mPath);
                }
            }

            Rectangle lRect = new Rectangle(mX, mY, mWidth / 2, 50);
            using (GraphicsPath lPath = GetTopLeftRoundPath(lRect, 30))
            {
                FillMousePart(g, lPath, lRect, LPressed, normalColor, activeColor, activeEndColor);
                using (Pen p = new Pen(borderColor, 1.5f)) g.DrawPath(p, lPath);
            }

            Rectangle rRect = new Rectangle(mX + mWidth / 2, mY, mWidth / 2, 50);
            using (GraphicsPath rPath = GetTopRightRoundPath(rRect, 30))
            {
                FillMousePart(g, rPath, rRect, RPressed, normalColor, activeColor, activeEndColor);
                using (Pen p = new Pen(borderColor, 1.5f)) g.DrawPath(p, rPath);
            }

            int wWidth = 12;
            int wHeight = 24;
            int wX = mX + (mWidth - wWidth) / 2;
            int wY = mY + 12;
            Rectangle wRect = new Rectangle(wX, wY, wWidth, wHeight);
            using (GraphicsPath wPath = GetRoundPath(wRect, 6))
            {
                FillMousePart(g, wPath, wRect, MPressed, normalColor, Color.FromArgb(239, 68, 68), Color.FromArgb(185, 28, 28));
                using (Pen p = new Pen(borderColor, 1.5f)) g.DrawPath(p, wPath);
            }

            if (!string.IsNullOrEmpty(_scrollDir))
            {
                using (Font arrowFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, _scrollDir, arrowFont, new Rectangle(wX - 22, wY + 2, 20, 20), Color.FromArgb(0, 242, 254), TextFormatFlags.HorizontalCenter);
                }
            }

            using (Font textFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, _scrollText, textFont, new Rectangle(0, this.Height - 30, this.Width, 20), Color.FromArgb(180, 180, 190), TextFormatFlags.HorizontalCenter);
            }
        }

        private static void DrawMouseKey(Graphics g, Rectangle rect, bool pressed, Color normColor, Color actColor, Color actEndColor, Color bordColor)
        {
            using (GraphicsPath path = GetRoundPath(rect, 4))
            {
                if (pressed)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(rect, actColor, actEndColor, 45f))
                    {
                        g.FillPath(brush, path);
                    }
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(normColor))
                    {
                        g.FillPath(brush, path);
                    }
                }
                using (Pen pen = new Pen(bordColor, 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        private static void FillMousePart(Graphics g, GraphicsPath path, Rectangle rect, bool pressed, Color normColor, Color actColor, Color actEndColor)
        {
            if (pressed)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, actColor, actEndColor, 45f))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        private static GraphicsPath GetRoundPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius;
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath GetTopLeftRoundPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius;
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddLine(rect.Right, rect.Y, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath GetTopRightRoundPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius;
            path.AddLine(rect.X, rect.Y, rect.Right - r, rect.Y);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
            path.CloseFigure();
            return path;
        }
    }

}
