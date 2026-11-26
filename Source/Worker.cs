using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardExtensionClean
{
    // UI Automation runs on a windowless MTA, never a Forms timer callback.
    // A single consumer prevents COM message pumping from reentering a change.
    internal sealed class WorkerLoop
    {
        private readonly BlockingCollection<Packet> inbox=new BlockingCollection<Packet>();
        private readonly ContextFence fence=new ContextFence();
        private readonly ISurface surface;
        private long currentEpoch=1;
        private readonly bool retireAfterToggle;
        private WorkerLoop(bool mock,bool retire)
        {
            retireAfterToggle=retire;
            surface=mock?(ISurface)new MockSurface(fence):new ClipboardSurface(fence,Send);
        }
        internal static void Run(bool mock,bool retire=false)
        {
            Exception fault=null;
            var thread=new Thread(()=> {
                try {new WorkerLoop(mock,retire).RunCore();}
                catch(Exception error){fault=error;}
            }) {Name="ClipPlus windowless automation"};
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();thread.Join();
            if(fault!=null)throw new InvalidOperationException("작업 프로세스가 중단됐습니다.",fault);
        }
        private void Send(Packet packet)
        {
            Console.WriteLine(Wire.Encode(packet));Console.Out.Flush();
        }
        private void RunCore()
        {
            Wire.Log("worker automation apartment: "+Thread.CurrentThread.GetApartmentState());
            new Thread(()=> {
                try {
                    string line;
                    while((line=Console.ReadLine())!=null) {
                        if(line.Length>8192)continue;
                        var packet=Wire.Decode(line);
                        if(packet.Kind=="reset")fence.Advance(packet.Epoch);
                        inbox.Add(packet);
                    }
                } catch { }
                fence.Advance(long.MaxValue);Environment.Exit(0);
            }) {IsBackground=true,Name="ClipPlus worker input"}.Start();
            for(;;)
            {
                Packet command;
                if(inbox.TryTake(out command,75))
                {
                    if(command.Kind=="quit")return;
                    if(command.Kind=="reset")
                    {
                        if(command.Epoch<currentEpoch)continue;
                        currentEpoch=command.Epoch;
                        surface.Reset(command.Owner,command.Epoch,(int)command.Stamp);
                    }
                    else if(command.Kind=="hang-test" && surface is MockSurface)Thread.Sleep(Timeout.Infinite);
                    else if(command.Kind=="change")
                    {
                        Wire.Log("change begin "+command.Request+" operation="+command.Operation+" epoch="+command.Epoch);
                        string result;
                        try {result=surface.Change(command).GetAwaiter().GetResult();}
                        catch(Exception error){result=error.Message;}
                        Wire.Log("change end "+command.Request+" result="+result);
                        if(!(surface is MockSurface))Wire.LogAsync("change native window request="+command.Request
                            +" ownerBefore="+command.Owner+" foregroundAfter="+Native.GetForegroundWindow().ToInt64()
                            +" rootVisible="+Native.IsWindowVisible(new System.IntPtr(command.Root)));
                        Send(new Packet {Kind="result",Request=command.Request,Error=result,Epoch=currentEpoch});
                        if(retireAfterToggle && command.Operation=="toggle")
                        {
                            Wire.Log("pin worker retired without further UIA queries request="+command.Request);
                            return;
                        }
                    }
                }
                long probeStarted=ActionRules.Now;
                try {Send(surface.Probe());}
                catch(Exception error){Send(new Packet {Kind="state",Stamp=ActionRules.Now,Epoch=currentEpoch,Pin="Unknown",Error=error.GetType().Name});}
                finally {if(ActionRules.Now-probeStarted>300)Wire.LogAsync("slow UIA probe ms="+(ActionRules.Now-probeStarted));}
            }
        }
    }
    internal sealed class WorkerClient : IDisposable
    {
        private Process child;
        private readonly BlockingCollection<Packet> outbound=new BlockingCollection<Packet>(32);
        private Packet latest=new Packet();
        private Packet inputScope=new Packet();
        private Packet armedToggle;
        private readonly DeleteGesture deleteGesture;
        private readonly object requestGate=new object();
        private readonly DeferredClear deferredClear=new DeferredClear();
        private bool disposed;
        private sealed class PendingRequest
        {
            internal readonly string Id=Guid.NewGuid().ToString("N");
            internal readonly long Started=ActionRules.Now;
            internal int PinProviderStarted;
            internal string Operation;
        }
        private long epoch=1,lastPacket,startedAt;
        private PendingRequest pending;
        private int transportFault;
        private int probeErrors;
        internal string Status="준비 중";
        internal Packet Latest {get{return Volatile.Read(ref latest);}}
        internal int ProcessId {get{return child==null?0:child.Id;}}
        internal bool IsBusy {get{return Volatile.Read(ref pending)!=null;}}
        internal WorkerClient(bool mock=false,string executable=null)
        {
            deleteGesture=new DeleteGesture(CommitDelete);
            child=Process.Start(new ProcessStartInfo(executable??Application.ExecutablePath,mock?"--worker-mock":"--worker") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
                StandardOutputEncoding=System.Text.Encoding.UTF8
            });
            startedAt=ActionRules.Now;
            var inputThread=new Thread(Read) {IsBackground=true,Name="ClipPlus state reader"};
            inputThread.Start();
            var outputThread=new Thread(delegate() {
                try {
                    foreach(var packet in outbound.GetConsumingEnumerable()) {
                        child.StandardInput.WriteLine(Wire.Encode(packet));
                        child.StandardInput.Flush();
                    }
                } catch(Exception error) {Interlocked.Exchange(ref transportFault,1);Wire.Log("worker send: "+error.GetType().Name);}
            }) {IsBackground=true,Name="ClipPlus command writer"};
            outputThread.Start();
            Write(new Packet {Kind="reset",Epoch=epoch,Owner=0});
        }
        private void Read()
        {
            try {
                string line;
                while((line=child.StandardOutput.ReadLine())!=null) {
                    if(line.Length>8192)continue;
                    var packet=Wire.Decode(line);
                    Interlocked.Exchange(ref lastPacket,ActionRules.Now);
                    if(packet.Kind=="state" && packet.Epoch==Interlocked.Read(ref epoch))
                    {
                        Volatile.Write(ref latest,packet);
                        var bound=Volatile.Read(ref inputScope);
                        if(packet.Visible && packet.Focused && packet.Owner!=0)Volatile.Write(ref inputScope,packet);
                        else if(!packet.Visible || packet.Root!=bound.Root)
                        {
                            if(bound.Root!=0)Wire.LogAsync("popup scope ended visible="+packet.Visible+" focused="+packet.Focused+" epoch="+packet.Epoch);
                            Volatile.Write(ref inputScope,new Packet());
                        }
                        if(!string.IsNullOrEmpty(packet.Error))
                        {
                            if(++probeErrors>=3)Interlocked.Exchange(ref transportFault,1);
                        }
                        else probeErrors=0;
                        if(!IsBusy)Status=packet.Focused?"선택 항목 준비됨":"Win+V 대기";
                    }
                    var outstanding=Volatile.Read(ref pending);
                    if(packet.Kind=="progress" && outstanding!=null && packet.Request==outstanding.Id
                        && packet.Operation=="pin-provider-start")
                        Volatile.Write(ref outstanding.PinProviderStarted,1);
                    if(packet.Kind=="result" && outstanding!=null && packet.Request==outstanding.Id) {
                      lock(requestGate) {
                        if(!ReferenceEquals(Volatile.Read(ref pending),outstanding))continue;
                        Status=packet.Error ?? "";
                        Interlocked.CompareExchange(ref pending,null,outstanding);
                        var next=deferredClear.Take(outstanding.Id,Interlocked.Read(ref epoch),ActionRules.Now);
                        if(next!=null && ContextMatches(next))QueueOperation(next,"clear");
                      }
                      if(Status.Length!=0)Wire.LogAsync("action: "+Status);
                    }
                }
            } catch(Exception error) {Interlocked.Exchange(ref transportFault,1);Wire.Log("worker receive: "+error.GetType().Name);}
        }
        internal void Write(Packet packet)
        {
            try {if(!outbound.TryAdd(packet))Interlocked.Exchange(ref transportFault,1);}
            catch(InvalidOperationException) {Interlocked.Exchange(ref transportFault,1);}
        }
        internal void Reset(long owner,int pause)
        {
            if(Volatile.Read(ref inputScope).Root!=0)Wire.LogAsync("input scope reset pause="+pause+" newOwner="+owner);
            deleteGesture.Cancel();
          lock(requestGate) {
            deferredClear.Cancel();
            long next=Interlocked.Increment(ref epoch);
            Volatile.Write(ref latest,new Packet());
            Volatile.Write(ref inputScope,new Packet());
            Interlocked.Exchange(ref armedToggle,null);
            // Keep the in-flight watchdog armed even when its context expires.
            Write(new Packet {Kind="reset",Epoch=next,Owner=owner,Stamp=pause});
          }
        }
        internal bool TryAction(bool toggle)
        {
            if(!toggle && deleteGesture.TryComplete(ContextMatches))return true;
            var state=Latest;
            if(state.Epoch!=Interlocked.Read(ref epoch) || !state.Visible || !state.Focused || !ActionRules.Fresh(state.Stamp,ActionRules.Now,450)
                || Native.GetForegroundWindow().ToInt64()!=state.Owner
                || !Native.IsWindowVisible(new IntPtr(state.Root)))return false;
            if(toggle)
            {
                deleteGesture.Cancel();
                if(IsBusy)return true;
                Interlocked.Exchange(ref armedToggle,new Packet {Id=state.Id,Root=state.Root,
                    Owner=state.Owner,Epoch=state.Epoch,Stamp=ActionRules.Now});
                Wire.LogAsync("Insert armed; awaiting physical key-up");
                return true;
            }
            Interlocked.Exchange(ref armedToggle,null);
            deleteGesture.Press(state);
            return true;
        }
        internal bool HandleKey(bool toggle)
        {
            try
            {
                if(TryAction(toggle)){Wire.LogAsync("key action accepted toggle="+toggle+" busy="+IsBusy);return true;}
            }
            catch(Exception error){Wire.LogAsync("key action error: "+error.GetType().Name);}
            var current=Latest;var bound=Volatile.Read(ref inputScope);
            bool owns=ClipboardInputScope.Owns(current,bound,Interlocked.Read(ref epoch),ActionRules.Now,IsBusy,
                Native.GetForegroundWindow().ToInt64(),bound.Root!=0 && Native.IsWindowVisible(new System.IntPtr(bound.Root)));
            Wire.LogAsync("key action withheld toggle="+toggle+" consumed="+owns+" age="+(ActionRules.Now-current.Stamp)+" busy="+IsBusy+" focused="+current.Focused);
            return owns;
        }
        internal void CancelDeleteGesture()
        {
            deleteGesture.Cancel();
            lock(requestGate)deferredClear.Cancel();
            var active=Volatile.Read(ref pending);
            if(active!=null && active.Operation=="clear")Reset(Latest.Owner,0);
        }
        private void CommitDelete(Packet saved,string operation)
        {
            if(operation=="clear")
            {
                if(ContextMatches(saved))QueueOperation(saved,operation);
                return;
            }
            var current=Latest;
            if(saved.Epoch!=Interlocked.Read(ref epoch) || !ActionRules.Fresh(saved.Stamp,ActionRules.Now,1200)
                || !current.Focused || current.Epoch!=saved.Epoch || current.Root!=saved.Root || current.Owner!=saved.Owner
                || !string.Equals(current.Id,saved.Id,StringComparison.OrdinalIgnoreCase)
                || !ActionRules.Fresh(current.Stamp,ActionRules.Now,450)
                || Native.GetForegroundWindow().ToInt64()!=saved.Owner || !Native.IsWindowVisible(new IntPtr(saved.Root)))return;
            QueueOperation(saved,operation);
        }
        private bool ContextMatches(Packet saved)
        {
            return saved.Epoch==Interlocked.Read(ref epoch) && ActionRules.Fresh(saved.Stamp,ActionRules.Now,1200)
                && Native.GetForegroundWindow().ToInt64()==saved.Owner && Native.IsWindowVisible(new IntPtr(saved.Root));
        }
        internal Action TakeToggleCommit()
        {
            var armed=Interlocked.Exchange(ref armedToggle,null);
            return armed==null?null:(Action)(()=>CommitToggle(armed));
        }
        private void CommitToggle(Packet armed)
        {
            var state=Latest;
            if(armed.Epoch!=Interlocked.Read(ref epoch) || !ActionRules.Fresh(armed.Stamp,ActionRules.Now,1200)
                || !state.Focused || state.Root!=armed.Root || state.Owner!=armed.Owner
                || !string.Equals(state.Id,armed.Id,StringComparison.OrdinalIgnoreCase)
                || !ActionRules.Fresh(state.Stamp,ActionRules.Now,450)
                || Native.GetForegroundWindow().ToInt64()!=armed.Owner
                || !Native.IsWindowVisible(new IntPtr(armed.Root)))return;
            foreach(int modifier in new[]{16,17,18,91,92})
                if((Native.GetAsyncKeyState(modifier)&0x8000)!=0)return;
            Wire.LogAsync("Insert released; committing saved selection");
            QueueAction(armed,true);
        }
        private bool QueueAction(Packet state,bool toggle)
        {
            return QueueOperation(state,toggle?"toggle":"delete");
        }
        private bool QueueOperation(Packet state,string operation)
        {
          lock(requestGate) {
            if(disposed)return false;
            var active=Volatile.Read(ref pending);
            if(active!=null)
            {
                if(operation=="clear" && active.Operation=="delete")deferredClear.Store(state,active.Id);
                return true;
            }
            var request=new PendingRequest {Operation=operation};
            if(Interlocked.CompareExchange(ref pending,request,null)!=null)return true;
            Write(new Packet {Kind="change",Operation=operation,Id=state.Id,Root=state.Root,
                Owner=state.Owner,Stamp=state.Stamp,Epoch=state.Epoch,Request=request.Id});
            return true;
          }
        }
        internal string HealthError
        {
            get {
                try {
                    if(child.HasExited)return "worker exited: "+child.ExitCode;
                    if(Volatile.Read(ref transportFault)!=0)return "worker transport or repeated probe error";
                    long last=Interlocked.Read(ref lastPacket);
                    if(last==0)return ActionRules.Now-startedAt<5000 ? null : "worker startup timeout";
                    var request=Volatile.Read(ref pending);
                    if(ActionRules.Now-last>=6000)return "worker heartbeat timeout";
                    if(request!=null && ActionRules.Now-request.Started>=8000)return "worker action timeout";
                    return null;
                } catch(Exception error) {return error.GetType().Name;}
            }
        }
        internal bool Healthy {get{return HealthError==null;}}
        internal bool NeedsHostRecovery
        {
            get
            {
                var outstanding=Volatile.Read(ref pending);
                string failure=HealthError;
                return outstanding!=null && Volatile.Read(ref outstanding.PinProviderStarted)!=0
                    && (failure=="worker heartbeat timeout" || failure=="worker action timeout");
            }
        }
        public void Dispose()
        {
            deleteGesture.Dispose();
            Volatile.Write(ref inputScope,new Packet());
            lock(requestGate){disposed=true;deferredClear.Cancel();}
            Interlocked.Exchange(ref armedToggle,null);
            outbound.CompleteAdding();
            try {if(child!=null && !child.HasExited){child.Kill();child.WaitForExit(2000);}}catch{}
            try {child.Dispose();}catch{}
        }
    }
}
