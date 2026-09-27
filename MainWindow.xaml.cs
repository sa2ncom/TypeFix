using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Reflection;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace TypeFix
{
    public partial class MainWindow : Window
    {
        // WinAPI: global hotkey
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, WF.Keys vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int HOTKEY_ID = 1;
        private const uint MOD_NONE = 0x0000; // just F10
        private const int VK_F10 = 0x79;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_C = 0x43;
        private const ushort VK_V = 0x56;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const string LayoutSampleJson =
            """
            {
              "Id": "en-zh",
              "Name": "Chinese ↔ English",
              "LayoutPairs": [
                {
                  "Id": "en-zh",
                  "Name": "English-Chinese",
                  "Layout1Name": "English",
                  "Layout2Name": "Chinese",
                  "Layout1Chars": "qwertyuiop[]asdfghjkl;'zxcvbnm,./",
                  "Layout2Chars": "qwertyuiop[]asdfghjkl;'zxcvbnm,./"
                }
              ]
            }
            """;

        private const string WebsiteUrl = "https://typefix.ir";
        private const string DonateUrl = "https://typefix.ir/donate";
        private const string GitHubUrl = "https://github.com/sa2ncom/TypeFix";

        private WF.NotifyIcon? _trayIcon;
        private WF.ContextMenuStrip? _trayMenu;
        private WF.ToolStripMenuItem? _trayOpenItem;
        private WF.ToolStripMenuItem? _trayExitItem;
        private HwndSource? _hwndSource;
        private readonly LayoutCatalog _catalog;
        private readonly LanguageEngine _engine;
        private int _isProcessingSelection; // 0 = idle, 1 = busy (Interlocked)
        private bool _isPseudoMaximized;
        private Rect _restoreBounds;
        private bool _allowExit;
        private bool _trayHintShown;
        private bool _suppressLanguageChange;
        private bool _snugHeightApplied;
        private ResizeEdge _resizeEdge = ResizeEdge.None;
        private System.Windows.Point _resizeCursor;
        private Rect _resizeBounds;
        private System.Drawing.Text.PrivateFontCollection? _vazirmatnFonts;
        private GCHandle _vazirmatnPin;
        private System.Drawing.Font? _vazirmatnMenuFont;
        private System.Drawing.Font? _trayUiFont;

        public MainWindow()
        {
            InitializeComponent();

            _suppressLanguageChange = true;
            LanguageCombo.ItemsSource = UiLanguage.Options;
            LanguageCombo.SelectedValue = UiLanguage.Current;
            _suppressLanguageChange = false;

            _catalog = new LayoutCatalog();
            _engine = new LanguageEngine();
            _engine.Apply(_catalog.Active.Config);
            _catalog.Changed += OnLayoutsChanged;
            GeneralLayouts.Attach(_catalog);
            SettingsLayouts.Attach(_catalog);
            GeneralLayouts.ReportStatus = ReportLayoutStatus;
            SettingsLayouts.ReportStatus = ReportLayoutStatus;

            ApplyWindowIcon();
            InitTrayIcon();
            ApplyLanguageChrome();

            var app = System.Windows.Application.Current;
            if (app != null)
                app.SessionEnding += (_, _) => _allowExit = true;

            StartupToggle.IsChecked = StartupManager.IsStartupEnabled();

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = ver is null ? "v1.0.0" : $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            LayoutSampleBox.Text = LayoutSampleJson;

            SetStatus(UiLanguage.Format("StatusReadyLayout", _catalog.Label(_catalog.Active)));
        }

        private void OnLayoutsChanged()
        {
            _engine.Apply(_catalog.Active.Config);
            if (_trayIcon != null)
                _trayIcon.Text = TrayTip(_catalog.Label(_catalog.Active));
        }

        private void ReportLayoutStatus(string message, bool isError)
        {
            SetStatus(message, isError);
        }

        private static string TrayTip(string layoutName)
        {
            string text = "TypeFix · " + layoutName;
            return text.Length <= 63 ? text : text[..63];
        }

        #region Window & Tray
        private void ApplyWindowIcon()
        {
            try
            {
                using var icon = LoadAppIcon();
                Icon = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                // The custom title bar does not need this; the taskbar falls back to the executable icon.
            }
        }

        private void InitTrayIcon()
        {
            _trayIcon = new WF.NotifyIcon
            {
                Text = TrayTip(_catalog.Label(_catalog.Active)),
                Icon = LoadAppIcon(),
                Visible = true
            };

            _trayMenu = new WF.ContextMenuStrip();
            _trayOpenItem = new WF.ToolStripMenuItem(UiLanguage.Get("TrayOpen"), null, (_, _) => ShowFromTray());
            _trayExitItem = new WF.ToolStripMenuItem(UiLanguage.Get("TrayExit"), null, (_, _) => ExitApp());
            _trayMenu.Items.Add(_trayOpenItem);
            _trayMenu.Items.Add(_trayExitItem);
            _trayIcon.ContextMenuStrip = _trayMenu;

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == WF.MouseButtons.Left)
                    ShowFromTray();
            };
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }

        private static Icon LoadAppIcon()
        {
            int size = 32;
            try
            {
                int small = WF.SystemInformation.SmallIconSize.Width;
                if (small > 0)
                    size = small;
            }
            catch
            {
                // Keep the default tray size.
            }

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TypeFix.ico");
            try
            {
                if (File.Exists(iconPath))
                {
                    using var fileIcon = new Icon(iconPath);
                    return new Icon(fileIcon, size, size);
                }
            }
            catch
            {
                // Fall through to the icon embedded in the executable.
            }

            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    using var associated = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (associated != null)
                        return new Icon(associated, size, size);
                }
            }
            catch
            {
                // Fall through to the generic application icon.
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private void ShowFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                ShowInTaskbar = true;
                Show();
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Focus();
            });
        }

        private void ExitApp()
        {
            // Defer so the tray menu click finishes before the icon is disposed.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _allowExit = true;
                System.Windows.Application.Current.Shutdown();
            }));
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (_isPseudoMaximized)
            {
                double percent = ActualWidth > 0 ? e.GetPosition(this).X / ActualWidth : 0.5;
                var screen = PointToScreen(e.GetPosition(this));
                ToggleMaximize();
                Left = screen.X - (Width * percent);
                Top = screen.Y - 24;
            }

            try
            {
                DragMove();
            }
            catch
            {
                // DragMove throws if the button is released before the call.
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (_isPseudoMaximized)
            {
                Shell.Margin = new Thickness(20);
                Shell.CornerRadius = new CornerRadius(16);
                Shell.Effect = CreateWindowShadow();
                Left = _restoreBounds.Left;
                Top = _restoreBounds.Top;
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
                MaximizeGlyph.Text = "\uE922";
                MaximizeButton.ToolTip = UiLanguage.Get("TooltipMaximize");
                _isPseudoMaximized = false;
                ResizeChrome.Visibility = Visibility.Visible;
                return;
            }

            _restoreBounds = new Rect(Left, Top, Width, Height);
            var area = SystemParameters.WorkArea;
            Shell.Margin = new Thickness(0);
            Shell.CornerRadius = new CornerRadius(0);
            Shell.Effect = null;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
            MaximizeGlyph.Text = "\uE923";
            MaximizeButton.ToolTip = UiLanguage.Get("TooltipRestore");
            _isPseudoMaximized = true;
            ResizeChrome.Visibility = Visibility.Collapsed;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySnugDefaultHeight();
        }

        private void ApplySnugDefaultHeight()
        {
            if (_snugHeightApplied || _isPseudoMaximized)
                return;

            UpdateLayout();
            if (ActualHeight < 1)
                return;

            _snugHeightApplied = true;
            double gap = RailGap.ActualHeight;
            double fitted = ActualHeight - (gap > 0.5 ? gap : 0);
            double extra = SettingsContentOverflow(gap);
            SizeToContent = SizeToContent.Manual;
            Height = fitted + extra;
            MinHeight = fitted + extra;
        }

        private double SettingsContentOverflow(double railGap)
        {
            double width = PagesHost.ActualWidth;
            if (width < 1)
                return 0;

            // Match the page scroll viewer's right padding so wrapped lines measure the same.
            SettingsBody.Measure(new System.Windows.Size(Math.Max(1, width - 4), double.PositiveInfinity));
            double viewport = PagesHost.ActualHeight - (railGap > 0.5 ? railGap : 0);
            double overflow = SettingsBody.DesiredSize.Height - viewport;
            if (overflow < 1)
                return 0;

            return Math.Ceiling(overflow + 12);
        }

        private void ResizeEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPseudoMaximized || sender is not FrameworkElement edge)
                return;

            var edgeName = ParseResizeEdge(edge.Tag);
            if (edgeName == ResizeEdge.None)
                return;

            _resizeEdge = edgeName;
            _resizeCursor = PointToScreen(e.GetPosition(this));
            _resizeBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            Mouse.Capture(this);
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (_resizeEdge == ResizeEdge.None || e.LeftButton != MouseButtonState.Pressed)
                return;

            var cursor = PointToScreen(e.GetPosition(this));
            ApplyResize(cursor.X - _resizeCursor.X, cursor.Y - _resizeCursor.Y);
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (_resizeEdge == ResizeEdge.None)
                return;

            _resizeEdge = ResizeEdge.None;
            if (Mouse.Captured == this)
                Mouse.Capture(null);
        }

        private void ApplyResize(double dx, double dy)
        {
            double left = _resizeBounds.Left;
            double top = _resizeBounds.Top;
            double width = _resizeBounds.Width;
            double height = _resizeBounds.Height;
            double minWidth = MinWidth > 0 ? MinWidth : 980;
            double minHeight = MinHeight > 0 ? MinHeight : 0;

            bool west = _resizeEdge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
            bool east = _resizeEdge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight;
            bool north = _resizeEdge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;
            bool south = _resizeEdge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;

            if (west)
            {
                dx = Math.Min(dx, width - minWidth);
                left += dx;
                width -= dx;
            }
            else if (east)
            {
                width = Math.Max(minWidth, width + dx);
            }

            if (north)
            {
                if (minHeight > 0)
                    dy = Math.Min(dy, height - minHeight);
                top += dy;
                height -= dy;
            }
            else if (south)
            {
                height = minHeight > 0 ? Math.Max(minHeight, height + dy) : height + dy;
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        private static ResizeEdge ParseResizeEdge(object tag) => tag switch
        {
            "Left" => ResizeEdge.Left,
            "Right" => ResizeEdge.Right,
            "Top" => ResizeEdge.Top,
            "Bottom" => ResizeEdge.Bottom,
            "TopLeft" => ResizeEdge.TopLeft,
            "TopRight" => ResizeEdge.TopRight,
            "BottomLeft" => ResizeEdge.BottomLeft,
            "BottomRight" => ResizeEdge.BottomRight,
            _ => ResizeEdge.None
        };

        private enum ResizeEdge
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private static System.Windows.Media.Effects.DropShadowEffect CreateWindowShadow()
        {
            return new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.55,
                Color = System.Windows.Media.Color.FromRgb(0, 0, 0)
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_allowExit || e.Cancel)
                return;

            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();

            if (_trayIcon != null)
                _trayIcon.Visible = true;

            if (_trayHintShown || _trayIcon == null)
                return;

            _trayHintShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(
                    2500,
                    "TypeFix",
                    UiLanguage.Get("TrayBalloon"),
                    WF.ToolTipIcon.None);
            }
            catch
            {
                // Windows can suppress balloon tips.
            }
        }

        private void SetStatus(string message, bool isError = false)
        {
            StatusText.Text = message;
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(isError ? "StatusErrorBrush" : "StatusOkBrush");
        }

        private void CopyToClipboard(string text, string successMessage)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                SetStatus(successMessage);
            }
            catch
            {
                SetStatus(UiLanguage.Get("StatusCopyFailed"), isError: true);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
            _hwndSource.AddHook(WndProc);

            var handle = _hwndSource.Handle;
            bool ok = RegisterHotKey(handle, HOTKEY_ID, MOD_NONE, WF.Keys.F10);
            if (!ok)
            {
                SetStatus(UiLanguage.Get("StatusHotkeyFailed"), isError: true);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
                _hwndSource.RemoveHook(WndProc);
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayUiFont?.Dispose();
            _trayUiFont = null;

            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                handled = true;
                // Fire-and-forget: do not block the window message pump with Sleep.
                _ = ProcessSelectionAsync();
            }

            return IntPtr.Zero;
        }

        #endregion

        #region Core: selection processing

        private const int ClipboardCopyTimeoutMs = 500;
        private const int ClipboardSetTimeoutMs = 300;
        private const int ClipboardPollMs = 20;
        private const int PasteSettleMs = 180;

        private async Task ProcessSelectionAsync()
        {
            // Ignore overlapping F10 presses while a conversion is in flight.
            if (Interlocked.CompareExchange(ref _isProcessingSelection, 1, 0) != 0)
                return;

            string? previousText = null;

            try
            {
                // Return from WM_HOTKEY first. SendKeys("^c") is layout-sensitive: with Persian
                // active it injects the character "c" and Notepad replaces the selection.
                await Task.Yield();
                await WaitUntilKeyUpAsync(VK_F10, timeoutMs: 800).ConfigureAwait(true);

                // Clipboard + synthesized input need the STA UI thread.
                previousText = TryGetClipboardText();

                // Clear first so we can detect when Ctrl+C actually lands.
                TryClearClipboard();

                SendCtrlCombo(VK_C);

                string? original = await WaitForClipboardTextAsync(
                    timeoutMs: ClipboardCopyTimeoutMs,
                    pollMs: ClipboardPollMs,
                    accept: text => !string.IsNullOrEmpty(text)).ConfigureAwait(true);

                if (string.IsNullOrEmpty(original))
                {
                    RestoreClipboardText(previousText);
                    return;
                }

                // CPU-bound mapping off the UI thread.
                string converted = await Task.Run(() => _engine.Convert(original)).ConfigureAwait(true);
                if (converted == original)
                {
                    RestoreClipboardText(previousText);
                    return;
                }

                if (!await TrySetClipboardTextAsync(converted, ClipboardSetTimeoutMs, ClipboardPollMs).ConfigureAwait(true))
                {
                    RestoreClipboardText(previousText);
                    return;
                }

                SendCtrlCombo(VK_V);

                // Let the target app finish reading the clipboard before we restore it.
                await Task.Delay(PasteSettleMs).ConfigureAwait(true);

                RestoreClipboardText(previousText);
                SetStatus(UiLanguage.Format("StatusConvertedSelection", _catalog.Label(_catalog.Active)));
            }
            catch
            {
                RestoreClipboardText(previousText);
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingSelection, 0);
            }
        }

        /// <summary>
        /// Ctrl+key via virtual-key codes. Independent of the active keyboard layout.
        /// </summary>
        private static void SendCtrlCombo(ushort key)
        {
            INPUT[] inputs =
            [
                KeyInput(VK_CONTROL, keyUp: false),
                KeyInput(key, keyUp: false),
                KeyInput(key, keyUp: true),
                KeyInput(VK_CONTROL, keyUp: true)
            ];

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private static INPUT KeyInput(ushort vk, bool keyUp)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                    }
                }
            };
        }

        private static async Task WaitUntilKeyUpAsync(int vk, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while ((GetAsyncKeyState(vk) & 0x8000) != 0 && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(10).ConfigureAwait(true);
        }

        private static string? TryGetClipboardText()
        {
            try
            {
                return WF.Clipboard.ContainsText() ? WF.Clipboard.GetText() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void TryClearClipboard()
        {
            try
            {
                WF.Clipboard.Clear();
            }
            catch
            {
                // ignore
            }
        }

        private static void RestoreClipboardText(string? previousText)
        {
            try
            {
                if (previousText != null)
                    WF.Clipboard.SetText(previousText);
                else
                    WF.Clipboard.Clear();
            }
            catch
            {
                // ignore
            }
        }

        private static async Task<bool> TrySetClipboardTextAsync(string text, int timeoutMs, int pollMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    WF.Clipboard.SetText(text);
                    if (WF.Clipboard.ContainsText() && WF.Clipboard.GetText() == text)
                        return true;
                }
                catch
                {
                    // clipboard busy; retry
                }

                await Task.Delay(pollMs).ConfigureAwait(true);
            }

            return false;
        }

        private static async Task<string?> WaitForClipboardTextAsync(int timeoutMs, int pollMs, Func<string, bool> accept)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (WF.Clipboard.ContainsText())
                    {
                        string text = WF.Clipboard.GetText();
                        if (accept(text))
                            return text;
                    }
                }
                catch
                {
                    // clipboard busy; retry
                }

                await Task.Delay(pollMs).ConfigureAwait(true);
            }

            return null;
        }

        #endregion

        #region Try converter

        private async void TryConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string input = TryInputBox.Text;
            if (string.IsNullOrEmpty(input))
            {
                SetStatus(UiLanguage.Get("StatusTryEmpty"), isError: true);
                TryInputBox.Focus();
                return;
            }

            TryConvertButton.IsEnabled = false;
            try
            {
                string converted = await Task.Run(() => _engine.Convert(input)).ConfigureAwait(true);
                TryOutputBox.Text = converted;

                if (converted == input)
                    SetStatus(UiLanguage.Get("StatusTryUnchanged"));
                else
                    SetStatus(UiLanguage.Get("StatusTryConverted"));
            }
            finally
            {
                TryConvertButton.IsEnabled = true;
            }
        }

        private void TrySwitchButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TryInputBox.Text) && string.IsNullOrEmpty(TryOutputBox.Text))
            {
                SetStatus(UiLanguage.Get("StatusTryNothingToSwitch"), isError: true);
                TryInputBox.Focus();
                return;
            }

            string original = TryInputBox.Text;
            TryInputBox.Text = TryOutputBox.Text;
            TryOutputBox.Text = original;

            TryInputBox.Focus();
            TryInputBox.CaretIndex = TryInputBox.Text.Length;
            SetStatus(UiLanguage.Get("StatusTrySwapped"));
        }

        private void TryCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TryOutputBox.Text))
            {
                SetStatus(UiLanguage.Get("StatusTryNothingToCopy"), isError: true);
                return;
            }

            CopyToClipboard(TryOutputBox.Text, UiLanguage.Get("StatusTryCopied"));
        }

        private void TryClearButton_Click(object sender, RoutedEventArgs e)
        {
            TryInputBox.Clear();
            TryOutputBox.Clear();
            TryInputBox.Focus();
            SetStatus(UiLanguage.Get("StatusTryCleared"));
        }

        private void TryInputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                TryConvertButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void TryInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SyncNoteBox(TryInputBox, TryInputPlaceholder);
        }

        private void TryOutputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SyncNoteBox(TryOutputBox, TryOutputPlaceholder);
        }

        private static void SyncNoteBox(System.Windows.Controls.TextBox box, System.Windows.Controls.TextBlock placeholder)
        {
            placeholder.Visibility = string.IsNullOrEmpty(box.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            box.FlowDirection = IsMostlyRightToLeft(box.Text)
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight;
        }

        private static bool IsMostlyRightToLeft(string text)
        {
            int rtl = 0;
            int ltr = 0;

            foreach (char c in text)
            {
                if (c is >= '\u0590' and <= '\u08FF')
                    rtl++;
                else if (char.IsLetter(c))
                    ltr++;
            }

            return rtl > ltr;
        }

        #endregion

        private void ApplyLanguageChrome()
        {
            FontFamily = UiLanguage.UsesVazirmatn
                ? (System.Windows.Media.FontFamily)FindResource("PersianFont")
                : new System.Windows.Media.FontFamily(UiLanguage.UiFontName);
            ContentRoot.FlowDirection = UiLanguage.Flow;
            ApplyTrayFont();
            MaximizeButton.ToolTip = UiLanguage.Get(_isPseudoMaximized ? "TooltipRestore" : "TooltipMaximize");

            if (_trayOpenItem != null)
                _trayOpenItem.Text = UiLanguage.Get("TrayOpen");
            if (_trayExitItem != null)
                _trayExitItem.Text = UiLanguage.Get("TrayExit");
            if (_trayMenu != null)
                _trayMenu.RightToLeft = UiLanguage.IsRightToLeft ? WF.RightToLeft.Yes : WF.RightToLeft.No;

            SyncNoteBox(TryInputBox, TryInputPlaceholder);
            SyncNoteBox(TryOutputBox, TryOutputPlaceholder);
            GeneralLayouts.Refresh();
            SettingsLayouts.Refresh();
            if (_trayIcon != null)
                _trayIcon.Text = TrayTip(_catalog.Label(_catalog.Active));
        }

        private void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange)
                return;
            if (LanguageCombo.SelectedValue is not string code || code == UiLanguage.Current)
                return;

            UiLanguage.Apply(code);
            ApplyLanguageChrome();
            SetStatus(UiLanguage.Get("StatusLanguageChanged"));
        }

        #region Settings handlers

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = StartupToggle.IsChecked == true;
            StartupManager.SetStartup(enabled);
            SetStatus(UiLanguage.Get(enabled ? "StatusStartupOn" : "StatusStartupOff"));
        }

        private void ApplyTrayFont()
        {
            if (_trayMenu == null)
                return;

            if (UiLanguage.UsesVazirmatn)
            {
                _vazirmatnMenuFont ??= CreateVazirmatnMenuFont();
                if (_vazirmatnMenuFont != null)
                {
                    _trayMenu.Font = _vazirmatnMenuFont;
                    DisposeTrayUiFont();
                    return;
                }
            }

            string family = UiLanguage.UiFontName.Split(',')[0].Trim();
            System.Drawing.Font? next = null;
            try
            {
                next = new System.Drawing.Font(family, 9f);
            }
            catch
            {
                next = null;
            }

            if (next == null)
            {
                _trayMenu.Font = System.Drawing.SystemFonts.MenuFont;
                DisposeTrayUiFont();
                return;
            }

            _trayMenu.Font = next;
            DisposeTrayUiFont();
            _trayUiFont = next;
        }

        private void DisposeTrayUiFont()
        {
            if (_trayUiFont == null)
                return;

            System.Drawing.Font font = _trayUiFont;
            _trayUiFont = null;
            font.Dispose();
        }

        private System.Drawing.Font? CreateVazirmatnMenuFont()
        {
            try
            {
                var info = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Fonts/Vazirmatn-Regular.ttf"));
                if (info?.Stream == null)
                    return null;

                using var stream = info.Stream;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                byte[] bytes = buffer.ToArray();
                _vazirmatnPin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                _vazirmatnFonts = new System.Drawing.Text.PrivateFontCollection();
                _vazirmatnFonts.AddMemoryFont(_vazirmatnPin.AddrOfPinnedObject(), bytes.Length);
                return new System.Drawing.Font(_vazirmatnFonts.Families[0], 9f);
            }
            catch
            {
                return null;
            }
        }

        private void CopyLayoutSample_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(LayoutSampleJson, UiLanguage.Get("StatusLayoutSampleCopied"));
        }

        private void CopyWebsite_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(WebsiteUrl, UiLanguage.Get("StatusWebsiteCopied"));
        }

        private void CopyContact_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(GitHubUrl, UiLanguage.Get("StatusGitHubCopied"));
        }

        private void SupportCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DonateUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void WebsiteText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = WebsiteUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ContactText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GitHubUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }
    }

    // Startup manager as before
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "TypeFix";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var value = key?.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartup(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                               ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

                if (enabled)
                {
                    string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
