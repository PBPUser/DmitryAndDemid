using System.Runtime.InteropServices;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// The handful of libc calls the pad needs. Device nodes are opened through raw file descriptors rather than
/// <see cref="FileStream"/> because two of the three things we do to them — the force-feedback upload and the
/// effect removal — are ioctls, which .NET has no managed equivalent for.
///
/// Nothing here is ever called off Linux: every caller checks <see cref="OperatingSystem.IsLinux"/> first, so
/// the P/Invokes are never resolved on a platform that has no libc to resolve them against.
/// </summary>
internal static class LinuxIo
{
    private const string Libc = "libc";

    [DllImport(Libc, EntryPoint = "open", SetLastError = true)]
    internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport(Libc, EntryPoint = "close", SetLastError = true)]
    internal static extern int Close(int fd);

    [DllImport(Libc, EntryPoint = "write", SetLastError = true)]
    internal static extern nint Write(int fd, byte[] buffer, nint count);

    /// <summary>ioctl with a pointer argument (the force-feedback upload).</summary>
    [DllImport(Libc, EntryPoint = "ioctl", SetLastError = true)]
    internal static extern int Ioctl(int fd, nuint request, byte[] argument);

    /// <summary>ioctl with an immediate argument (removing an effect takes the id by value, not by pointer).</summary>
    [DllImport(Libc, EntryPoint = "ioctl", SetLastError = true)]
    internal static extern int Ioctl(int fd, nuint request, nint argument);

    internal const int OpenWriteOnly = 0x0001;
    internal const int OpenReadWrite = 0x0002;

    /// <summary>Keeps a write from parking the game thread if the pad stops draining its queue.</summary>
    internal const int OpenNonBlocking = 0x0800;

    /// <summary>Don't leak the descriptor into anything the game spawns.</summary>
    internal const int OpenCloseOnExec = 0x80000;

    /// <summary>
    /// Linux's ioctl request encoding: direction, payload size, a "type" letter namespacing the call, and the
    /// call's number within that type. Same layout on x86-64 and arm64, the two architectures the game ships on.
    /// </summary>
    internal static nuint IoWrite(char type, uint number, uint size) =>
        (nuint)((1u << 30) | (size << 16) | ((uint)type << 8) | number);

    internal static string ErrorText(int errno) => new System.ComponentModel.Win32Exception(errno).Message;
}
