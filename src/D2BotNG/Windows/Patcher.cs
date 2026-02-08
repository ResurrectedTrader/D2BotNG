using System.Diagnostics;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

public class Patcher
{
    private readonly ILogger<Patcher> _logger;

    public Patcher(ILogger<Patcher> logger)
    {
        _logger = logger;
    }

    public bool ApplyPatch(Process process, string module, int offset, byte[] bytes)
    {
        try
        {
            var moduleBase = GetModuleBaseAddress(process, module);
            if (moduleBase == 0)
            {
                _logger.LogError("Module {Module} not found in process {Pid}", module, process.Id);
                return false;
            }

            var targetAddress = moduleBase + offset;

            var handle = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, process.Id);
            if (handle == 0)
            {
                _logger.LogError("Failed to open process {Pid} for memory write", process.Id);
                return false;
            }

            try
            {
                // Change memory protection
                if (!VirtualProtectEx(handle, targetAddress, (uint)bytes.Length, PAGE_EXECUTE_READWRITE, out uint oldProtection))
                {
                    _logger.LogError("Failed to change memory protection at {Address:X}", targetAddress);
                    return false;
                }

                try
                {
                    // Write the patch bytes
                    if (!WriteProcessMemory(handle, targetAddress, bytes, (uint)bytes.Length, out _))
                    {
                        _logger.LogError("Failed to write patch at {Address:X}", targetAddress);
                        return false;
                    }

                    _logger.LogDebug("Applied patch to {Module}+{Offset:X} ({ByteCount} bytes)", module, offset, bytes.Length);
                    return true;
                }
                finally
                {
                    // Restore original protection
                    VirtualProtectEx(handle, targetAddress, (uint)bytes.Length, oldProtection, out _);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch to {Module}+{Offset:X}", module, offset);
            return false;
        }
    }

    private static nint GetModuleBaseAddress(Process process, string moduleName)
    {
        // Module enumeration can fail on suspended processes (ERROR_PARTIAL_COPY)
        // because the loader hasn't fully initialized. Retry a few times.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                process.Refresh();

                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return module.BaseAddress;
                    }
                }

                return 0;
            }
            catch (System.ComponentModel.Win32Exception) when (attempt < 9)
            {
                Thread.Sleep(200);
            }
        }

        return 0;
    }
}
