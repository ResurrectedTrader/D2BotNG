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
            var rawHandle = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
                false, process.Id);
            if (rawHandle == 0)
            {
                _logger.LogError("Failed to open process {Pid} for patching", process.Id);
                return false;
            }

            using var handle = new SafeProcessHandle(rawHandle, ownsHandle: true);
            var hProcess = handle.DangerousGetHandle();

            // Get module base address via CreateRemoteThread + LoadLibraryW
            // Works for both .exe and .dll — LoadLibraryW on an already-mapped exe returns its base address
            // This works on suspended processes because the remote thread is not suspended
            var moduleBase = LoadModuleRemotely(hProcess, module);

            if (moduleBase == 0)
            {
                _logger.LogError("Module {Module} not found in process {Pid}", module, process.Id);
                return false;
            }

            var targetAddress = moduleBase + offset;

            // Change memory protection
            if (!VirtualProtectEx(hProcess, targetAddress, (uint)bytes.Length, PAGE_EXECUTE_READWRITE, out uint oldProtection))
            {
                _logger.LogError("Failed to change memory protection at {Address:X}", targetAddress);
                return false;
            }

            try
            {
                // Write the patch bytes
                if (!WriteProcessMemory(hProcess, targetAddress, bytes, (uint)bytes.Length, out _))
                {
                    _logger.LogError("Failed to write patch at {Address:X}", targetAddress);
                    return false;
                }

                _logger.LogDebug("Applied patch to {Module}+{Offset:X} ({ByteCount} bytes)", module, offset, bytes.Length);
                return true;
            }
            finally
            {
                VirtualProtectEx(hProcess, targetAddress, (uint)bytes.Length, oldProtection, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch to {Module}+{Offset:X}", module, offset);
            return false;
        }
    }

    /// <summary>
    /// Force-loads a DLL into the target process by calling LoadLibraryW via CreateRemoteThread.
    /// Works on suspended processes because CreateRemoteThread creates a new, non-suspended thread.
    /// Returns the module base address (LoadLibrary's return value) or 0 on failure.
    /// </summary>
    private nint LoadModuleRemotely(nint processHandle, string modulePath)
    {
        var pathBytes = System.Text.Encoding.Unicode.GetBytes(modulePath + '\0');

        // Allocate memory in target process for the DLL path string
        var remoteMemory = VirtualAllocEx(processHandle, 0, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remoteMemory == 0)
        {
            _logger.LogError("Failed to allocate memory for module path in target process");
            return 0;
        }

        try
        {
            // Write the DLL path into the allocated memory
            if (!WriteProcessMemory(processHandle, remoteMemory, pathBytes, (uint)pathBytes.Length, out _))
            {
                _logger.LogError("Failed to write module path to target process");
                return 0;
            }

            // Get LoadLibraryW address from our own kernel32 (same address in target due to ASLR shared base)
            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryAddr == 0)
            {
                _logger.LogError("Failed to get LoadLibraryW address");
                return 0;
            }

            // Create remote thread that calls LoadLibraryW(modulePath)
            var rawThreadHandle = CreateRemoteThread(processHandle, 0, 0, loadLibraryAddr, remoteMemory, 0, out _);
            if (rawThreadHandle == 0)
            {
                _logger.LogError("Failed to create remote thread for LoadLibraryW");
                return 0;
            }

            using var threadHandle = new SafeProcessHandle(rawThreadHandle, ownsHandle: true);

            // Wait for LoadLibraryW to complete
            WaitForSingleObject(threadHandle.DangerousGetHandle(), INFINITE);

            // LoadLibrary's return value is the module base address (or 0 on failure)
            // Retrieved via the thread's exit code
            if (!GetExitCodeThread(threadHandle.DangerousGetHandle(), out var moduleBase))
            {
                _logger.LogError("Failed to get exit code from LoadLibraryW thread");
                return 0;
            }

            if (moduleBase == 0)
            {
                _logger.LogError("LoadLibraryW failed in target process for {ModulePath}", modulePath);
                return 0;
            }

            return (nint)moduleBase;
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteMemory, 0, MEM_RELEASE);
        }
    }
}
