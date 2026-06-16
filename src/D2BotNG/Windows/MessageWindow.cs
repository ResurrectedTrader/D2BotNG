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
/// Owns a hidden message-only window that receives WM_COPYDATA messages from D2BS.
/// Created early in startup so the HWND is stable for the full process lifetime
/// (handoff rehydration relies on this).
/// </summary>
public class MessageWindow : IDisposable
{
    private readonly ILogger<MessageWindow> _logger;
    private readonly Channel<D2BSMessage> _messageChannel;
    private nint _wndProcPtr;
    private WndProcDelegate? _wndProcDelegate;
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
    /// Creates the message-only window. Call once from Program.Main before any hosted
    /// service runs — handoff rehydration reads Handle.
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

        _logger.LogDebug("Created message-only window with handle: {Handle}", Handle);
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

            _logger.LogDebug("WM_COPYDATA received: sender={Sender}, type={Type}, len={Len}, data={Data}",
                wParam, messageType, copyData.cbData, data);

            // Normalize heartbeat event.
            if (messageType == MessageType.Heartbeat || data.Contains("heartBeat"))
            {
                data = JsonSerializer.Serialize(new ProfileMessage
                {
                    Function = "heartBeat"
                });
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

        if (Handle != 0)
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

    public override string ToString() =>
        $"ProfileMessage {{ Profile = {Profile}, Function = {Function}, Arguments = [{string.Join(", ", Arguments)}] }}";
}

/// <summary>
/// Represents a message received from D2BS via WM_COPYDATA.
/// </summary>
public record D2BSMessage
{
    public nint SenderHandle { get; init; }
    public required ProfileMessage Message { get; init; }
}
