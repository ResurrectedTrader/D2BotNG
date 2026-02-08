using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using D2BotNG.Converters;
using JetBrains.Annotations;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public enum MessageType
{
    Mule = 0,
    GameInfo = 2,
    LastError = 4,
    Emit = 420,
    Irc = 0x411,
    UploadItem = 0x9FF,
    Profile = 0x666,
    ExecuteScript = 0x1337,
    SetProfile = 0x31337,
    Heartbeat = 0xBBBB,
    DataRetrieve = 0xF124
}

/// <summary>
/// Manages a window handle for receiving WM_COPYDATA messages from D2BS.
/// In GUI mode, uses MainForm.Handle. In headless mode, creates a message-only window.
/// </summary>
public class MessageWindow : IDisposable
{
    private readonly ILogger<MessageWindow> _logger;
    private readonly Channel<D2BSMessage> _messageChannel;
    private nint _wndProcPtr;
    private WndProcDelegate? _wndProcDelegate;
    private bool _ownsWindow;
    private bool _disposed;

    public MessageWindow(ILogger<MessageWindow> logger)
    {
        _logger = logger;
        _messageChannel = Channel.CreateUnbounded<D2BSMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    /// <summary>
    /// The window handle to pass to game processes via -handle argument.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// Channel reader for processing incoming D2BS messages.
    /// </summary>
    public ChannelReader<D2BSMessage> Messages => _messageChannel.Reader;

    /// <summary>
    /// Set the handle to use (call this in GUI mode with MainForm.Handle).
    /// </summary>
    public void SetHandle(nint handle)
    {
        if (_ownsWindow && Handle != 0)
        {
            DestroyWindow(Handle);
            _ownsWindow = false;
        }

        Handle = handle;
        _logger.LogDebug("MessageWindow using external handle: {Handle}", handle);
    }

    /// <summary>
    /// Create a message-only window for headless mode.
    /// </summary>
    public void CreateMessageOnlyWindow()
    {
        if (Handle != 0)
        {
            _logger.LogWarning("MessageWindow already has a handle");
            return;
        }

        // Keep delegate alive
        _wndProcDelegate = WndProc;
        _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

        // Register window class
        var className = "D2BotNG_MessageWindow_" + Guid.NewGuid().ToString("N")[..8];
        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _wndProcPtr,
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };

        var atom = RegisterClassExW(ref wndClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to register window class: {error}");
        }

        // Create message-only window
        Handle = CreateWindowExW(
            0, className, "D2BotNG", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, 0, GetModuleHandle(null), 0);

        if (Handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to create message window: {error}");
        }

        _ownsWindow = true;
        _logger.LogInformation("Created message-only window with handle: {Handle}", Handle);
    }

    /// <summary>
    /// Process an incoming WM_COPYDATA message. Call from WndProc.
    /// </summary>
    public void HandleCopyData(nint wParam, nint lParam)
    {
        try
        {
            var copyData = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            var bytes = new byte[copyData.cbData];
            Marshal.Copy(copyData.lpData, bytes, 0, copyData.cbData);

            // Remove null terminator if present
            var length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0) length--;

            var messageType = (MessageType)copyData.dwData.ToInt64();
            var data = Encoding.UTF8.GetString(bytes, 0, length);

            // Normalize heartbeat event.
            if (messageType == MessageType.Heartbeat || data.Contains("heartBeat"))
            {
                data = JsonSerializer.Serialize(new ProfileMessage
                {
                    Function = "heartBeat"
                }
                );
            }

            try
            {
                var message = new D2BSMessage
                {
                    SenderHandle = wParam,
                    Message = JsonSerializer.Deserialize<ProfileMessage>(data)!
                };

                if (!_messageChannel.Writer.TryWrite(message))
                {
                    _logger.LogWarning("Failed to queue D2BS message");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WM_COPYDATA for data {data}", data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WM_COPYDATA");
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg != WM_COPYDATA)
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        HandleCopyData(wParam, lParam);
        return 1;

    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _messageChannel.Writer.Complete();

        if (_ownsWindow && Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }
    }
}

/// <summary>
/// Represents a JSON message received from D2BS via WM_COPYDATA that was serialized using JSON.
/// </summary>
public record ProfileMessage
{
    [JsonPropertyName("profile")] public string? Profile { get; set; }

    [JsonPropertyName("func")] public string? Function { get; set; }

    [JsonPropertyName("args")]
    [JsonConverter(typeof(StringListCoercingConverter))]
    public string[] Arguments { get; set; } = [];
}

/// <summary>
/// Represents a message received from D2BS via WM_COPYDATA.
/// </summary>
public record D2BSMessage
{
    public nint SenderHandle { get; init; }
    public required ProfileMessage Message { get; init; }
}
