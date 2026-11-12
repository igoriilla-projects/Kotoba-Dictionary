using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Kotoba.Windows;

/// Native notification-area icon, avoiding a WinForms dependency in the WPF app.
internal sealed class TrayIcon : IDisposable
{
    private const int Callback=0x8001;
    private readonly HwndSource source;
    private readonly Action open;
    private readonly Func<ContextMenu> menu;
    private readonly uint taskbarCreated;
    private NotifyIconData data;
    public bool IsVisible {get;private set;}
    public TrayIcon(Action open,Func<ContextMenu> menu)
    {
        this.open=open;this.menu=menu;
        source=new HwndSource(new HwndSourceParameters("Kotoba notification icon"){WindowStyle=unchecked((int)0x80000000),Width=0,Height=0});source.AddHook(Messages);
        taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        data=new NotifyIconData{Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=source.Handle,Id=1,Flags=7,CallbackMessage=Callback,Icon=LoadIcon(IntPtr.Zero,new IntPtr(32512)),Tip="Kotoba · Открыть / Виджет",Info="",InfoTitle=""};
        IsVisible=Shell_NotifyIcon(0,ref data);
    }
    private IntPtr Messages(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if((uint)message==taskbarCreated)IsVisible=Shell_NotifyIcon(0,ref data);
        if(message==Callback)
        {
            int mouse=unchecked((int)lParam.ToInt64());
            if(mouse==0x203)open();
            if(mouse==0x205)
            {SetForegroundWindow(source.Handle);var context=menu();context.Placement=PlacementMode.MousePoint;context.Closed+=(_,_)=>PostMessage(source.Handle,0,IntPtr.Zero,IntPtr.Zero);context.IsOpen=true;}
            handled=true;
        }
        return IntPtr.Zero;
    }
    public void Dispose(){if(IsVisible)Shell_NotifyIcon(2,ref data);IsVisible=false;source.Dispose();}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags;public int CallbackMessage;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint message,ref NotifyIconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr LoadIcon(IntPtr instance,IntPtr name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
}
