using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace TypeFix
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = @"Local\TypeFix_SingleInstance_Mutex_B6A974A3-DFF6-4DF1-92BE-753C172BD3E8";
        private const string ShowEventName = @"Local\TypeFix_SingleInstance_Show_B6A974A3-DFF6-4DF1-92BE-753C172BD3E8";
        private const string ExitEventName = @"Local\TypeFix_SingleInstance_Exit_B6A974A3-DFF6-4DF1-92BE-753C172BD3E8";
        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private static Mutex? _mutex;
        private static EventWaitHandle? _showWaitHandle;
        private static RegisteredWaitHandle? _showRegistration;
        private static EventWaitHandle? _exitWaitHandle;
        private static RegisteredWaitHandle? _exitRegistration;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isExitRequest = false;
            bool startInTray = false;

            foreach (var arg in e.Args)
            {
                if (IsExitArgument(arg))
                {
                    isExitRequest = true;
                    break;
                }

                if (IsTrayArgument(arg))
                {
                    startInTray = true;
                }
            }

            if (isExitRequest)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEvent))
                    {
                        exitEvent.Set();
                        exitEvent.Dispose();
                    }
                }
                catch
                {
                    // Ignore if unable to notify
                }

                Shutdown();
                return;
            }

            _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                if (!startInTray)
                {
                    AllowSetForegroundWindow(ASFW_ANY);
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
                        {
                            showEvent.Set();
                            showEvent.Dispose();
                        }
                    }
                    catch
                    {
                        // Ignore if notification cannot be sent
                    }
                }

                Shutdown();
                return;
            }

            try
            {
                _showWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _showRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _showWaitHandle,
                    (state, timedOut) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (MainWindow is MainWindow mainWindow)
                            {
                                mainWindow.ShowFromTray();
                            }
                        }));
                    },
                    null,
                    Timeout.Infinite,
                    false);

                _exitWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
                _exitRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _exitWaitHandle,
                    (state, timedOut) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (MainWindow is MainWindow mainWindow)
                            {
                                mainWindow.ExitApp();
                            }
                            else
                            {
                                Shutdown();
                            }
                        }));
                    },
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch
            {
                // If event wait handle creation fails, continue running primary instance
            }

            UiLanguage.Initialize();
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startInTray)
            {
                mainWindow.StartInTray();
            }
            else
            {
                mainWindow.Show();
            }
        }

        private static bool IsExitArgument(string arg)
        {
            return arg.Equals("--exit", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-exit", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--quit", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/quit", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-quit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrayArgument(string arg)
        {
            return arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/tray", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/hidden", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-hidden", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-startup", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("/background", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("-background", StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showRegistration?.Unregister(null);
            _showRegistration = null;
            _showWaitHandle?.Dispose();
            _showWaitHandle = null;

            _exitRegistration?.Unregister(null);
            _exitRegistration = null;
            _exitWaitHandle?.Dispose();
            _exitWaitHandle = null;

            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Mutex was not owned or already released
                }
                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }
    }
}
