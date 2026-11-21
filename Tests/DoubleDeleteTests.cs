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
        Check(DeleteGesture.Interval==ActionRules.DoublePressMilliseconds,"Delete and refresh share the same interval");
        var events=new BlockingCollection<string>();
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op+":"+p.Id))) {
            gesture.Press(State());string value;
            Check(!events.TryTake(out value,100),"single delete is deferred");
            Check(events.TryTake(out value,1500)&&value=="delete:A","single delete fires after interval");
            Check(!events.TryTake(out value,100),"single delete fires once");
            gesture.Press(State());gesture.Press(State("a"));
            Check(events.TryTake(out value,100)&&value=="clear:A","second matching press replaces single with clear");
            Check(!events.TryTake(out value,DeleteGesture.Interval+100),"double delete cancels old single timer");
            gesture.Press(State());gesture.Cancel();
            Check(!events.TryTake(out value,DeleteGesture.Interval+100),"cancel prevents deferred single");
        }
        foreach(string field in new[]{"Id","Epoch","Root","Owner"}) {
            using(var gesture=new DeleteGesture((p,op)=>events.Add(op+":"+p.Id))) {
                gesture.Press(State());var next=State();
                if(field=="Id")next.Id="B";if(field=="Epoch")next.Epoch=2;if(field=="Root")next.Root=43;if(field=="Owner")next.Owner=2;
                gesture.Press(next);string value;
                Check(!events.TryTake(out value,50),field+" change cannot trigger clear");
                Check(events.TryTake(out value,1500)&&value=="delete:"+next.Id,field+" change arms only new single");
                Check(!events.TryTake(out value,60),field+" change cancels old single");
            }
        }
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op+":"+p.Id))) {
            var source=State();gesture.Press(source);source.Id="MUTATED";string value;
            Check(events.TryTake(out value,1500)&&value=="delete:A","pending selection is immutable snapshot");
            gesture.Press(State());
            var saved=(Packet)typeof(DeleteGesture).GetField("first",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(gesture);
            gesture.Cancel();
            typeof(DeleteGesture).GetMethod("Single",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(gesture,new object[]{saved});
            Check(!events.TryTake(out value,50),"already queued callback after cancel is harmless");
        }
        var disposed=new DeleteGesture((p,op)=>events.Add(op));disposed.Press(State());disposed.Dispose();disposed.Press(State());
        string ignored;Check(!events.TryTake(out ignored,DeleteGesture.Interval+100),"dispose cancels old timer and ignores new press");
        using(var gesture=new DeleteGesture((p,op)=>events.Add(op))) {
            var trigger=new ReleaseTrigger();
            trigger.Press(()=>{gesture.Press(State());return true;});
            for(int i=0;i<30;i++)trigger.Press(()=>{gesture.Press(State());return true;});
            Check(!events.TryTake(out ignored,80),"held autorepeats cannot synthesize double delete");
            trigger.Release(()=>{});
            trigger.Press(()=>{gesture.Press(State());return true;});trigger.Release(()=>{});
            Check(events.TryTake(out ignored,100)&&ignored=="clear","real up then down triggers clear");
            Check(!events.TryTake(out ignored,DeleteGesture.Interval+100),"repeat plus real double yields one action");
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
            Check(mock.Change("clear")=="blocked","clear requires a live selected row");
            mock.Change("select","B");
            Check(mock.Change("clear","B")=="cleared","repeated clear handles all-pinned storage");
            mock.Send(new Packet{Kind="reset",Epoch=2,Owner=1});mock.Await(p=>p.Kind=="state"&&p.Epoch==2);
            Check(mock.Change("clear",epoch:1)=="expired","old context clear rejected after reset");
        }
    }
    static int Main(){try{GestureTests();IpcTests();Console.WriteLine("ALL PASS: "+checks+" synthetic double-delete checks. No real UI or clipboard API access.");return 0;}catch(Exception error){Console.WriteLine(error);return 1;}}
}
