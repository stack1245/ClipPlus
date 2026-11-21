using System;
using ClipboardExtensionClean;
internal static class DeferredClearTests
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
    static Packet State(long stamp=1000,long epoch=1){return new Packet{Id="A",Root=42,Owner=1,Epoch=epoch,Stamp=stamp};}
    static int Main(){try{
        var d=new DeferredClear();
        Check(d.Take("r",1,1000)==null,"empty queue has no action");
        d.Store(State(),"r");
        Check(d.Take("wrong",1,1100)==null,"wrong request does not release clear");
        Check(d.Take("r",1,1100)!=null,"matching request releases clear");
        Check(d.Take("r",1,1100)==null,"duplicate result cannot release twice");
        d.Store(State(),"r");Check(d.Take("r",1,2200)!=null,"1200ms expiry boundary allowed");
        d.Store(State(),"r");Check(d.Take("r",1,2201)==null,"1201ms expired clear rejected");
        Check(d.Take("r",1,1100)==null,"expired entry consumed permanently");
        d.Store(State(),"r");Check(d.Take("r",2,1100)==null,"epoch mismatch rejects clear");
        Check(d.Take("r",1,1100)==null,"epoch mismatch consumes entry");
        d.Store(State(),"r");d.Cancel();Check(d.Take("r",1,1100)==null,"cancel prevents pending clear");
        var source=State();d.Store(source,"r");source.Id="WRONG";source.Root=99;source.Owner=99;source.Epoch=99;source.Stamp=0;
        var taken=d.Take("r",1,1100);
        Check(taken!=null&&taken.Id=="A"&&taken.Root==42&&taken.Owner==1&&taken.Epoch==1&&taken.Stamp==1000,"store copies all context fields");
        d.Store(State(),"old");d.Store(State(1050),"new");
        Check(d.Take("old",1,1100)==null,"overwritten request cannot release new entry");
        Check(d.Take("new",1,1100).Stamp==1050,"only one latest deferred entry retained");
        d.Store(State(1200),"r");Check(d.Take("r",1,1100)==null,"future dated entry rejected");
        Console.WriteLine("ALL PASS: "+count+" pure deferred-clear checks; no UI or clipboard access.");return 0;
    }catch(Exception e){Console.WriteLine(e);return 1;}}
}
