using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace KeyboardDiagnostic
{
    internal static class DiagnosticTheme
    {
        public static readonly Color Canvas = Color.FromArgb(14, 18, 26);
        public static readonly Color Surface = Color.FromArgb(23, 28, 38);
        public static readonly Color SurfaceRaised = Color.FromArgb(31, 38, 51);
        public static readonly Color Input = Color.FromArgb(17, 23, 34);
        public static readonly Color Border = Color.FromArgb(58, 67, 84);
        public static readonly Color BorderStrong = Color.FromArgb(78, 89, 109);
        public static readonly Color Text = Color.FromArgb(242, 245, 250);
        public static readonly Color TextSecondary = Color.FromArgb(190, 199, 213);
        public static readonly Color TextMuted = Color.FromArgb(139, 150, 169);
        public static readonly Color Accent = Color.FromArgb(111, 157, 232);
        public static readonly Color AccentPressed = Color.FromArgb(92, 139, 218);
        public static readonly Color AccentSurface = Color.FromArgb(24, 43, 70);
        public static readonly Color OnAccent = Color.FromArgb(10, 18, 31);
        public static readonly Color Success = Color.FromArgb(83, 182, 138);
        public static readonly Color Warning = Color.FromArgb(224, 164, 79);
        public static readonly Color Danger = Color.FromArgb(218, 91, 106);

        public const int PanelRadius = 12;
        public const int KeyRadius = 6;

        public static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (diameter <= 1f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawPanel(Graphics graphics, Rectangle bounds, Brush surfaceBrush, Pen borderPen)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                return;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF frame = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1.5f, bounds.Height - 1.5f);
            using GraphicsPath path = CreateRoundedPath(frame, PanelRadius);
            graphics.FillPath(surfaceBrush, path);
            graphics.DrawPath(borderPen, path);
        }
    }

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

        private readonly GlobalInputHook _inputHook = new GlobalInputHook();
        private readonly KeyStateTracker _keyStateTracker = new KeyStateTracker();
        private readonly TypingMetrics _typingMetrics = new TypingMetrics();

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
        private readonly KeyRateCounter _keyRateCounter = new KeyRateCounter();
        private double _lastLatencyMs;
        private Timer _kpsTimer;

        // --- 快取供高頻重繪使用的固定 GDI 資源（表單生命週期內重複使用，於 Dispose 釋放）---
        private readonly Font _indicatorNameFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font _indicatorValueFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        private readonly Pen _indicatorBorderPen = new Pen(DiagnosticTheme.Border, 1f);
        private readonly Pen _panelBorderPen = new Pen(DiagnosticTheme.Border, 1f);
        private readonly SolidBrush _panelSurfaceBrush = new SolidBrush(DiagnosticTheme.Surface);
        private readonly SolidBrush _indicatorSurfaceBrush = new SolidBrush(DiagnosticTheme.Input);
        private readonly SolidBrush _statusNormalBrush = new SolidBrush(DiagnosticTheme.Success);
        private readonly SolidBrush _statusStuckBrush = new SolidBrush(DiagnosticTheme.Danger);
        private readonly SolidBrush _logDefaultBrush = new SolidBrush(DiagnosticTheme.TextSecondary);
        private readonly SolidBrush _logPressBrush = new SolidBrush(DiagnosticTheme.Accent);
        private readonly SolidBrush _logReleaseBrush = new SolidBrush(DiagnosticTheme.Success);
        private readonly SolidBrush _logStuckBrush = new SolidBrush(DiagnosticTheme.Danger);
        private readonly SolidBrush _logMouseBrush = new SolidBrush(DiagnosticTheme.Warning);

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
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Text = "Windows 11 鍵盤與滑鼠診斷工具";
            this.ClientSize = new Size(1450, 820);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = DiagnosticTheme.Canvas;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            TableLayoutPanel rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = DiagnosticTheme.Canvas,
                Padding = new Padding(24, 12, 24, 12),
                Margin = Padding.Empty,
                RowCount = 4,
                ColumnCount = 1
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 54f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 46f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            this.Controls.Add(rootLayout);

            // 1. 頂部控制面板
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DiagnosticTheme.Canvas,
                Padding = new Padding(4, 4, 0, 8),
                Margin = Padding.Empty
            };

            Label titleLabel = new Label
            {
                Text = "KEYBOARD & MOUSE DIAGNOSTIC",
                Font = new Font("Segoe UI", 15.5f, FontStyle.Bold),
                ForeColor = DiagnosticTheme.Text,
                AutoSize = true,
                Location = new Point(4, 14),
                AccessibleRole = AccessibleRole.StaticText
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
                Padding = new Padding(0, 11, 0, 0),
                Margin = Padding.Empty
            };

            _statusLight = new Label
            {
                Width = 14,
                Height = 14,
                BackColor = Color.Transparent,
                Tag = false,
                Margin = new Padding(5, 6, 5, 0)
            };
            // 繪製圓形指示燈（依目前狀態選用快取好的固定筆刷，避免每次重繪配置新物件）
            _statusLight.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                SolidBrush b = _statusLight.Tag is true ? _statusStuckBrush : _statusNormalBrush;
                e.Graphics.FillEllipse(b, 0, 0, _statusLight.Width - 1, _statusLight.Height - 1);
            };
            statusPanel.Controls.Add(_statusLight);

            _statusText = new Label
            {
                Text = "系統偵測中 - 正常",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = _statusNormalBrush.Color,
                AutoSize = true,
                Margin = new Padding(5, 4, 18, 0),
                AccessibleName = "診斷狀態"
            };
            statusPanel.Controls.Add(_statusText);

            _countLabel = new Label
            {
                Text = "當前按下鍵數: 0",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = DiagnosticTheme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(5, 4, 18, 0),
                AccessibleName = "目前按下鍵數"
            };
            statusPanel.Controls.Add(_countLabel);

            // 鍵盤種類下拉選單
            _keyboardTypeSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = DiagnosticTheme.SurfaceRaised,
                ForeColor = DiagnosticTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(166, 30),
                Margin = new Padding(5, 0, 14, 0),
                Cursor = Cursors.Hand,
                TabStop = true,
                TabIndex = 0,
                AccessibleName = "鍵盤配置",
                AccessibleDescription = "選擇全尺寸、TKL 或緊湊型鍵盤配置"
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
                BackColor = DiagnosticTheme.Accent,
                ForeColor = DiagnosticTheme.OnAccent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(92, 30),
                Margin = new Padding(5, 0, 5, 0),
                Cursor = Cursors.Hand,
                TabStop = true,
                TabIndex = 1,
                AccessibleName = "清除重設",
                AccessibleDescription = "清除所有鍵盤、滑鼠、打字與統計狀態"
            };
            resetBtn.FlatAppearance.BorderSize = 1;
            resetBtn.FlatAppearance.BorderColor = DiagnosticTheme.Accent;
            resetBtn.FlatAppearance.MouseOverBackColor = DiagnosticTheme.AccentPressed;
            resetBtn.FlatAppearance.MouseDownBackColor = DiagnosticTheme.AccentPressed;
            resetBtn.Click += (s, e) => ResetAll();
            statusPanel.Controls.Add(resetBtn);

            topPanel.Controls.Add(statusPanel);
            rootLayout.Controls.Add(topPanel, 0, 0);

            // 2. 鍵盤主體卡片面板容器
            _keyboardContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = DiagnosticTheme.Canvas,
                Padding = new Padding(18),
                Margin = new Padding(0, 6, 0, 8),
                RowCount = 1,
                ColumnCount = 3
            };
            _keyboardContainer.Paint += (s, e) =>
            {
                DiagnosticTheme.DrawPanel(e.Graphics, _keyboardContainer.ClientRectangle, _panelSurfaceBrush, _panelBorderPen);
            };
            rootLayout.Controls.Add(_keyboardContainer, 0, 1);

            // 3. 下方三大特色版面容器
            _bottomPanelContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 6),
                RowCount = 1,
                ColumnCount = 3,
                BackColor = Color.Transparent
            };
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21f)); // 滑鼠診斷
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51f)); // 打字測試
            _bottomPanelContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f)); // 即時日誌
            rootLayout.Controls.Add(_bottomPanelContainer, 0, 2);

            // --- 3.1 滑鼠診斷區 ---
            _mouseTester = new MouseTesterControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0),
                AccessibleName = "滑鼠診斷區",
                AccessibleDescription = "顯示左右鍵、中鍵、側鍵與滾輪輸入狀態"
            };
            _bottomPanelContainer.Controls.Add(_mouseTester, 0, 0);

            // --- 3.2 打字測試區面板 ---
            Panel typePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DiagnosticTheme.Canvas,
                Padding = new Padding(18),
                Margin = new Padding(4, 0, 4, 0)
            };
            typePanel.Paint += (s, e) =>
                DiagnosticTheme.DrawPanel(e.Graphics, typePanel.ClientRectangle, _panelSurfaceBrush, _panelBorderPen);

            Label typeTitle = new Label
            {
                Text = "打字與延遲測試 (TYPING & LATENCY TEST)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = DiagnosticTheme.TextSecondary,
                Location = new Point(18, 14),
                AutoSize = true,
                AccessibleRole = AccessibleRole.StaticText
            };
            typePanel.Controls.Add(typeTitle);

            _typeTextBox = new TextBox
            {
                Multiline = true,
                BackColor = DiagnosticTheme.Input,
                ForeColor = DiagnosticTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10.5f),
                Location = new Point(18, 42),
                Size = new Size(650, 96),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                TabStop = true,
                TabIndex = 2,
                AccessibleName = "打字測試輸入區",
                AccessibleDescription = "在此輸入以計算 WPM，按 Escape 清空"
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
                Location = new Point(18, 150),
                Size = new Size(650, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _wpmLabel = CreateIndicatorLabel("WPM", () => GetWPMString());
            _kpsLabel = CreateIndicatorLabel("當前 KPS", () => GetKpsString(false));
            _maxKpsLabel = CreateIndicatorLabel("最高 KPS", () => GetKpsString(true));
            _latencyLabel = CreateIndicatorLabel("按鍵持續時間", () => GetLastLatencyString());

            speedIndicators.Controls.Add(_wpmLabel);
            speedIndicators.Controls.Add(_kpsLabel);
            speedIndicators.Controls.Add(_maxKpsLabel);
            speedIndicators.Controls.Add(_latencyLabel);
            typePanel.Controls.Add(speedIndicators);
            typePanel.Resize += (s, e) =>
            {
                int contentWidth = Math.Max(200, typePanel.ClientSize.Width - 36);
                _typeTextBox.Width = contentWidth;
                speedIndicators.Width = contentWidth;
            };
            _bottomPanelContainer.Controls.Add(typePanel, 1, 0);

            // --- 3.3 即時日誌面板 ---
            Panel logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DiagnosticTheme.Canvas,
                Padding = new Padding(18),
                Margin = new Padding(8, 0, 0, 0)
            };
            logPanel.Paint += (s, e) =>
                DiagnosticTheme.DrawPanel(e.Graphics, logPanel.ClientRectangle, _panelSurfaceBrush, _panelBorderPen);

            Label logTitle = new Label
            {
                Text = "實時按鍵日誌 (LIVE EVENT LOG)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = DiagnosticTheme.TextSecondary,
                Location = new Point(18, 14),
                AutoSize = true,
                AccessibleRole = AccessibleRole.StaticText
            };
            logPanel.Controls.Add(logTitle);

            _logListBox = new ListBox
            {
                BackColor = DiagnosticTheme.Input,
                ForeColor = DiagnosticTheme.TextSecondary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                Location = new Point(18, 42),
                Size = new Size(350, 210),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                TabStop = true,
                TabIndex = 3,
                AccessibleName = "即時按鍵日誌",
                AccessibleDescription = "顯示最近一百筆鍵盤與滑鼠輸入事件"
            };
            _logListBox.DrawItem += LogListBox_DrawItem;
            logPanel.Controls.Add(_logListBox);
            logPanel.Resize += (s, e) =>
            {
                _logListBox.Size = new Size(
                    Math.Max(100, logPanel.ClientSize.Width - 36),
                    Math.Max(80, logPanel.ClientSize.Height - 60));
            };
            _bottomPanelContainer.Controls.Add(logPanel, 2, 0);

            // 4. 底部提示資訊
            _bottomTips = new Label
            {
                Text = "冷藍色代表目前按下 | 深藍色代表已測試過 | 紅色代表卡鍵 (>2秒) | 支援滑鼠點擊與滾輪檢測 | 按 ESC 可清空打字測試區",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = DiagnosticTheme.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                AccessibleRole = AccessibleRole.StaticText
            };
            rootLayout.Controls.Add(_bottomTips, 0, 3);

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

        private Label CreateIndicatorLabel(string name, Func<string> getValue)
        {
            Label lbl = new Label
            {
                Size = new Size(158, 70),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(10),
                AccessibleName = name
            };
            // 此面板每秒／每次按鍵都會重繪，字型與邊框 Pen 使用表單快取的固定資源
            lbl.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                DiagnosticTheme.DrawPanel(e.Graphics, lbl.ClientRectangle, _indicatorSurfaceBrush, _indicatorBorderPen);
                TextRenderer.DrawText(e.Graphics, name, _indicatorNameFont, new Rectangle(11, 7, lbl.Width - 22, 20), DiagnosticTheme.TextMuted);
                string valueText = getValue();
                lbl.AccessibleDescription = $"{name}: {valueText}";
                TextRenderer.DrawText(e.Graphics, valueText, _indicatorValueFont, new Rectangle(11, 27, lbl.Width - 22, 34), DiagnosticTheme.Accent, TextFormatFlags.VerticalCenter);
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
            _inputHook.KeyChanged += InputHook_KeyChanged;
            _inputHook.MouseButtonChanged += InputHook_MouseButtonChanged;
            _inputHook.MouseWheelScrolled += InputHook_MouseWheelScrolled;
            _inputHook.CallbackError += InputHook_CallbackError;

            try
            {
                _inputHook.Start();
            }
            catch (Win32Exception exception)
            {
                MessageBox.Show(
                    this,
                    $"無法安裝全域輸入監控 Hook（Win32 錯誤 {exception.NativeErrorCode}）。程式將關閉。",
                    "初始化失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                BeginInvoke(new Action(Close));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _inputHook.Stop();
            _watchdogTimer?.Stop();
            _kpsTimer?.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            _inputHook.Dispose();
            if (disposing)
            {
                _watchdogTimer?.Dispose();
                _kpsTimer?.Dispose();
                _indicatorNameFont?.Dispose();
                _indicatorValueFont?.Dispose();
                _indicatorBorderPen?.Dispose();
                _panelBorderPen?.Dispose();
                _panelSurfaceBrush?.Dispose();
                _indicatorSurfaceBrush?.Dispose();
                _statusNormalBrush?.Dispose();
                _statusStuckBrush?.Dispose();
                _logDefaultBrush?.Dispose();
                _logPressBrush?.Dispose();
                _logReleaseBrush?.Dispose();
                _logStuckBrush?.Dispose();
                _logMouseBrush?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InputHook_KeyChanged(string keyName, bool isPressed)
        {
            if (isPressed)
            {
                OnKeyDownEvent(keyName);
            }
            else
            {
                OnKeyUpEvent(keyName);
            }
        }

        private void InputHook_MouseButtonChanged(string buttonName, bool isPressed)
        {
            OnMouseChanged(buttonName, isPressed);
        }

        private void InputHook_MouseWheelScrolled(int delta)
        {
            OnMouseWheelScrolled(delta);
        }

        private void InputHook_CallbackError(Exception exception)
        {
            AddLog($"[Hook 錯誤] {exception.Message}");
        }

        private void OnKeyDownEvent(string keyName)
        {
            if (_keyStateTracker.Press(keyName))
            {
                _keyRateCounter.RecordPress();
                UpdateKeyUI(keyName, KeyControl.KeyState.Pressed);
                AddLog($"[按下] {keyName}");
            }
        }

        private void OnKeyUpEvent(string keyName)
        {
            KeyReleaseResult release = _keyStateTracker.Release(keyName);
            if (release.WasPressed)
            {
                UpdateKeyUI(keyName, KeyControl.KeyState.Tested);

                string durationStr = release.DurationMilliseconds > 0
                    ? $" (持續 {release.DurationMilliseconds:F0}ms)"
                    : "";
                AddLog($"[放開] {keyName}{durationStr}");

                if (release.DurationMilliseconds > 0)
                {
                    _lastLatencyMs = release.DurationMilliseconds;
                    UpdateLatencyUI(release.DurationMilliseconds);
                }
            }
        }

        private void StuckWatchdog_Tick(object sender, EventArgs e)
        {
            IReadOnlyList<string> stuckKeys = _keyStateTracker.MarkStuck(TimeSpan.FromSeconds(2));

            foreach (var keyName in stuckKeys)
            {
                UpdateKeyUI(keyName, KeyControl.KeyState.Stuck);
                AddLog($"[卡鍵] {keyName} (已按住 >2秒!)");
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
            _wpmLabel.Invalidate();
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
            _typingMetrics.ObserveCharacterCount(_typeTextBox.TextLength);
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

            KeyStateSnapshot snapshot = _keyStateTracker.GetSnapshot();
            int pressedCount = snapshot.ActiveKeyCount;
            IReadOnlyList<string> stuckKeys = snapshot.StuckKeys;

            _countLabel.Text = $"當前按下鍵數: {pressedCount}";

            if (stuckKeys.Count > 0)
            {
                _statusLight.Tag = true;
                _statusText.Text = $"警告：偵測到卡鍵！({string.Join(", ", stuckKeys)})";
                _statusText.ForeColor = _statusStuckBrush.Color;
            }
            else
            {
                _statusLight.Tag = false;
                _statusText.ForeColor = _statusNormalBrush.Color;
                _statusText.Text = pressedCount > 0
                    ? $"偵測中 - 同時按下 {pressedCount} 個鍵"
                    : "系統偵測中 - 正常";
            }
            _statusLight.Invalidate();
        }

        private void ResetAll()
        {
            _keyStateTracker.Reset();
            _keyRateCounter.Reset();
            _typingMetrics.Reset();
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
            SolidBrush textBrush = _logDefaultBrush;

            if (text.Contains("[按下]", StringComparison.Ordinal))
            {
                textBrush = _logPressBrush;
            }
            else if (text.Contains("[放開]", StringComparison.Ordinal))
            {
                textBrush = _logReleaseBrush;
            }
            else if (text.Contains("[卡鍵]", StringComparison.Ordinal))
            {
                textBrush = _logStuckBrush;
            }
            else if (text.Contains("[滑鼠]", StringComparison.Ordinal))
            {
                textBrush = _logMouseBrush;
            }

            e.Graphics.DrawString(text, e.Font, textBrush, e.Bounds.X + 5, e.Bounds.Y + 2);

            e.DrawFocusRectangle();
        }

        public string GetWPMString()
        {
            return _typingMetrics.CalculateWordsPerMinute(_typeTextBox.TextLength)
                .ToString(CultureInfo.InvariantCulture);
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

        // 邊框顏色只有四種固定狀態；以應用程式生命週期的靜態 Pen 陣列共用，避免每次重繪配置/釋放
        private static readonly Pen[] BorderPens =
        {
            new Pen(DiagnosticTheme.BorderStrong, 1.2f), // Untested
            new Pen(DiagnosticTheme.Accent, 1.8f),       // Pressed
            new Pen(DiagnosticTheme.Accent, 1.2f),       // Tested
            new Pen(DiagnosticTheme.Danger, 1.8f),       // Stuck
        };

        private static readonly SolidBrush[] BackgroundBrushes =
        {
            new SolidBrush(DiagnosticTheme.SurfaceRaised),
            new SolidBrush(DiagnosticTheme.Accent),
            new SolidBrush(DiagnosticTheme.AccentSurface),
            new SolidBrush(DiagnosticTheme.Danger),
        };

        private static readonly Color[] TextColors =
        {
            DiagnosticTheme.TextSecondary,
            DiagnosticTheme.OnAccent,
            DiagnosticTheme.Accent,
            DiagnosticTheme.OnAccent,
        };

        private static Pen GetBorderPen(KeyState state)
        {
            int index = (int)state;
            return index >= 0 && index < BorderPens.Length ? BorderPens[index] : BorderPens[0];
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
            set
            {
                _state = value;
                AccessibleDescription = value switch
                {
                    KeyState.Pressed => $"{_keyText} 目前按下",
                    KeyState.Tested => $"{_keyText} 已測試",
                    KeyState.Stuck => $"{_keyText} 可能卡鍵",
                    _ => $"{_keyText} 尚未測試",
                };
                Invalidate();
            }
        }

        public KeyControl()
        {
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
            this.TabStop = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RectangleF rect = new RectangleF(1, 1, this.Width - 3, this.Height - 3);
            int stateIndex = Math.Clamp((int)_state, 0, BackgroundBrushes.Length - 1);
            using (GraphicsPath path = DiagnosticTheme.CreateRoundedPath(rect, DiagnosticTheme.KeyRadius))
            {
                g.FillPath(BackgroundBrushes[stateIndex], path);
                g.DrawPath(GetBorderPen(_state), path);
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
            TextRenderer.DrawText(g, display, this.Font, Rectangle.Round(rect), TextColors[stateIndex], flags);
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

        // --- 快取供高頻重繪（每次滑鼠按鍵/滾輪事件）使用的固定 GDI 資源，於 Dispose 釋放 ---
        private readonly Font _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _smallBoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly SolidBrush _backgroundBrush = new SolidBrush(DiagnosticTheme.Surface);
        private readonly SolidBrush _canvasBrush = new SolidBrush(DiagnosticTheme.Canvas);
        private readonly SolidBrush _mouseBodyFillBrush = new SolidBrush(DiagnosticTheme.Input);
        private readonly SolidBrush _keyNormalBrush = new SolidBrush(DiagnosticTheme.SurfaceRaised);
        private readonly Pen _outerBorderPen = new Pen(DiagnosticTheme.Border, 1f);
        private readonly Pen _bodyBorderPen = new Pen(DiagnosticTheme.BorderStrong, 2f);
        private readonly Pen _partBorderPen = new Pen(DiagnosticTheme.BorderStrong, 1.5f);
        private readonly Pen _keyBorderPen = new Pen(DiagnosticTheme.BorderStrong, 1.2f);

        public MouseTesterControl()
        {
            this.DoubleBuffered = true;
            this.Size = new Size(200, 210);
            this.BackColor = DiagnosticTheme.Canvas;
            this.TabStop = false;

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
                _titleFont?.Dispose();
                _smallBoldFont?.Dispose();
                _backgroundBrush?.Dispose();
                _canvasBrush?.Dispose();
                _mouseBodyFillBrush?.Dispose();
                _keyNormalBrush?.Dispose();
                _outerBorderPen?.Dispose();
                _bodyBorderPen?.Dispose();
                _partBorderPen?.Dispose();
                _keyBorderPen?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            g.FillRectangle(_canvasBrush, this.ClientRectangle);
            DiagnosticTheme.DrawPanel(g, this.ClientRectangle, _backgroundBrush, _outerBorderPen);
            TextRenderer.DrawText(
                g,
                "滑鼠診斷區 (MOUSE DIAGNOSTICS)",
                _titleFont,
                new Rectangle(18, 14, Math.Max(0, this.Width - 36), 25),
                DiagnosticTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int mWidth = 90;
            int mHeight = 135;
            int mX = (this.Width - mWidth) / 2 + 10;
            int mY = 48;

            Color activeColor = DiagnosticTheme.Accent;

            int xKeyWidth = 8;
            int xKeyHeight = 22;
            int xKeyX = mX - xKeyWidth + 1;
            int x1Y = mY + 65;
            int x2Y = mY + 38;

            DrawMouseKey(g, new Rectangle(xKeyX, x1Y, xKeyWidth, xKeyHeight), X1Pressed, activeColor);
            DrawMouseKey(g, new Rectangle(xKeyX, x2Y, xKeyWidth, xKeyHeight), X2Pressed, activeColor);

            Rectangle mRect = new Rectangle(mX, mY, mWidth, mHeight);
            using (GraphicsPath mPath = GetRoundPath(mRect, 30))
            {
                g.FillPath(_mouseBodyFillBrush, mPath);
                g.DrawPath(_bodyBorderPen, mPath);
            }

            Rectangle lRect = new Rectangle(mX, mY, mWidth / 2, 50);
            using (GraphicsPath lPath = GetTopLeftRoundPath(lRect, 30))
            {
                FillMousePart(g, lPath, LPressed, activeColor);
                g.DrawPath(_partBorderPen, lPath);
            }

            Rectangle rRect = new Rectangle(mX + mWidth / 2, mY, mWidth / 2, 50);
            using (GraphicsPath rPath = GetTopRightRoundPath(rRect, 30))
            {
                FillMousePart(g, rPath, RPressed, activeColor);
                g.DrawPath(_partBorderPen, rPath);
            }

            int wWidth = 12;
            int wHeight = 24;
            int wX = mX + (mWidth - wWidth) / 2;
            int wY = mY + 12;
            Rectangle wRect = new Rectangle(wX, wY, wWidth, wHeight);
            using (GraphicsPath wPath = GetRoundPath(wRect, 6))
            {
                FillMousePart(g, wPath, MPressed, activeColor);
                g.DrawPath(_partBorderPen, wPath);
            }

            if (!string.IsNullOrEmpty(_scrollDir))
            {
                TextRenderer.DrawText(g, _scrollDir, _smallBoldFont, new Rectangle(wX - 22, wY + 2, 20, 20), DiagnosticTheme.Accent, TextFormatFlags.HorizontalCenter);
            }

            TextRenderer.DrawText(g, _scrollText, _smallBoldFont, new Rectangle(0, this.Height - 32, this.Width, 20), DiagnosticTheme.TextSecondary, TextFormatFlags.HorizontalCenter);
        }

        private void DrawMouseKey(Graphics g, Rectangle rect, bool pressed, Color activeColor)
        {
            using (GraphicsPath path = GetRoundPath(rect, 4))
            {
                if (pressed)
                {
                    using SolidBrush brush = new SolidBrush(activeColor);
                    g.FillPath(brush, path);
                }
                else
                {
                    g.FillPath(_keyNormalBrush, path);
                }
                g.DrawPath(_keyBorderPen, path);
            }
        }

        private void FillMousePart(Graphics g, GraphicsPath path, bool pressed, Color activeColor)
        {
            if (pressed)
            {
                using SolidBrush brush = new SolidBrush(activeColor);
                g.FillPath(brush, path);
            }
            else
            {
                g.FillPath(_keyNormalBrush, path);
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
