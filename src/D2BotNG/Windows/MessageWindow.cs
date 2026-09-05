using System.Buffers;
using System.Collections.Concurrent;
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
/// <remarks>
/// The window lives on its own dedicated pump thread, and its WndProc does nothing but
/// copy the payload and queue it. Both halves of that matter, because a sender is blocked
/// for the whole of the WndProc: D2BS sends WM_COPYDATA with a bare <c>SendMessageW</c>
/// (no timeout), so the game's thread stalls until this process's pump has serviced it.
///
/// The window used to be created on the main thread, which then went on to run
/// <c>Application.Run</c> — so every game was queueing behind WebView2, WinForms painting
/// and the titlebar drag loop, on top of each other. And the WndProc itself decoded UTF-8,
/// scanned the whole payload for "heartBeat" and ran a full <c>JsonSerializer.Deserialize</c>
/// of an envelope whose single argument is the entire escaped characterState snapshot —
/// all of it O(payload), all of it with N games waiting their turn. That is what made the
/// per-second capture hitch scale with instance count (d2bsng#11).
///
/// A dedicated thread also means headless mode has a real pump. It previously had none at
/// all and worked only because <c>Main</c> is <c>[STAThread]</c> and a managed blocking wait
/// on an STA thread happens to dispatch inter-thread sent messages.
/// </remarks>
public class MessageWindow : IDisposable
{
    private readonly ILogger<MessageWindow> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Channel<D2BSRawMessage> _messageChannel;

    /// <summary>
    /// When each sender last reported in, stamped on the message pump. Liveness must not be a
    /// function of dispatch latency: the dispatch queue is shared by the whole fleet and does
    /// slow work (renders, file writes, profile restarts), so a heartbeat stamped when it
    /// reaches the front of that queue says as much about the manager as about the bot.
    /// </summary>
    private readonly ConcurrentDictionary<nint, DateTime> _lastHeartbeatAt = new();

    private nint _wndProcPtr;
    private WndProcDelegate? _wndProcDelegate;
    private Thread? _pumpThread;
    /// <summary>Written by Dispose, read on the pump thread — hence volatile.</summary>
    private volatile bool _disposed;

    public MessageWindow(ILogger<MessageWindow> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
        _messageChannel = Channel.CreateUnbounded<D2BSRawMessage>(new UnboundedChannelOptions
        {
            // One reader: the consumer owns each payload buffer until it releases it, and
            // per-sender ordering only holds while a single reader drains the queue.
            SingleReader = true,
            SingleWriter = true
        });
    }

    /// <summary>
    /// The window handle to pass to game processes via -handle argument.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// Channel reader for processing incoming D2BS messages. Items are raw payloads: decode and
    /// parse them with <see cref="Parse"/>, then hand the buffer back with
    /// <see cref="D2BSRawMessage.Release"/>.
    /// </summary>
    public ChannelReader<D2BSRawMessage> Messages => _messageChannel.Reader;

    /// <summary>
    /// When the given sender last sent a heartbeat, as observed on the message pump.
    /// </summary>
    public bool TryGetLastHeartbeat(nint senderHandle, out DateTime at) =>
        _lastHeartbeatAt.TryGetValue(senderHandle, out at);

    /// <summary>
    /// Drops a sender's recorded liveness. Called when a profile's routing entry is removed so
    /// the map tracks running profiles instead of accreting a row per game ever launched.
    /// </summary>
    public void ForgetHandle(nint senderHandle) => _lastHeartbeatAt.TryRemove(senderHandle, out _);

    /// <summary>
    /// Creates the message-only window on its own pump thread and blocks until its handle is
    /// valid. Call once from Program.Main before any hosted service runs — handoff rehydration
    /// reads Handle.
    /// </summary>
    public void CreateMessageOnlyWindow()
    {
        if (Handle != 0)
        {
            _logger.LogWarning("MessageWindow already has a handle");
            return;
        }

        using var ready = new ManualResetEventSlim(false);
        Exception? startupError = null;

        _pumpThread = new Thread(() =>
        {
            try
            {
                CreateWindow();
            }
            catch (Exception ex)
            {
                startupError = ex;
                return;
            }
            finally
            {
                // Signalled whether or not creation succeeded, so a failure surfaces as the
                // exception below instead of hanging startup.
                // ReSharper disable once AccessToDisposedClosure — Set happens-before the Wait returns
                ready.Set();
            }

            RunMessageLoop();
        })
        {
            Name = "D2BotNG WM_COPYDATA pump",
            IsBackground = true
        };

        _pumpThread.Start();
        ready.Wait();

        if (startupError != null)
        {
            _pumpThread = null;
            throw startupError;
        }
    }

    /// <summary>
    /// Registers the window class and creates the window. Runs on the pump thread — a window
    /// belongs to the thread that created it, and only that thread's loop dispatches its
    /// messages.
    /// </summary>
    private void CreateWindow()
    {
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
        var handle = CreateWindowExW(
            0, className, "D2BotNG", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, 0, GetModuleHandle(null), 0);

        if (handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to create message window: {error}");
        }

        // Published last, and after the log rather than before it: a throwing sink between the
        // two would leave a non-zero Handle behind a thread that is about to exit, and the
        // re-entry guard in CreateMessageOnlyWindow would then answer a retry with a warning
        // and no pump.
        _logger.LogDebug("Created message-only window with handle: {Handle}", handle);
        Handle = handle;
    }

    /// <summary>
    /// The pump. Runs until the window is destroyed (Dispose posts WM_CLOSE, whose WM_DESTROY
    /// posts the WM_QUIT that ends this loop).
    /// </summary>
    /// <remarks>
    /// Any exit that Dispose did not ask for takes the whole process down, which is not
    /// dramatics. D2BS sends with a bare <c>SendMessageW</c> and no timeout, so a dead pump
    /// behind a live HWND blocks every game forever on its next send — heartbeat thread
    /// included, so the watchdog restarts each game into the same wedge while the manager keeps
    /// serving the UI and looks healthy. Exiting destroys the window, and a send to a dead HWND
    /// fails immediately instead of hanging. This is only a risk because the pump moved off the
    /// main thread: it used to BE the process's pump, so its death was the process's death.
    /// </remarks>
    private void RunMessageLoop()
    {
        try
        {
            while (true)
            {
                var result = GetMessageW(out var msg, 0, 0, 0);
                if (result == 0)
                {
                    break; // WM_QUIT
                }

                if (result == -1)
                {
                    _logger.LogError("GetMessage failed on the WM_COPYDATA pump: {Error}",
                        Marshal.GetLastWin32Error());
                    break;
                }

                // No TranslateMessage: a message-only window receives no keyboard input.
                DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "WM_COPYDATA pump faulted");
        }

        if (_disposed)
        {
            _logger.LogDebug("WM_COPYDATA pump stopped");
            return;
        }

        // Cleared first. The window died with the thread that owned it, and ProfileEngine keeps
        // handing this value to games and resending it on a missed heartbeat; left set, the
        // whole fleet times out, gets killed as unresponsive, and restarts into the same dead
        // HWND on a loop, with nothing in the log but "missed heartbeat".
        Handle = 0;
        _logger.LogCritical("WM_COPYDATA pump stopped unexpectedly — no game can reach the manager, shutting down");
        _lifetime.StopApplication();
    }

    /// <summary>
    /// Process an incoming WM_COPYDATA message. Call from WndProc.
    /// </summary>
    /// <remarks>
    /// Deliberately does no more than stamp a heartbeat or copy the payload out: the sending
    /// game is blocked in <c>SendMessageW</c> for exactly as long as this takes, and every
    /// other game is queued behind it. The copy itself is unavoidable — the COPYDATASTRUCT
    /// buffer is only valid for the duration of the call. Decoding and parsing happen on the
    /// consumer, in <see cref="Parse"/>.
    /// </remarks>
    public void HandleCopyData(nint wParam, nint lParam)
    {
        try
        {
            var copyData = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            var messageType = (MessageType)copyData.dwData.ToInt64();

            // Heartbeats are recorded here and never enqueued. kolbot's dedicated heartbeat
            // thread sends mode 0xBBBB once a second (threads/HeartBeat.js), so this is an O(1)
            // check that also keeps the highest-frequency message in the system off the shared
            // dispatch queue entirely. A framework that signals liveness some other way still
            // works: its message falls through, is parsed properly by the consumer, and the
            // "heartBeat" case there records it — late, but correctly.
            //
            // This deliberately does NOT treat any other message as proof of life. kolbot sends
            // console output, characterState and status updates from threads that outlive a
            // wedged main script, so counting them would mask the failure the watchdog exists
            // to catch.
            if (messageType == MessageType.Heartbeat)
            {
                _lastHeartbeatAt[wParam] = DateTime.UtcNow;
                return;
            }

            if (copyData.cbData < 0)
            {
                _logger.LogWarning("Ignoring WM_COPYDATA from {Sender} with negative length {Len}",
                    wParam, copyData.cbData);
                return;
            }

            var raw = D2BSRawMessage.Rent(wParam, messageType, copyData.cbData);
            var queued = false;
            try
            {
                if (copyData.cbData > 0)
                {
                    Marshal.Copy(copyData.lpData, raw.Buffer, 0, copyData.cbData);
                }

                queued = _messageChannel.Writer.TryWrite(raw);
                if (!queued)
                {
                    // Unbounded channel, so only a completed writer gets here (shutdown).
                    _logger.LogWarning("Failed to queue D2BS message");
                }
            }
            finally
            {
                // Ownership passes to the consumer only once the write lands. Anything that
                // throws in between (a null lpData, a logging sink) would otherwise lose the
                // rental silently, and a sender repeating the fault at 1Hz would quietly undo
                // the pooling this exists for.
                if (!queued) raw.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WM_COPYDATA");
        }
    }

    /// <summary>
    /// Decodes and parses a queued payload. Runs on the consumer, off the pump. Returns null if
    /// the payload is not a message we can read — the error is logged here rather than thrown,
    /// since one unreadable message must not stop the queue. Does not release the buffer; the
    /// caller owns it either way.
    /// </summary>
    public D2BSMessage? Parse(D2BSRawMessage raw)
    {
        var data = string.Empty;
        try
        {
            // Remove null terminator if present
            var length = raw.Length;
            while (length > 0 && raw.Buffer[length - 1] == 0) length--;

            data = Encoding.UTF8.GetString(raw.Buffer, 0, length);

            _logger.LogDebug("WM_COPYDATA received: sender={Sender}, type={Type}, len={Len}, data={Data}",
                raw.SenderHandle, raw.Type, raw.Length, data);

            // Normalize heartbeat event. Only reachable for a sender that doesn't set the
            // 0xBBBB mode (handled by the fast path in HandleCopyData) — its heartbeat comes
            // down the queue and the consumer's "heartBeat" case records it, later but
            // correctly.
            if (data.Contains("heartBeat"))
            {
                data = JsonSerializer.Serialize(new ProfileMessage
                {
                    Function = "heartBeat"
                });
            }

            return new D2BSMessage
            {
                SenderHandle = raw.SenderHandle,
                Message = JsonSerializer.Deserialize<ProfileMessage>(data)!
            };
        }
        catch (Exception ex)
        {
            // Length as well as the text: a failure in the decode itself leaves data empty, and
            // then the length is the only thing that says what arrived.
            _logger.LogError(ex, "Error handling WM_COPYDATA from {Sender} ({Len} bytes) for data {data}",
                raw.SenderHandle, raw.Length, data);
            return null;
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_COPYDATA:
                try
                {
                    HandleCopyData(wParam, lParam);
                }
                catch (Exception ex)
                {
                    // HandleCopyData catches its own, so this is the belt to that braces: an
                    // exception unwinding through DispatchMessageW's native frames would take
                    // the pump with it, and a dead pump hangs every game (see RunMessageLoop).
                    _logger.LogError(ex, "Unhandled error dispatching WM_COPYDATA");
                }

                return 1;
            case WM_CLOSE:
                // Dispose posts this. DestroyWindow has thread affinity, so it has to happen
                // here on the pump thread rather than in Dispose itself.
                DestroyWindow(hWnd);
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0); // ends RunMessageLoop
                return 0;
            default:
                return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // DestroyWindow only works from the owning thread, so ask the pump to close itself and
        // wait for the loop to unwind. The writer is completed after that, so the pump cannot
        // still be enqueueing into a completed channel.
        if (Handle != 0)
        {
            PostMessage(Handle, WM_CLOSE, 0, 0);
        }

        if (_pumpThread is { IsAlive: true } && !_pumpThread.Join(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("WM_COPYDATA pump did not stop within 5s");
        }

        _pumpThread = null;
        Handle = 0;

        _messageChannel.Writer.Complete();

        // Drain whatever the consumer will now never see, so pooled buffers go back.
        while (_messageChannel.Reader.TryRead(out var raw))
        {
            raw.Release();
        }
    }
}

/// <summary>
/// A WM_COPYDATA payload as it came off the wire, queued for the consumer to decode. Buffers
/// come from a pool because a characterState snapshot is hundreds of KB and arrives once a
/// second per running game, which is enough allocation to keep the large object heap busy.
/// </summary>
public sealed class D2BSRawMessage
{
    /// <summary>
    /// Payloads above this are allocated instead of pooled. Comfortably above a full PlugY stash
    /// snapshot, and the cap matters because <c>ArrayPool.Create</c> — unlike
    /// <c>ArrayPool&lt;T&gt;.Shared</c> — registers no Gen2 trim callback, so whatever a bucket
    /// retains it retains for the life of the process. Every doubling of this number doubles the
    /// largest bucket's permanent footprint for the sake of an outlier that is rented once.
    /// </summary>
    private const int MaxPooledPayload = 1024 * 1024;

    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Create(MaxPooledPayload, 8);

    private readonly bool _pooled;
    private int _released;

    private D2BSRawMessage(nint senderHandle, MessageType type, byte[] buffer, int length, bool pooled)
    {
        SenderHandle = senderHandle;
        Type = type;
        Buffer = buffer;
        Length = length;
        _pooled = pooled;
    }

    public nint SenderHandle { get; }

    public MessageType Type { get; }

    /// <summary>The payload buffer. May be longer than <see cref="Length"/> — it is pooled.</summary>
    public byte[] Buffer { get; }

    /// <summary>How many bytes of <see cref="Buffer"/> the sender actually wrote.</summary>
    public int Length { get; }

    public static D2BSRawMessage Rent(nint senderHandle, MessageType type, int length)
    {
        var pooled = length <= MaxPooledPayload;
        var buffer = pooled ? Pool.Rent(length) : new byte[length];
        return new D2BSRawMessage(senderHandle, type, buffer, length, pooled);
    }

    /// <summary>
    /// Hands the buffer back. A message has exactly one owner — whoever took it off the channel
    /// — and this is idempotent for that owner's benefit, not as a licence to share: returning
    /// one buffer twice hands the same array to two renters, which corrupts silently rather than
    /// throwing. Interlocked because the guard is worthless if it can itself race.
    /// </summary>
    public void Release()
    {
        if (!_pooled || Interlocked.Exchange(ref _released, 1) != 0) return;
        Pool.Return(Buffer);
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
