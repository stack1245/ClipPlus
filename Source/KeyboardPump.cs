using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardExtensionClean
{
    internal sealed class KeyboardPump : IDisposable
    {
        private readonly Func<bool,bool> action;
        private readonly Action<long,int> reset;
        private readonly Action refresh;
        private readonly Func<Action> releaseToggle;
        private readonly Action cancelDelete;
        private readonly RefreshGesture refreshGesture=new RefreshGesture();
        private readonly Thread thread;
        private readonly Native.KeyboardCallback callback;
        private Control window;
        private IntPtr hook;
        private readonly ReleaseTrigger insert=new ReleaseTrigger(),delete=new ReleaseTrigger();
        private Exception startError;
        internal bool Healthy {get{return thread.IsAlive && startError==null;}}
        internal KeyboardPump(Func<bool,bool> handler,Action<long,int> invalidate,Action refreshHandler,Func<Action> commitToggle,Action cancelDeleteHandler)
        {
            action=handler;reset=invalidate;refresh=refreshHandler;releaseToggle=commitToggle;cancelDelete=cancelDeleteHandler;callback=Key;
            using(var ready=new ManualResetEvent(false)) {
                bool announced=false;
                thread=new Thread(delegate() {
                    try {
                        using(window=new Control())
                        using(var timer=new System.Windows.Forms.Timer {Interval=10000}) {
                            var handle=window.Handle;
                            Renew();timer.Tick+=delegate {Renew();};timer.Start();
                            announced=true;ready.Set();Application.Run();
                        }
                    } catch(Exception error) {startError=error;if(!announced)ready.Set();}
                    finally {if(hook!=IntPtr.Zero)Native.UnhookWindowsHookEx(hook);}
                }) {IsBackground=true,Name="ClipPlus keyboard"};
                thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne();
                if(startError!=null)throw new InvalidOperationException("입력 훅을 시작하지 못했습니다.",startError);
            }
        }
        private void Renew()
        {
            IntPtr next=Native.InstallHook(13,callback,Native.GetModuleHandle(null),0);
            if(next==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr old=hook;hook=next;if(old!=IntPtr.Zero)Native.UnhookWindowsHookEx(old);
        }
        private IntPtr Key(int code,IntPtr message,IntPtr data)
        {
            if(code<0)return Native.CallNextHookEx(hook,code,message,data);
            try {
                var key=(Native.KeyInfo)Marshal.PtrToStructure(data,typeof(Native.KeyInfo));
                long kind=message.ToInt64();bool down=kind==0x100 || kind==0x104;
                bool up=kind==0x101 || kind==0x105;
                if(down && key.Key!=46)cancelDelete();
                bool win=(Native.GetAsyncKeyState(91)&0x8000)!=0 || (Native.GetAsyncKeyState(92)&0x8000)!=0;
                bool repeatedRefresh=refreshGesture.Observe((int)key.Key,down,up,win,ActionRules.Now);
                if(down && key.Key==86 && win)
                    reset(Native.GetForegroundWindow().ToInt64(),60);
                if(repeatedRefresh)
                {
                    refresh();
                }
                if(down && (key.Key==27 || key.Key==13 || key.Key==18))
                    reset(0,200);
                if(key.Key!=45 && key.Key!=46)return Native.CallNextHookEx(hook,code,message,data);
                bool toggle=key.Key==45;var trigger=toggle?insert:delete;
                if(up && trigger.Release(()=> {
                    if(toggle)try{var commit=releaseToggle();if(commit!=null)window.BeginInvoke(commit);}catch{}
                }))return (IntPtr)1;
                if(!down)return Native.CallNextHookEx(hook,code,message,data);
                if(trigger.Press(()=> {
                    foreach(int modifier in new[]{16,17,18,91,92})
                        if((Native.GetAsyncKeyState(modifier)&0x8000)!=0)return false;
                    return action(toggle);
                }))return (IntPtr)1;
            } catch { }
            return Native.CallNextHookEx(hook,code,message,data);
        }
        internal void RefreshHook()
        {
            if(window!=null && !window.IsDisposed)window.BeginInvoke((Action)Renew);
        }
        public void Dispose()
        {
            try {if(window!=null && !window.IsDisposed)window.BeginInvoke((Action)Application.ExitThread);}catch{}
            if(thread!=Thread.CurrentThread)thread.Join(1000);
        }
    }
}
