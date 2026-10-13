using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Aether.Gui;

[SupportedOSPlatform("macos")]
internal static class MacOSDockIcon
{
    public static void Apply()
    {
        Dispatcher.UIThread.VerifyAccess();
        using var resource = AssetLoader.Open(new Uri("avares://Aether.Gui/Assets/aether.icns"));
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var data = Send(Send(GetClass("NSData"), Selector("alloc")), Selector("initWithBytes:length:"), bytes, (nuint)bytes.Length);
        nint image = 0;
        try
        {
            image = Send(Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
            if (image == 0) throw new InvalidOperationException("macOS 无法解码应用图标。");
            var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
            // Avalonia's Window.Icon is a no-op on macOS; NSApplication retains this image.
            Send(app, Selector("setApplicationIconImage:"), image);
        }
        finally
        {
            Send(image, Selector("release"));
            Send(data, Selector("release"));
        }
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint Selector(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, byte[] bytes, nuint length);
}
