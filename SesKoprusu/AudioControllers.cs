using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SesKoprusu;

internal static class AudioControllers
{
    // Media-class controllers are visible even when RDP redirects MMDevice endpoints.
    public static List<string> Present()
    {
        var mediaClass = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318");
        var set = SetupDiGetClassDevsW(ref mediaClass, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var names = new List<string>();
            var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref device); i++)
            {
                foreach (uint property in new uint[] { 0, 12 }) // Device description and friendly name
                {
                    var buffer = new byte[2048];
                    if (SetupDiGetDeviceRegistryPropertyW(set, ref device, property, out _, buffer, (uint)buffer.Length, out uint size))
                        names.Add(Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0'));
                }
            }
            if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception();
            return names;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfo { public int Size; public Guid ClassGuid; public uint Instance; public UIntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint length, out uint required);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
