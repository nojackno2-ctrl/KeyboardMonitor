using KeyboardDiagnostic;

var tests = new (string Name, Action Run)[]
{
    ("letters, digits and function keys", TestBasicKeys),
    ("left and right modifiers", TestModifiers),
    ("navigation and numpad overlap", TestNavigationAndNumpad),
    ("OEM and unknown keys", TestOemAndUnknownKeys),
    ("words-per-minute calculation", TestWordsPerMinute),
    ("KPS sampling and reset", TestKeyRateCounter)
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

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected <{expected}>, actual <{actual}>");
    }
}
