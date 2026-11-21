using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ClipboardExtensionClean;

internal sealed class Connection : IDisposable
{
    internal readonly Process Process;
    private readonly BlockingCollection<Packet> received=new BlockingCollection<Packet>();
    internal Connection(string executable)
    {
        Process=System.Diagnostics.Process.Start(new ProcessStartInfo(executable,"--worker-mock") {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
            StandardOutputEncoding=System.Text.Encoding.UTF8});
        Task.Run(delegate {
            try {string line;while((line=Process.StandardOutput.ReadLine())!=null)
                received.Add(new JavaScriptSerializer().Deserialize<Packet>(line));}catch{}
        });
    }
    internal void Send(Packet p)
    {
        Process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(p));
        Process.StandardInput.Flush();
    }
    internal Packet Await(Func<Packet,bool> condition,int timeout=4000)
    {
        long end=ActionRules.Now+timeout;
        while(ActionRules.Now<end) {
            Packet packet;
            if(received.TryTake(out packet,100) && condition(packet))return packet;
        }
        throw new Exception("IPC response timed out");
    }
    public void Dispose()
    {
        try {Process.StandardInput.Close();}catch{}
        if(!Process.WaitForExit(2000))Process.Kill();
        Process.Dispose();
    }
}

internal static class IntegrationTests
{
    private static int count;
    private static void Check(bool value,string name)
    {
        if(!value)throw new Exception("FAIL: "+name);
        count++;Console.WriteLine("PASS: "+name);
    }
    private static Packet Change(Connection c,string operation,string id,long epoch=1,long? stamp=null,long root=42)
    {
        string request=Guid.NewGuid().ToString("N");
        c.Send(new Packet {Kind="change",Operation=operation,Id=id,Epoch=epoch,Stamp=stamp??ActionRules.Now,
            Root=root,Owner=1,Request=request});
        return c.Await(p=>p.Kind=="result" && p.Request==request);
    }
    private static int Main(string[] args)
    {
        try {
            string exe=Path.GetFullPath(args[0]);
            using(var c=new Connection(exe)) {
                var initial=c.Await(p=>p.Kind=="state" && p.Focused);
                Check(initial.Id=="A" && initial.Pin=="Unpinned","mock worker starts independently without clipboard access");
                Check(Change(c,"message","A").Error=="고정 항목 보호","Korean IPC status survives UTF-8 transport");
                Check(Change(c,"toggle","A").Error=="toggled","Insert-equivalent pins the selected item");
                Check(c.Await(p=>p.Kind=="state" && p.Id=="A").Pin=="Pinned","pin state is reflected in IPC");
                Check(Change(c,"delete","A").Error=="blocked","Delete cannot remove a pinned item");
                Check(Change(c,"toggle","A").Error=="toggled","Insert-equivalent unpins the selected item");
                Check(Change(c,"toggle","WRONG").Error=="blocked","changed item identity cannot toggle another item");
                Check(Change(c,"delete","A",1,ActionRules.Now-5000).Error=="expired","stale delete is rejected");
                Check(Change(c,"delete","A",1,ActionRules.Now+5000).Error=="expired","future-dated delete is rejected");
                Check(Change(c,"delete","A",1,null,99).Error=="expired","wrong window cannot delete an item");
                string delayed=Guid.NewGuid().ToString("N");
                c.Send(new Packet {Kind="change",Operation="delete-delay",Id="A",Epoch=1,Stamp=ActionRules.Now,Root=42,Owner=1,Request=delayed});
                Thread.Sleep(80);
                c.Send(new Packet {Kind="reset",Epoch=2,Owner=1});
                Check(c.Await(p=>p.Kind=="result" && p.Request==delayed).Error=="expired","reset arriving during await cancels delayed deletion");
                Check(c.Await(p=>p.Kind=="state" && p.Epoch==2 && p.Id=="A").Focused,"cancelled deletion preserves the item");
                c.Send(new Packet {Kind="reset",Epoch=1,Owner=1});
                Check(Change(c,"delete","A",2).Error=="deleted","older reset cannot roll the context backward");
                Check(!c.Await(p=>p.Kind=="state" && p.Epoch==2 && p.Id=="A").Focused,"unfixed item was actually removed from mock storage");
                Check(Change(c,"select","B",2).Error=="selected","second fixture can be selected");
                Check(Change(c,"delete","B",2).Error=="blocked","originally pinned second fixture remains protected");
                Check(Change(c,"toggle","B",2).Error=="toggled","pinned fixture can be unpinned");
                Check(Change(c,"delete","B",2).Error=="deleted","unpinning permits deletion");
                c.Send(new Packet {Kind="hang-test"});
                Thread.Sleep(150);
                c.Process.StandardInput.Close();
                Check(c.Process.WaitForExit(2000),"parent pipe close terminates even a deliberately hung worker");
            }
            using(var restarted=new Connection(exe)) {
                Check(restarted.Await(p=>p.Kind=="state" && p.Focused).Id=="A","worker can start cleanly after the previous worker hung");
            }
            Console.WriteLine("ALL PASS: "+count+" isolated IPC/mocked-clipboard assertions.");return 0;
        } catch(Exception error) {Console.WriteLine(error);return 1;}
    }
}
