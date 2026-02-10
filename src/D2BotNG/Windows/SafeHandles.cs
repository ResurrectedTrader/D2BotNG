using Microsoft.Win32.SafeHandles;

namespace D2BotNG.Windows;

public class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeProcessHandle(nint existingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}

public class SafeLocalAllocHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeLocalAllocHandle(nint existingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.LocalFree(handle) == 0;
    }
}
