using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;

namespace D2BotNG.Windows;

public static class Extensions
{
    public static bool SendMessage(this Process proc, MessageType messageType, string data)
    {
        Log.Debug("Sending message: {messageType} {data}", messageType, data);
        if (proc.MainWindowHandle == 0)
            return false;

        // D2BS reads a null terminated string, add null byte at the end.
        var bytes = Encoding.ASCII.GetBytes(data + '\0');
        var pData = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, pData, bytes.Length);

            var copyData = new COPYDATASTRUCT
            {
                dwData = (nint)messageType,
                cbData = bytes.Length,
                lpData = pData
            };

            var pCopyData = Marshal.AllocHGlobal(Marshal.SizeOf<COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(copyData, pCopyData, false);

                var result = SendMessageTimeout(
                    proc.MainWindowHandle,
                    WM_COPYDATA,
                    0,
                    pCopyData,
                    SMTO_NOTIMEOUTIFNOTHUNG,
                    250,
                    out _);

                if (result == 0)
                {
                    Log.Warning("Failed to send WM_COPYDATA to {ProcMainWindowHandle}", proc.MainWindowHandle);
                    return false;
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(pCopyData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
        }
    }
}
