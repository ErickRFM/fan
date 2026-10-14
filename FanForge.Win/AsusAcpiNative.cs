using System.Runtime.InteropServices;
using System.ComponentModel;
using System.IO;

namespace FanForge;

internal static class AsusAcpiNative
{
    const string DeviceName = @"\\.\\ATKACPI";
    const uint CONTROL_CODE = 0x0022240C;

    const uint DSTS = 0x53545344;
    const uint DEVS = 0x53564544;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
        ref uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint OPEN_EXISTING = 3;
    const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    const uint FILE_SHARE_READ = 1;
    const uint FILE_SHARE_WRITE = 2;

    public static bool TryOpen(out IntPtr handle)
    {
        handle = CreateFile(DeviceName, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        return handle != (IntPtr)(-1) && handle != IntPtr.Zero;
    }

    public static int DeviceGet(IntPtr handle, uint deviceId)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        byte[] status = Call(handle, DSTS, args);
        return BitConverter.ToInt32(status, 0) - 65536;
    }

    public static int DeviceSet(IntPtr handle, uint deviceId, int value)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        BitConverter.GetBytes((uint)value).CopyTo(args, 4);
        byte[] status = Call(handle, DEVS, args);
        return BitConverter.ToInt32(status, 0);
    }

    static byte[] Call(IntPtr handle, uint method, byte[] args)
    {
        byte[] acpiBuf = new byte[8 + args.Length];
        byte[] outBuffer = new byte[16];
        BitConverter.GetBytes(method).CopyTo(acpiBuf, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(acpiBuf, 4);
        Array.Copy(args, 0, acpiBuf, 8, args.Length);
        uint bytesReturned = 0;
        if (!DeviceIoControl(handle, CONTROL_CODE, acpiBuf, (uint)acpiBuf.Length, outBuffer, (uint)outBuffer.Length,
            ref bytesReturned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ATKACPI DeviceIoControl failed");
        if (bytesReturned < sizeof(int))
            throw new IOException("ATKACPI returned an incomplete response.");
        return outBuffer;
    }

    public static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    public const uint CPU_Fan = 0x00110013;
    public const uint GPU_Fan = 0x00110014;
    public const uint VivoBookMode = 0x00110019;
    public const uint PerformanceMode = 0x00120075;
    public const uint Temp_CPU = 0x00120094;
    public const uint Temp_GPU = 0x00120097;
    public const uint DevsCPUFanCurve = 0x00110024;
    public const uint DevsGPUFanCurve = 0x00110025;
}
