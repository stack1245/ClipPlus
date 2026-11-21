using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using ClipboardExtensionClean;

internal static class DoubleDeleteTests
{
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);checks++;Console.WriteLine("PASS: "+message);}
    static Packet State(string id="A",long epoch=1,long root=42,long owner=1){return new Packet{Id=id,Epoch=epoch,Root=root,Owner=owner,Stamp=ActionRules.Now};}
    static void GestureTests()
    {
        Check(DeleteGesture.Interval==ActionRules.DoublePressMilliseconds,"Delete and refresh share interval");
        var events=new BlockingCollection<string>();string value;
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op+":"+p.Id))) {
            gesture.Press(State());
            Check(events.TryTake(out value,0)&&value=="delete:A","first press deletes synchronously");
            Check(gesture.TryComplete(p=>true),"second press completes double gesture");
            Check(events.TryTake(out value,0)&&value=="clear:A","second press clears once");
            Check(!gesture.TryComplete(p=>true)&&events.Count==0,"duplicate completion does nothing");
            gesture.Press(State());events.Take();gesture.Cancel();
            Check(!gesture.TryComplete(p=>true)&&events.Count==0,"cancel removes double arm without undoing first delete");
        }
        foreach(string field in new[]{"Epoch","Root","Owner"}) {
            using(var gesture=new DeleteGesture((p,op)=>events.Add(op))) {
                var context=State();gesture.Press(context);events.Take();
                var next=State();if(field=="Epoch")next.Epoch++;if(field=="Root")next.Root++;if(field=="Owner")next.Owner++;
                Check(!gesture.TryComplete(p=>p.Epoch==next.Epoch&&p.Root==next.Root&&p.Owner==next.Owner),field+" mismatch rejects clear");
                Check(events.Count==0&&!gesture.TryComplete(p=>true),field+" rejection consumes stale arm");
            }
        }
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op+":"+p.Id))) {
            var original=State();gesture.Press(original);events.Take();original.Id="B";
            Check(gesture.TryComplete(p=>p.Root==original.Root&&p.Owner==original.Owner&&p.Epoch==original.Epoch),"selection ID change after delete permits clear");
            Check(events.Take()=="clear:A","clear retains original immutable snapshot");
            gesture.Press(State());events.Take();Thread.Sleep(DeleteGesture.Interval+20);
            Check(!gesture.TryComplete(p=>true)&&events.Count==0,"expired double cannot clear and has no timer action");
        }
        var disposed=new DeleteGesture((p,op)=>events.Add(op));disposed.Press(State());events.Take();disposed.Dispose();disposed.Press(State());
        Check(!disposed.TryComplete(p=>true)&&events.Count==0,"dispose blocks presses and completion");
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op))) {
            var trigger=new ReleaseTrigger();
            Func<bool> press=()=>{if(!gesture.TryComplete(p=>true))gesture.Press(State());return true;};
            trigger.Press(press);
            Check(events.Take()=="delete","hook first down immediately deletes");
            for(int i=0;i<30;i++)trigger.Press(press);
            Check(events.Count==0,"held autorepeats do not delete or clear");
            trigger.Release(()=>{});trigger.Press(press);trigger.Release(()=>{});
            Check(events.Take()=="clear"&&events.Count==0,"real up then down clears once");
        }
    }
    sealed class Mock : IDisposable
    {
        readonly Process child;readonly BlockingCollection<Packet> packets=new BlockingCollection<Packet>();readonly Thread reader;
        internal Mock(){child=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ClipPlus.exe"),"--worker-mock") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,StandardOutputEncoding=System.Text.Encoding.UTF8});reader=new Thread(()=>{try{string line;while((line=child.StandardOutput.ReadLine())!=null)packets.Add(new JavaScriptSerializer().Deserialize<Packet>(line));}catch{}}){IsBackground=true};reader.Start();}
        internal void Send(Packet p){child.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(p));child.StandardInput.Flush();}
        internal Packet Await(Func<Packet,bool> predicate){long end=ActionRules.Now+4000;while(ActionRules.Now<end){Packet p;if(packets.TryTake(out p,50)&&predicate(p))return p;}throw new Exception("mock IPC timeout");}
        internal string Change(string op,string id="A",long? stamp=null,long epoch=1,long root=42,long owner=1){var p=State(id,epoch,root,owner);p.Kind="change";p.Operation=op;p.Stamp=stamp??ActionRules.Now;p.Request=Guid.NewGuid().ToString("N");Send(p);return Await(x=>x.Kind=="result"&&x.Request==p.Request).Error;}
        public void Dispose(){try{child.StandardInput.Close();if(!child.WaitForExit(2000)){child.Kill();child.WaitForExit(2000);}}finally{reader.Join(1000);child.Dispose();}}
    }
    static void IpcTests()
    {
        using(var mock=new Mock()) {
            Check(mock.Await(p=>p.Kind=="state"&&p.Focused).Id=="A","synthetic worker starts on unpinned A");
            Check(mock.Change("clear",stamp:ActionRules.Now-5000)=="expired","stale clear is rejected");
            Check(mock.Change("clear",stamp:ActionRules.Now+5000)=="expired","future clear is rejected");
            Check(mock.Change("clear",epoch:0)=="expired","wrong epoch clear is rejected");
            Check(mock.Change("clear",root:99)=="expired","wrong root clear is rejected");
            Check(mock.Change("clear",owner:99)=="expired","wrong owner clear is rejected");
            Check(mock.Await(p=>p.Kind=="state"&&p.Id=="A").Focused,"rejected clears preserve A");
            Check(mock.Change("select","B")=="selected","pinned B can be selected");
            Check(mock.Await(p=>p.Kind=="state"&&p.Id=="B").Pin=="Pinned","B starts pinned");
            Check(mock.Change("clear","B")=="cleared","clear works with pinned selected item");
            var b=mock.Await(p=>p.Kind=="state"&&p.Id=="B");
            Check(b.Focused&&b.Pin=="Pinned","clear preserves pinned B");
            Check(mock.Change("delete","B")=="blocked","B remains protected after clear");
            mock.Change("select","A");Check(!mock.Await(p=>p.Kind=="state"&&p.Id=="A").Focused,"clear removes unpinned A");
            Check(mock.Change("clear")=="cleared","clear permits missing selected row after first delete");
            mock.Change("select","B");
            Check(mock.Change("clear","B")=="cleared","repeated clear handles all-pinned storage");
            mock.Send(new Packet{Kind="reset",Epoch=2,Owner=1});mock.Await(p=>p.Kind=="state"&&p.Epoch==2);
            Check(mock.Change("clear",epoch:1)=="expired","old context clear rejected after reset");
        }
    }
    static int Main(){try{GestureTests();IpcTests();Console.WriteLine("ALL PASS: "+checks+" synthetic double-delete checks. No real UI or clipboard API access.");return 0;}catch(Exception error){Console.WriteLine(error);return 1;}}
}
