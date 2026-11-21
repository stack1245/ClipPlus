using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardExtensionClean
{
    internal static class Native
    {
        internal delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        internal delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyInfo { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct GuiThread
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public int Left, Top, Right, Bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point {public int X,Y;}
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GuiThread info);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr parameter);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int capacity);
        internal static string ClassName(IntPtr window)
        {
            var text=new StringBuilder(256);GetClassName(window,text,text.Capacity);return text.ToString();
        }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint="SetWindowsHookExW", SetLastError=true)]
        internal static extern IntPtr InstallHook(int type, KeyboardCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    }
}
