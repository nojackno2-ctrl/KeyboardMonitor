using KeyboardDiagnostic;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

var tests = new (string Name, Action Run)[]
{
    ("letters, digits and function keys", TestBasicKeys),
    ("left and right modifiers", TestModifiers),
    ("navigation and numpad overlap", TestNavigationAndNumpad),
    ("OEM and unknown keys", TestOemAndUnknownKeys),
    ("words-per-minute calculation", TestWordsPerMinute),
    ("KPS sampling and reset", TestKeyRateCounter),
    ("monotonic key state and stuck detection", TestKeyStateTracker),
    ("typing metrics and reset", TestTypingMetrics),
    ("thread-safe services and hook disposal", TestThreadSafetyAndLifecycle),
    ("cached paint resources and control disposal lifetime", TestKeyControlAndMouseTesterPaintLifetime)
};

int failed = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL: {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} test groups passed.");
return failed == 0 ? 0 : 1;

static void TestBasicKeys()
{
    Equal("A", KeyboardInput.ParseKey(0x41, 0, 0));
    Equal("9", KeyboardInput.ParseKey(0x39, 0, 0));
    Equal("F12", KeyboardInput.ParseKey(0x7B, 0, 0));
    Equal("NUM_7", KeyboardInput.ParseKey(0x67, 0, 0));
}

static void TestModifiers()
{
    Equal("SHIFT_L", KeyboardInput.ParseKey(0x10, 0x2A, 0));
    Equal("SHIFT_R", KeyboardInput.ParseKey(0x10, 0x36, 0));
    Equal("CTRL_L", KeyboardInput.ParseKey(0x11, 0, 0));
    Equal("CTRL_R", KeyboardInput.ParseKey(0x11, 0, 1));
    Equal("ALT_L", KeyboardInput.ParseKey(0x12, 0, 0));
    Equal("ALT_R", KeyboardInput.ParseKey(0x12, 0, 1));
    Equal("SHIFT_R", KeyboardInput.ParseKey(0xA1, 0, 0));
}

static void TestNavigationAndNumpad()
{
    Equal("INSERT", KeyboardInput.ParseKey(0x2D, 0, 1));
    Equal("NUM_0", KeyboardInput.ParseKey(0x2D, 0, 0));
    Equal("↑", KeyboardInput.ParseKey(0x26, 0, 1));
    Equal("NUM_8", KeyboardInput.ParseKey(0x26, 0, 0));
    Equal("DELETE", KeyboardInput.ParseKey(0x2E, 0, 1));
    Equal("NUM_.", KeyboardInput.ParseKey(0x2E, 0, 0));
    Equal("ENTER", KeyboardInput.ParseKey(0x0D, 0, 0));
    Equal("NUM_ENTER", KeyboardInput.ParseKey(0x0D, 0, 1));
}

static void TestOemAndUnknownKeys()
{
    Equal(";", KeyboardInput.ParseKey(0xBA, 0, 0));
    Equal("\\", KeyboardInput.ParseKey(0xDC, 0, 0));
    Equal(null, KeyboardInput.ParseKey(0xFF, 0, 0));
}

static void TestWordsPerMinute()
{
    Equal(0, KeyboardInput.CalculateWordsPerMinute(0, TimeSpan.FromMinutes(1)));
    Equal(0, KeyboardInput.CalculateWordsPerMinute(25, TimeSpan.FromSeconds(0.5)));
    Equal(5, KeyboardInput.CalculateWordsPerMinute(25, TimeSpan.FromMinutes(1)));
    Equal(10, KeyboardInput.CalculateWordsPerMinute(50, TimeSpan.FromMinutes(1)));
}

static void TestKeyRateCounter()
{
    var counter = new KeyRateCounter();
    counter.RecordPress();
    counter.RecordPress();
    Equal(2, counter.Sample());
    Equal(2, counter.LastSample);
    Equal(2, counter.Peak);

    counter.RecordPress();
    Equal(1, counter.Sample());
    Equal(1, counter.LastSample);
    Equal(2, counter.Peak);

    counter.Reset();
    Equal(0, counter.LastSample);
    Equal(0, counter.Peak);
}

static void TestKeyStateTracker()
{
    var clock = new FakeClock();
    var tracker = new KeyStateTracker(clock);

    Equal(true, tracker.Press("A"));
    Equal(false, tracker.Press("A"));
    clock.Advance(250);

    KeyReleaseResult release = tracker.Release("A");
    Equal(true, release.WasPressed);
    Equal(250.0, release.DurationMilliseconds);
    Equal(false, tracker.Release("A").WasPressed);

    Equal(true, tracker.Press("B"));
    clock.Advance(2000);
    var stuckKeys = tracker.MarkStuck(TimeSpan.FromSeconds(2));
    Equal(1, stuckKeys.Count);
    Equal("B", stuckKeys[0]);
    Equal(1, tracker.GetSnapshot().ActiveKeyCount);
    Equal(1, tracker.GetSnapshot().StuckKeys.Count);

    tracker.Reset();
    Equal(0, tracker.GetSnapshot().ActiveKeyCount);
}

static void TestTypingMetrics()
{
    var clock = new FakeClock();
    var metrics = new TypingMetrics(clock);

    metrics.ObserveCharacterCount(25);
    Equal(0, metrics.CalculateWordsPerMinute(25));
    clock.Advance(60_000);
    Equal(5, metrics.CalculateWordsPerMinute(25));

    metrics.Reset();
    Equal(0, metrics.CalculateWordsPerMinute(25));
}

static void TestThreadSafetyAndLifecycle()
{
    var counter = new KeyRateCounter();
    Parallel.For(0, 1000, _ => counter.RecordPress());
    Equal(1000, counter.Sample());

    var tracker = new KeyStateTracker();
    Parallel.For(0, 100, index => tracker.Press($"KEY_{index}"));
    Equal(100, tracker.GetSnapshot().ActiveKeyCount);
    Parallel.For(0, 100, index => tracker.Release($"KEY_{index}"));
    Equal(0, tracker.GetSnapshot().ActiveKeyCount);

    var hook = new GlobalInputHook();
    Equal(false, hook.IsStarted);
    hook.Stop();
    hook.Dispose();
    hook.Dispose();
    Equal(false, hook.IsStarted);
}

static void TestKeyControlAndMouseTesterPaintLifetime()
{
    // KeyControl／MouseTesterControl 衍生自 WinForms Control，需要 STA 執行緒才能安全建立與操作。
    Exception? failure = null;
    var thread = new System.Threading.Thread(() =>
    {
        try
        {
            RunPaintLifetimeChecks();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(System.Threading.ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure != null)
    {
        throw failure;
    }
}

static void RunPaintLifetimeChecks()
{
    FieldInfo borderPensField = typeof(KeyControl).GetField("BorderPens", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("KeyControl.BorderPens field not found.");
    var borderPens = (Pen[])borderPensField.GetValue(null)!;
    Equal(Enum.GetValues(typeof(KeyControl.KeyState)).Length, borderPens.Length);

    using var bitmap = new Bitmap(160, 40);
    using var graphics = Graphics.FromImage(bitmap);

    var keyA = new KeyControl { KeyText = "A", Size = new Size(80, 40) };
    var keyB = new KeyControl { KeyText = "B", Size = new Size(80, 40) };

    foreach (KeyControl.KeyState state in Enum.GetValues(typeof(KeyControl.KeyState)))
    {
        keyA.State = state;
        keyB.State = state;
        InvokeOnPaint(keyA, graphics);
        InvokeOnPaint(keyB, graphics);
    }

    var mouseTester = new MouseTesterControl { Size = new Size(200, 210) };
    InvokeOnPaint(mouseTester, graphics);
    mouseTester.LPressed = true;
    mouseTester.RegisterScroll(1);
    InvokeOnPaint(mouseTester, graphics);
    mouseTester.ResetMouse();

    keyA.Dispose();
    keyB.Dispose();
    mouseTester.Dispose();

    // 重複 Dispose 不應拋出例外，確保快取資源與計時器安全解除。
    keyA.Dispose();
    mouseTester.Dispose();
}

static void InvokeOnPaint(Control control, Graphics graphics)
{
    MethodInfo method = typeof(Control).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Control.OnPaint method not found.");
    using var args = new PaintEventArgs(graphics, control.ClientRectangle);
    method.Invoke(control, new object[] { args });
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected <{expected}>, actual <{actual}>");
    }
}

sealed class FakeClock : IMonotonicClock
{
    private long _timestamp;

    public long GetTimestamp()
    {
        return _timestamp;
    }

    public double GetElapsedMilliseconds(long startTimestamp)
    {
        return _timestamp - startTimestamp;
    }

    public void Advance(long milliseconds)
    {
        _timestamp += milliseconds;
    }
}
