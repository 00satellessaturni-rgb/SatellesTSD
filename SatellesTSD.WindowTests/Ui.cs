using System.Runtime.ExceptionServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SatellesTSD.WindowTests;

/// <summary>One UI thread for every window test: a single-threaded apartment with WinForms configured once and a
/// real message loop running (<see cref="Application.Run()"/>), as the app has. A test body is sent to that thread
/// and waited for; inside it, <see cref="Until"/> pumps the loop while a condition is awaited — the window's async
/// handlers continue on the WinForms synchronization context, which runs only when the loop is pumped.</summary>
internal static class Ui
{
    private static readonly Lazy<Control> Marshal = new(Start);
    private static string? _stuck;

    public static void Run(Action body, int timeoutSeconds = 90)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_stuck is not null)
        {
            // the body that timed out is still on the one UI thread (in a modal loop, or blocked): nothing later can be trusted
            throw new InvalidOperationException("An earlier window test is still stuck on the UI thread: " + _stuck);
        }

        var marshal = Marshal.Value;
        var work = Task.Run(() => marshal.Invoke(body));
        if (!work.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            _stuck = $"timed out after {timeoutSeconds} s";
            throw new TimeoutException($"The window test did not finish within {timeoutSeconds} s (a modal dialog, or a run that never ended).");
        }

        if (work.Exception is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure.InnerExceptions.Count == 1 ? failure.InnerException! : failure).Throw();
        }
    }

    /// <summary>Pumps the message loop until the condition holds.</summary>
    public static void Until(Func<bool> condition, int timeoutMs = 20000, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Waited {timeoutMs} ms for {what ?? "the condition"}.");
            }

            Application.DoEvents();
            Thread.Sleep(10);
        }

        Application.DoEvents();
    }

    /// <summary>Pumps whatever is queued (a BeginInvoke'd reload, a finished run's continuation).</summary>
    public static void Pump(int rounds = 3)
    {
        for (var i = 0; i < rounds; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static Control Start()
    {
        Control? marshal = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // what ApplicationConfiguration.Initialize does inside the WinExe; process-wide, before any window
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                marshal = new Control();
                _ = marshal.Handle; // the thread's marshaling window, so Invoke reaches this loop
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                Application.Run();
            }
        }) { IsBackground = true, Name = "window tests UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return marshal!;
    }
}
