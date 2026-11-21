using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardExtensionClean
{
    internal sealed class TrayApp : ApplicationContext
    {
        private WorkerClient worker;
        private KeyboardPump keyboard;
        private bool refreshing;
        private long noticeUntil;
        private string notice;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer clock=new System.Windows.Forms.Timer {Interval=250};
        private readonly Control dispatcher=new Control();
        internal TrayApp()
        {
            var handle=dispatcher.Handle;
            worker=new WorkerClient();
            keyboard=new KeyboardPump(toggle=>worker!=null && worker.TryAction(toggle),
                (owner,pause)=>{var current=worker;if(current!=null)current.Reset(owner,pause);},
                ()=>dispatcher.BeginInvoke((Action)Refresh),
                ()=>{var current=worker;return current==null?null:current.TakeToggleCommit();},
                ()=>{var current=worker;if(current!=null)current.CancelDeleteGesture();});
            var menu=new ContextMenuStrip();
            menu.Items.Add("새로고침",null,delegate {Refresh();});
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료",null,delegate {ExitThread();});
            tray=new NotifyIcon {Icon=SystemIcons.Application,Text="ClipPlus — Insert 고정 전환 / Delete 비고정 삭제",
                ContextMenuStrip=menu,Visible=true};
            clock.Tick+=delegate {
                if(refreshing)return;
                if(!keyboard.Healthy){Wire.Log("keyboard thread stopped");Environment.Exit(3);}
                if(worker==null || !worker.Healthy){
                    Wire.Log(worker==null?"worker missing":worker.HealthError);

                    RestartWorker();
                }
                string status=ActionRules.Now<noticeUntil?notice:(worker==null?"다시 시작 중":worker.Status);
                tray.Text=("ClipPlus — "+status).Substring(0,Math.Min(63,("ClipPlus — "+status).Length));
            };
            clock.Start();Wire.Log("ClipPlus started");
        }
        private void Refresh()
        {
            if(refreshing)return;
            refreshing=true;
            try
            {
                RestartWorker();keyboard.RefreshHook();
                notice="새로고침 완료";noticeUntil=ActionRules.Now+8000;
                Wire.Log("ClipPlus refreshed: worker and keyboard hook; Windows host untouched");
                tray.ShowBalloonTip(3000,"ClipPlus","새로고침 완료",ToolTipIcon.Info);
            }
            finally{refreshing=false;}
        }
        private void RestartWorker()
        {
            var previous=worker;worker=null;
            if(previous!=null)previous.Dispose();
            worker=new WorkerClient();
            Wire.Log("clipboard worker restarted");
        }
        protected override void ExitThreadCore()
        {
            clock.Stop();clock.Dispose();keyboard.Dispose();
            if(worker!=null)worker.Dispose();
            tray.Visible=false;tray.Dispose();dispatcher.Dispose();base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        private static void WaitForExistingAgent()
        {
            Mutex existing;
            if(!Mutex.TryOpenExisting(@"Local\ClipPlus.Agent",out existing))return;
            using(existing)
            {
                bool acquired=false;
                try {existing.WaitOne();acquired=true;}
                catch(AbandonedMutexException){acquired=true;}
                finally {if(acquired)existing.ReleaseMutex();}
            }
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try {
                if(args.Length!=0 && (args[0]=="--worker" || args[0]=="--worker-mock" || args[0]=="--worker-mock-once")) {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException,false);
                    Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(),
                        new System.Text.UTF8Encoding(false)) {AutoFlush=true});
                    Console.SetIn(new System.IO.StreamReader(Console.OpenStandardInput(),
                        new System.Text.UTF8Encoding(false)));
                    WorkerLoop.Run(args[0]!="--worker",args[0]=="--worker-mock-once");return 0;
                }
                if(args.Length!=0 && args[0]=="--agent") {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException,false);
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    bool owns;
                    using(var mutex=new Mutex(true,@"Local\ClipPlus.Agent",out owns)) {
                        if(!owns)return 0;
                        Application.Run(new TrayApp());return 0;
                    }
                }
                bool supervisor;
                using(var mutex=new Mutex(true,@"Local\ClipPlus.Supervisor",out supervisor)) {
                    if(!supervisor)return 0;
                    while(true) {
                        WaitForExistingAgent();
                        using(var process=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--agent") {
                            UseShellExecute=false,CreateNoWindow=true})) {
                            process.WaitForExit();
                        }
                        Thread.Sleep(1000);
                    }
                }
            } catch(Exception error) {Wire.Log(error.ToString());return 1;}
        }
    }
}
