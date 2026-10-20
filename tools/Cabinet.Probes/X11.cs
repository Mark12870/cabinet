using System.Runtime.InteropServices;

namespace Cabinet.Probes;

internal static unsafe partial class X11
{
    private const string Library = "libX11.so.6";

    [LibraryImport(Library)]
    public static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport(Library)]
    public static partial nuint XDefaultRootWindow(IntPtr display);

    [LibraryImport(Library)]
    public static partial int XWarpPointer(
        IntPtr display, nuint source, nuint destination, int sourceX, int sourceY,
        uint sourceWidth, uint sourceHeight, int destinationX, int destinationY);

    [LibraryImport(Library)]
    public static partial int XSync(IntPtr display, int discard);

    [LibraryImport(Library)]
    public static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libXtst.so.6")]
    public static partial int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);

    [LibraryImport(Library)]
    public static partial int XQueryTree(
        IntPtr display, nuint window, out nuint root, out nuint parent, out nuint* children, out uint count);

    [LibraryImport(Library)]
    public static partial int XFetchName(IntPtr display, nuint window, out IntPtr name);
}
