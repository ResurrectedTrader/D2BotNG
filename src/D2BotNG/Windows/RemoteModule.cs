using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using D2BotNG.Logging;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;
using ILogger = Serilog.ILogger;

namespace D2BotNG.Windows;

/// <summary>
/// Resolves system-DLL export addresses (e.g. kernel32!LoadLibraryW) and module bases valid inside a
/// target game process — the inputs needed to drive injection/patching via CreateRemoteThread.
///
/// <para>
/// The classic shortcut — <c>GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW")</c> — is only
/// valid in the target when it is the same bitness as us, since system DLLs load at the same base in every
/// same-flavour process for a boot session. The x64 manager keeps that fast path for x64 targets. For a
/// 32-bit (WOW64) game the 32-bit kernel32 is a different image at a different base — but that base is shared
/// across <em>all</em> WOW64 processes this boot, so the address is read once from a peer WOW64 process
/// (walking its PE export table over ReadProcessMemory, following forwarders) and cached. See
/// <see cref="ResolveExportForTarget"/>.
/// </para>
///
/// <para>
/// <see cref="GetModuleBase"/> reads the target's own loader list and is meant for use after a module is
/// mapped (e.g. a patch-target base). Handles must carry <c>PROCESS_QUERY_INFORMATION</c> and
/// <c>PROCESS_VM_READ</c>. Cold-start caveat: cross-bitness resolution needs at least one accessible WOW64
/// process to read from; if none is running yet (e.g. the very first 32-bit launch) it returns 0.
/// </para>
/// </summary>
public static class RemoteModule
{
    private static readonly ILogger Logger = TrackingLoggerFactory.ForContext(typeof(RemoteModule));

    // PE optional-header magic values.
    private const ushort OptionalHeaderMagicPe32Plus = 0x20B; // 64-bit image (PE32+)
    private const int DataDirectoryOffsetPe32 = 96;           // IMAGE_OPTIONAL_HEADER32.DataDirectory
    private const int DataDirectoryOffsetPe32Plus = 112;      // IMAGE_OPTIONAL_HEADER64.DataDirectory

    // 32-bit (WOW64) system-DLL export addresses are identical across every WOW64 process for a boot
    // session, so resolve them once (from a peer process) and cache for reuse.
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<string, nint> Wow64Exports = new(StringComparer.Ordinal);

    /// <summary>
    /// Finds the base address of a module loaded in the target process, matched by file name
    /// (case-insensitive, e.g. "kernel32.dll"). Returns 0 if it is not currently loaded.
    /// </summary>
    public static nint GetModuleBase(nint hProcess, string moduleName)
    {
        var capacity = 256;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var modules = new nint[capacity];
            var cb = (uint)(modules.Length * IntPtr.Size);
            if (!EnumProcessModulesEx(hProcess, modules, cb, out var needed, LIST_MODULES_ALL))
            {
                Logger.Error("EnumProcessModulesEx failed for {Module} (error {Error})", moduleName, Marshal.GetLastWin32Error());
                return 0;
            }

            var available = (int)(needed / (uint)IntPtr.Size);
            if (available > modules.Length)
            {
                // Module set grew between the size report and the read — retry with the exact size.
                capacity = available;
                continue;
            }

            var sb = new StringBuilder(1024);
            for (var i = 0; i < available; i++)
            {
                sb.Clear();
                if (GetModuleFileNameExW(hProcess, modules[i], sb, (uint)sb.Capacity) == 0) continue;
                if (string.Equals(Path.GetFileName(sb.ToString()), moduleName, StringComparison.OrdinalIgnoreCase))
                    return modules[i];
            }

            return 0;
        }

        return 0;
    }

    /// <summary>
    /// Resolves an exported function address valid in <paramref name="targetHandle"/>'s address space,
    /// picking a strategy by the target's bitness (the manager is built x64):
    /// <list type="bullet">
    /// <item>Same bitness (x64 target): reuse our own module — system DLLs load at the same base in every
    /// same-flavour process this boot, so <c>GetProcAddress(GetModuleHandle(...))</c> is valid in the target
    /// too, and works even while it is suspended.</item>
    /// <item>Cross bitness (32-bit/WOW64 target): the 32-bit module isn't ours and isn't yet mapped in a
    /// suspended target, but it loads at the same base in every WOW64 process this boot — so read the address
    /// out of a peer WOW64 process and cache it.</item>
    /// </list>
    /// Returns 0 if it cannot be resolved.
    /// </summary>
    public static nint ResolveExportForTarget(nint targetHandle, string moduleName, string functionName)
    {
        if (!IsWow64Process(targetHandle, out var targetIsWow64))
        {
            Logger.Warning("IsWow64Process failed (error {Error}); assuming same bitness as the manager", Marshal.GetLastWin32Error());
            targetIsWow64 = false;
        }

        // Manager is x64-native, so a non-WOW64 target is also x64: same flavour, shared module base.
        if (!targetIsWow64)
            return GetProcAddress(GetModuleHandle(moduleName), functionName);

        return GetWow64Export(moduleName, functionName);
    }

    /// <summary>
    /// Resolves a 32-bit (WOW64) module export address, identical in every WOW64 process this boot. Read
    /// once from any accessible peer WOW64 process and cached. Returns 0 if none can be read (e.g. a cold
    /// start with no other 32-bit process running yet).
    /// </summary>
    private static nint GetWow64Export(string moduleName, string functionName)
    {
        var cacheKey = moduleName + "!" + functionName;
        lock (CacheLock)
        {
            if (Wow64Exports.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var address = ResolveFromPeerWow64Process(moduleName, functionName);
        if (address != 0)
            lock (CacheLock)
            {
                Wow64Exports[cacheKey] = address;
            }

        return address;
    }

    private static nint ResolveFromPeerWow64Process(string moduleName, string functionName)
    {
        foreach (var process in Process.GetProcesses())
        {
            nint handle = 0;
            try
            {
                handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, process.Id);
                if (handle == 0) continue;
                if (!IsWow64Process(handle, out var isWow64) || !isWow64) continue;

                var moduleBase = GetModuleBase(handle, moduleName);
                if (moduleBase == 0) continue; // not loaded here (e.g. a still-suspended process)

                var address = ResolveExport(handle, moduleBase, functionName, depth: 0);
                if (address != 0)
                {
                    Logger.Debug("Resolved 32-bit {Module}!{Function} from peer WOW64 PID {Pid}", moduleName, functionName, process.Id);
                    return address;
                }
            }
            catch
            {
                // Process may have exited or be inaccessible; try the next one.
            }
            finally
            {
                if (handle != 0) CloseHandle(handle);
                process.Dispose();
            }
        }

        Logger.Error("Could not resolve 32-bit {Module}!{Function}: no readable WOW64 process available", moduleName, functionName);
        return 0;
    }

    private static nint ResolveExport(nint hProcess, nint moduleBase, string functionName, int depth)
    {
        if (depth > 8) return 0; // guard against pathological forwarder chains

        // IMAGE_DOS_HEADER.e_lfanew (offset 0x3C) → IMAGE_NT_HEADERS.
        var ntHeaders = moduleBase + ReadInt32(hProcess, moduleBase + 0x3C);

        // NT headers: Signature (4) + IMAGE_FILE_HEADER (20) + IMAGE_OPTIONAL_HEADER.
        var optionalHeader = ntHeaders + 4 + 20;
        var magic = ReadUInt16(hProcess, optionalHeader);
        var dataDirectory = optionalHeader + (magic == OptionalHeaderMagicPe32Plus ? DataDirectoryOffsetPe32Plus : DataDirectoryOffsetPe32);

        // Data directory entry 0 is the export table (VirtualAddress, Size).
        var exportRva = ReadUInt32(hProcess, dataDirectory);
        var exportSize = ReadUInt32(hProcess, dataDirectory + 4);
        if (exportRva == 0) return 0;

        var exportDir = moduleBase + (nint)exportRva;
        var numberOfNames = ReadUInt32(hProcess, exportDir + 0x18);
        var addressOfFunctions = ReadUInt32(hProcess, exportDir + 0x1C);
        var addressOfNames = ReadUInt32(hProcess, exportDir + 0x20);
        var addressOfNameOrdinals = ReadUInt32(hProcess, exportDir + 0x24);

        for (uint i = 0; i < numberOfNames; i++)
        {
            var nameRva = ReadUInt32(hProcess, moduleBase + (nint)addressOfNames + (nint)(i * 4));
            if (!string.Equals(ReadAnsiString(hProcess, moduleBase + (nint)nameRva, 256), functionName, StringComparison.Ordinal))
                continue;

            var ordinal = ReadUInt16(hProcess, moduleBase + (nint)addressOfNameOrdinals + (nint)(i * 2));
            var functionRva = ReadUInt32(hProcess, moduleBase + (nint)addressOfFunctions + ordinal * 4);

            // A function RVA that points back inside the export directory is a forwarder string
            // ("TargetDll.TargetFunction"), not code — resolve it in the forwarded module instead.
            if (functionRva >= exportRva && functionRva < exportRva + exportSize)
            {
                var forwarder = ReadAnsiString(hProcess, moduleBase + (nint)functionRva, 256);
                var dot = forwarder.IndexOf('.');
                if (dot <= 0) return 0;

                var forwardModule = forwarder[..dot] + ".dll";
                var forwardFunction = forwarder[(dot + 1)..];
                var forwardBase = GetModuleBase(hProcess, forwardModule);
                if (forwardBase == 0) return 0;

                return ResolveExport(hProcess, forwardBase, forwardFunction, depth + 1);
            }

            return moduleBase + (nint)functionRva;
        }

        return 0;
    }

    private static int ReadInt32(nint hProcess, nint address) => (int)ReadUInt32(hProcess, address);

    private static uint ReadUInt32(nint hProcess, nint address) => BitConverter.ToUInt32(ReadExact(hProcess, address, 4), 0);

    private static ushort ReadUInt16(nint hProcess, nint address) => BitConverter.ToUInt16(ReadExact(hProcess, address, 2), 0);

    // Reads exactly count bytes or throws. A short read means the walk hit unmapped/freed memory (e.g.
    // the donor process exited mid-walk), so resolution must abort rather than derive an address from a
    // partial/zero value — ResolveFromPeerWow64Process catches this and moves on to the next process.
    private static byte[] ReadExact(nint hProcess, nint address, int count)
    {
        var buffer = new byte[count];
        if (!ReadProcessMemory(hProcess, address, buffer, (nuint)count, out var read) || (int)read != count)
            throw new InvalidOperationException($"ReadProcessMemory at 0x{address:X} ({count} bytes) failed (error {Marshal.GetLastWin32Error()})");
        return buffer;
    }

    private static string ReadAnsiString(nint hProcess, nint address, int maxLength)
    {
        var buffer = new byte[maxLength];
        ReadProcessMemory(hProcess, address, buffer, (nuint)maxLength, out var read);
        var count = (int)read;

        if (count <= 0)
        {
            // The full span may straddle an unmapped page; fall back to a short read at the start.
            buffer = new byte[Math.Min(maxLength, 64)];
            if (!ReadProcessMemory(hProcess, address, buffer, (nuint)buffer.Length, out read) || (int)read == 0)
                return "";
            count = (int)read;
        }

        var end = Array.IndexOf(buffer, (byte)0, 0, count);
        if (end < 0) end = count;
        return Encoding.ASCII.GetString(buffer, 0, end);
    }
}
