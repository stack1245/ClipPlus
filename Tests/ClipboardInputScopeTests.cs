using System;
using ClipboardExtensionClean;
internal static class ClipboardInputScopeTests
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
    static Packet Bound(){return new Packet{Root=42,Owner=7,Epoch=3,Stamp=1000,Visible=true,Focused=true};}
    static bool Own(Packet latest,Packet bound,long now=1100,bool busy=false,long epoch=3,long foreground=7,bool visible=true){return ClipboardInputScope.Owns(latest,bound,epoch,now,busy,foreground,visible);}
    static int Main(){try{
        Check(Own(Bound(),Bound()),"fresh focused popup owns keys");
        Check(!ActionRules.Fresh(1000,1750,450)&&Own(Bound(),Bound(),1750),"750ms stale mutation is blocked but popup still consumes keys");
        Check(Own(Bound(),Bound(),2500),"1500ms guarded key scope boundary allowed");
        Check(!Own(Bound(),Bound(),2501),"idle scope expires after 1500ms");
        Check(Own(Bound(),Bound(),9000,true),"busy request retains same popup key scope");
        var latest=Bound();latest.Focused=false;latest.Owner=0;
        Check(Own(latest,Bound()),"lost focused row retains known popup ownership");
        latest.Id="new-selection";Check(Own(latest,Bound()),"row identity is not required for key consumption only");
        latest=Bound();latest.Root=43;Check(!Own(latest,Bound()),"different root cannot consume keys");
        latest=Bound();latest.Epoch=4;Check(!Own(latest,Bound()),"latest epoch mismatch rejected");
        var bound=Bound();bound.Epoch=2;Check(!Own(Bound(),bound),"bound epoch mismatch rejected");
        Check(!Own(Bound(),Bound(),epoch:4),"current epoch mismatch rejected");
        Check(!Own(Bound(),Bound(),foreground:8),"different native foreground owner rejected");
        latest=Bound();latest.Owner=8;Check(!Own(latest,Bound()),"explicit latest owner change rejected");
        latest=Bound();latest.Visible=false;Check(!Own(latest,Bound()),"hidden latest popup rejected");
        Check(!Own(Bound(),Bound(),visible:false),"hidden native root rejected");
        bound=Bound();bound.Visible=false;Check(!Own(Bound(),bound),"hidden bound popup rejected");
        bound=Bound();bound.Focused=false;Check(!Own(Bound(),bound),"scope must originate from actually focused popup");
        latest=Bound();latest.Stamp=1200;Check(!Own(latest,Bound(),1100,true),"busy cannot override future timestamp");
        latest=Bound();latest.Stamp=-1;Check(!Own(latest,Bound(),1100,true),"busy cannot override negative timestamp");
        Check(!Own(null,Bound())&&!Own(Bound(),null),"null snapshots rejected");
        Check(!Own(Bound(),new Packet()),"disposed or reset empty bound never consumes keys");
        bound=Bound();bound.Root=0;Check(!Own(Bound(),bound),"zero root rejected");
        bound=Bound();bound.Owner=0;Check(!Own(Bound(),bound),"zero owner rejected");
        Check(!Own(Bound(),Bound(),9000,true,foreground:8),"busy does not override changed foreground");
        Check(!Own(Bound(),Bound(),9000,true,visible:false),"busy does not override hidden root");
        Console.WriteLine("ALL PASS: "+checks+" pure input-scope assertions; no UI or clipboard access.");return 0;
    }catch(Exception error){Console.WriteLine(error);return 1;}}
}
