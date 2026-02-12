using System.Text.Json;
using D2BotNG.Core.Protos;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;
using Message = System.Windows.Forms.Message;

namespace D2BotNG.UI;

public class MainForm : Form
{
    private const int ResizeBorderSize = 3;

    // ReSharper disable InconsistentNaming — Win32 API constants
    private const int WM_SYSCOMMAND = 0x112;
    private const int SC_MINIMIZE = 0xF020;
    private const int WM_NCHITTEST = 0x84;
    private const int WM_EXITSIZEMOVE = 0x232;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    // ReSharper restore InconsistentNaming

    private readonly WebView2 _webView;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly string _serverUrl;
    private readonly SettingsRepository _settingsRepository;
    private readonly ProfileEngine _profileEngine;
    private readonly MessageWindow _messageWindow;
    private bool _isClosing;
    private Rectangle _restoreBounds;

    public MainForm(string serverUrl, SettingsRepository settingsRepository, ProfileEngine profileEngine, MessageWindow messageWindow)
    {
        _serverUrl = serverUrl;
        _settingsRepository = settingsRepository;
        _profileEngine = profileEngine;
        _messageWindow = messageWindow;

        // Form setup - borderless with resize via WM_NCHITTEST
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Icon = LoadAppIcon();

        // Load saved window position/size
        LoadWindowSettings();

        // WebView2 setup - fills entire form, resize handled via JS
        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(_webView);

        // Tray icon setup
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Show", null, OnShowClick);
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("Start All", null, OnStartAllClick);
        _trayMenu.Items.Add("Stop All", null, OnStopAllClick);
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("Exit", null, OnExitClick);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "D2BotNG",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += OnTrayDoubleClick;

        // Events
        Load += OnFormLoad;
        FormClosing += OnFormClosing;
        Resize += OnFormResize;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Text = "D2BotNG";
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 27); // zinc-900 to match app theme
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)WM_COPYDATA)
        {
            _messageWindow.HandleCopyData(m.WParam, m.LParam);
            m.Result = 1;
            return;
        }

        // Intercept system minimize (taskbar right-click, Win+D, etc.) → minimize to tray
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
        {
            MinimizeToTray();
            return;
        }

        if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
        {
            var pt = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
            var hitTest = GetHitTest(pt);
            if (hitTest != HTCLIENT)
            {
                m.Result = hitTest;
                return;
            }
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            _ = SaveWindowSettingsAsync();
        }
        base.WndProc(ref m);
    }

    private int GetHitTest(Point pt)
    {
        var left = pt.X < ResizeBorderSize;
        var right = pt.X >= ClientSize.Width - ResizeBorderSize;
        var top = pt.Y < ResizeBorderSize;
        var bottom = pt.Y >= ClientSize.Height - ResizeBorderSize;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;

        return HTCLIENT;
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        // Register this form's handle with MessageWindow for WM_COPYDATA routing
        _messageWindow.SetHandle(Handle);

        try
        {
            await _webView.EnsureCoreWebView2Async();

            // Configure WebView2
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // Listen for messages from web app
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Navigate to server
            _webView.CoreWebView2.Navigate(_serverUrl);

            // Bring window to front after WebView2 init
            Activate();
            BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize WebView2: {ex.Message}\n\nPlease ensure WebView2 Runtime is installed.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            using var doc = JsonDocument.Parse(message);
            var action = doc.RootElement.GetProperty("action").GetString();

            switch (action)
            {
                case "minimize":
                    MinimizeToTray();
                    break;

                case "maximize":
                    ToggleMaximize();
                    break;

                case "close":
                    Close();
                    break;

                case "minimizeToTray":
                    MinimizeToTray();
                    break;

                case "forceClose":
                    _isClosing = true;
                    _trayIcon.Visible = false;
                    Close();
                    break;

                case "dragStart":
                    StartDrag();
                    break;

                case "resizeStart":
                    var edge = doc.RootElement.GetProperty("edge").GetString();
                    StartResize(edge);
                    break;
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            if (_restoreBounds != Rectangle.Empty)
            {
                Bounds = _restoreBounds;
            }
        }
        else
        {
            _restoreBounds = Bounds;
            var screen = Screen.FromRectangle(Bounds);
            // MaximizedBounds position is relative to the monitor origin, not absolute
            MaximizedBounds = new Rectangle(
                screen.WorkingArea.X - screen.Bounds.X,
                screen.WorkingArea.Y - screen.Bounds.Y,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height);
            WindowState = FormWindowState.Maximized;
        }

        NotifyWindowState();
        _ = SaveWindowSettingsAsync();
    }

    private void NotifyWindowState()
    {
        var isMaximized = WindowState == FormWindowState.Maximized;
        _webView.CoreWebView2?.ExecuteScriptAsync(
            $"window.d2bot?.onWindowStateChanged?.({{ isMaximized: {isMaximized.ToString().ToLower()} }})");
    }

    private void StartDrag()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            var mousePos = Cursor.Position;
            var relativeX = (double)(mousePos.X - Left) / Width;

            _restoreBounds = _restoreBounds with { X = mousePos.X - (int)(_restoreBounds.Width * relativeX), Y = mousePos.Y - 10 };

            WindowState = FormWindowState.Normal;
            Bounds = _restoreBounds;
            NotifyWindowState();
        }

        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private void StartResize(string? edge)
    {
        if (WindowState == FormWindowState.Maximized) return;

        var hitTest = edge switch
        {
            "left" => HTLEFT,
            "right" => HTRIGHT,
            "top" => HTTOP,
            "bottom" => HTBOTTOM,
            "topLeft" => HTTOPLEFT,
            "topRight" => HTTOPRIGHT,
            "bottomLeft" => HTBOTTOMLEFT,
            "bottomRight" => HTBOTTOMRIGHT,
            _ => 0
        };

        if (hitTest != 0)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, hitTest, 0);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Allow close if _isClosing is set (forceClose was called) or if not user-initiated
        if (_isClosing || e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        // Check settings to determine behavior
        var settings = await _settingsRepository.GetAsync();
        var closeAction = settings.CloseAction;

        if (closeAction == CloseAction.Exit)
        {
            // User configured to exit - allow the close
            _isClosing = true;
            _trayIcon.Visible = false;
            return;
        }

        // Cancel the close for other actions
        e.Cancel = true;

        if (closeAction == CloseAction.MinimizeToTray)
        {
            MinimizeToTray();
        }
        else
        {
            // Ask - bring window to front and show dialog via web UI
            RestoreFromTray();
            _webView.CoreWebView2?.ExecuteScriptAsync(
                "window.dispatchEvent(new CustomEvent('d2bot-show-close-dialog'))");
        }
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        NotifyWindowState();
    }

    private void MinimizeToTray()
    {
        Hide();
        _trayIcon.ShowBalloonTip(
            3000,
            "D2BotNG",
            "D2BotNG is still running in the background. Double-click the tray icon to restore.",
            ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnShowClick(object? sender, EventArgs e)
    {
        RestoreFromTray();
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e)
    {
        RestoreFromTray();
    }

    private async void OnStartAllClick(object? sender, EventArgs e)
    {
        await _profileEngine.StartAllAsync();
    }

    private async void OnStopAllClick(object? sender, EventArgs e)
    {
        await _profileEngine.StopAllAsync();
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        _isClosing = true;
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("D2BotNG.app.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // Ignore, use default
        }
        return null;
    }

    private void LoadWindowSettings()
    {
        var settings = _settingsRepository.GetAsync().GetAwaiter().GetResult();
        var window = settings.Window;

        if (window is { Width: > 0, Height: > 0 })
        {
            // Restore saved bounds
            var bounds = new Rectangle(window.X, window.Y, window.Width, window.Height);

            // Ensure window is visible on at least one screen
            if (IsVisibleOnAnyScreen(bounds))
            {
                Bounds = bounds;
                _restoreBounds = bounds;

                if (!window.IsMaximized) return;

                var screen = Screen.FromRectangle(bounds);
                MaximizedBounds = new Rectangle(
                    screen.WorkingArea.X - screen.Bounds.X,
                    screen.WorkingArea.Y - screen.Bounds.Y,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height);
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                // Saved position not visible, center on primary screen
                CenterOnScreen();
            }
        }
        else
        {
            // No saved settings, center on screen
            CenterOnScreen();
        }
    }

    private void CenterOnScreen()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top + (screen.Height - Height) / 2;
    }

    private static bool IsVisibleOnAnyScreen(Rectangle bounds)
    {
        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
    }

    private async Task SaveWindowSettingsAsync()
    {
        var settings = await _settingsRepository.GetAsync();

        // Save the normal bounds (not maximized bounds)
        var boundsToSave = WindowState == FormWindowState.Maximized ? _restoreBounds : Bounds;

        settings.Window ??= new WindowSettings();
        settings.Window.X = boundsToSave.X;
        settings.Window.Y = boundsToSave.Y;
        settings.Window.Width = boundsToSave.Width;
        settings.Window.Height = boundsToSave.Height;
        settings.Window.IsMaximized = WindowState == FormWindowState.Maximized;

        await _settingsRepository.UpdateAsync(settings);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _webView.Dispose();
        }
        base.Dispose(disposing);
    }
}
