using System.Diagnostics;
using System.Text;
using D2BotNG.Core.Protos;
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

    public async Task<bool> ApplyPatchAsync(Process process, string module, Patch patch)
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
            var moduleBase = await LoadModuleRemotelyAsync(hProcess, module);

            if (moduleBase == 0)
            {
                _logger.LogError("Module {Module} not found in process {Pid}", module, process.Id);
                return false;
            }

            var targetAddress = moduleBase + patch.Offset;

            // Change memory protection
            if (!VirtualProtectEx(hProcess, targetAddress, (nuint)patch.Data.Length, PAGE_EXECUTE_READWRITE, out uint oldProtection))
            {
                _logger.LogError("Failed to change memory protection at {Address:X}", targetAddress);
                return false;
            }

            try
            {
                // Write the patch bytes
                if (!WriteProcessMemory(hProcess, targetAddress, patch.Data.ToByteArray(), (nuint)patch.Data.Length, out _))
                {
                    _logger.LogError("Failed to write patch at {Address:X}", targetAddress);
                    return false;
                }

                _logger.LogDebug("Applied patch {Name} to {Module}+{Offset:X} ({ByteCount} bytes)", patch.Name, module, patch.Offset, patch.Data.Length);
                return true;
            }
            finally
            {
                VirtualProtectEx(hProcess, targetAddress, (nuint)patch.Data.Length, oldProtection, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch to {Module}+{Offset:X}", module, patch.Offset);
            return false;
        }
    }

    /// <summary>
    /// Force-loads a DLL into the target process by calling LoadLibraryW via CreateRemoteThread.
    /// Works on suspended processes because CreateRemoteThread creates a new, non-suspended thread.
    /// Returns the module base address (LoadLibrary's return value) or 0 on failure.
    /// </summary>
    private async Task<nint> LoadModuleRemotelyAsync(nint processHandle, string modulePath)
    {
        var pathBytes = Encoding.Unicode.GetBytes(modulePath + '\0');

        // Allocate memory in target process for the DLL path string
        var remoteMemory = VirtualAllocEx(processHandle, 0, (nuint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remoteMemory == 0)
        {
            _logger.LogError("Failed to allocate memory for module path in target process");
            return 0;
        }

        try
        {
            // Write the DLL path into the allocated memory
            if (!WriteProcessMemory(processHandle, remoteMemory, pathBytes, (nuint)pathBytes.Length, out _))
            {
                _logger.LogError("Failed to write module path to target process");
                return 0;
            }

            // Resolve LoadLibraryW for the target. Same-bitness targets use our own kernel32 (shared
            // base); a 32-bit target gets the 32-bit kernel32 address from a peer WOW64 process.
            var loadLibraryAddr = RemoteModule.ResolveExportForTarget(processHandle, "kernel32.dll", "LoadLibraryW");
            if (loadLibraryAddr == 0)
            {
                _logger.LogError("Failed to resolve LoadLibraryW for target process");
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

            // Poll instead of blocking so the thread is released between checks
            if (!await WaitForSingleObjectAsync(threadHandle.DangerousGetHandle(), TimeSpan.FromSeconds(10)))
            {
                _logger.LogError("Timed out waiting for LoadLibraryW in target process for {ModulePath}", modulePath);
                return 0;
            }

            // The remote thread's exit code can't carry the module base on x64: it's a 32-bit
            // DWORD, while a 64-bit HMODULE needs the full pointer width. Read the base from the
            // loader's module list instead — the LoadLibraryW above both forces the load and (as
            // the first thread to run in the suspended process) initializes the loader data, so
            // the module is now enumerable.
            var moduleBase = RemoteModule.GetModuleBase(processHandle, Path.GetFileName(modulePath));
            if (moduleBase == 0)
            {
                _logger.LogError("Module {ModulePath} not loaded in target process after LoadLibraryW", modulePath);
                return 0;
            }

            return moduleBase;
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteMemory, 0, MEM_RELEASE);
        }
    }
}
