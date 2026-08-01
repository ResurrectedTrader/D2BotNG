using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace D2BotNG.Services.Analytics;

/// <summary>
/// Stable anonymous install id, <i>derived</i> rather than stored — analytics writes nothing
/// to disk or the registry. Three independent machine facts are combined so that a missing or
/// constant one (some Wine prefixes report no MachineGuid) still leaves the id distinct, then
/// salted and hashed: only the digest ever leaves the machine, and the salt keeps it from
/// matching the same machine's identifier in any other software.
///
/// This must stay byte-identical to d2bsng's DeriveInstallId (components/analytics/Analytics.cpp)
/// so the manager and the injected DLL report the same install and can be joined in the
/// dashboard: same salt, same three facts in the same order, same '|' separator, UTF-8 bytes,
/// SHA-256 rendered as lowercase hex.
/// </summary>
internal static class InstallId
{
    /// <summary>
    /// Fixed, and scoped to the publisher rather than to one app — that is what lets d2bsng and
    /// the manager derive the same id. Bumping it re-buckets every install as new.
    /// </summary>
    private const string Salt = "ResurrectedTrader-analytics-v1";

    public static string Derive() => Compute(MachineGuid(), SystemVolumeSerial(), ComputerName());

    /// <summary>
    /// The derivation itself, separated from gathering the machine facts so it can be pinned
    /// by a test. Every detail here is load-bearing for matching d2bsng — the salt, the field
    /// order, the '|' separator, UTF-8 bytes, and lowercase hex.
    /// </summary>
    internal static string Compute(string machineGuid, string volumeSerial, string computerName)
    {
        var material = $"{Salt}|{machineGuid}|{volumeSerial}|{computerName}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid, or empty when absent/unreadable.
    /// Pinned to the 64-bit view to match d2bsng, which has to ask for it explicitly (that DLL
    /// is 32-bit, so WOW64 would otherwise redirect the read to WOW6432Node).
    /// </summary>
    private static string MachineGuid()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            // `as string` yields "" for the non-string value d2bsng rejects by type. It is
            // laxer for REG_EXPAND_SZ, which it accepts (expanded) -- unreachable, since
            // MachineGuid is always REG_SZ.
            return key?.GetValue("MachineGuid") as string ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Serial of the volume Windows is installed on, as a decimal string (0 when unavailable).
    /// Survives reboots and app reinstalls; changes on a reformat.
    /// </summary>
    private static string SystemVolumeSerial()
    {
        var windowsDirectory = new StringBuilder(260);
        if (GetSystemWindowsDirectoryW(windowsDirectory, (uint)windowsDirectory.Capacity) == 0)
        {
            return "0";
        }

        // Just the drive root ("C:\"), as d2bsng takes it.
        var root = windowsDirectory.ToString();
        if (root.Length < 3)
        {
            return "0";
        }

        return GetVolumeInformationW(root[..3], null, 0, out var serial, out _, out _, null, 0)
            ? serial.ToString()
            : "0";
    }

    private static string ComputerName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetSystemWindowsDirectoryW(StringBuilder buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        uint fileSystemNameSize);
}
