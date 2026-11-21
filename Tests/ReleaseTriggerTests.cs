using System;
using ClipboardExtensionClean;

internal static class ReleaseTriggerTests
{
    private static int count;
    private static void Check(bool condition,string name)
    {
        if(!condition)throw new Exception("FAIL: "+name);
        count++;Console.WriteLine("PASS: "+name);
    }
    public static int Main()
    {
        try {
            var insert=new ReleaseTrigger();int prepares=0,commits=0;
            Func<bool> prepare=()=>{prepares++;return true;};
            Check(!insert.Release(()=>commits++),"orphan up passes without commit");
            Check(insert.Press(prepare),"accepted down is swallowed");
            for(int i=0;i<20;i++)Check(insert.Press(()=>{throw new Exception("repeat prepared");}),"held repeat swallowed "+i);
            Check(prepares==1 && commits==0,"down and repeat never commit");
            Check(insert.Release(()=>commits++),"matching up is swallowed");
            Check(commits==1,"matching up commits once");
            Check(!insert.Release(()=>commits++),"duplicate up passes");
            Check(commits==1,"duplicate up never commits");
            Check(insert.Press(prepare) && insert.Release(()=>commits++),"next gesture re-arms normally");
            Check(commits==2 && prepares==2,"two gestures each prepare and commit once");

            var rejected=new ReleaseTrigger();int rejectedPrepares=0;
            Check(!rejected.Press(()=>{rejectedPrepares++;return false;}),"rejected down passes");
            Check(!rejected.Release(()=>{throw new Exception("rejected committed");}),"rejected down has no consumed up");
            Check(rejected.Press(()=>{rejectedPrepares++;return true;}),"rejection does not leave held state");
            rejected.Release(()=>{});
            Check(rejectedPrepares==2,"new accepted press can follow rejection");

            var canceled=new ReleaseTrigger();bool armed=false;int canceledCommits=0;
            Check(canceled.Press(()=>{armed=true;return true;}),"cancellable gesture prepares");
            armed=false;
            Check(canceled.Press(()=>{throw new Exception("canceled gesture re-armed");}),"canceling external arm preserves held repeat suppression");
            Check(canceled.Release(()=>{if(armed)canceledCommits++;}),"canceled gesture still swallows matching up");
            Check(canceledCommits==0,"invalidated arm never performs action");

            var exceptional=new ReleaseTrigger();exceptional.Press(()=>true);bool threw=false;
            try {exceptional.Release(()=>{throw new InvalidOperationException("synthetic");});}
            catch(InvalidOperationException){threw=true;}
            Check(threw,"commit exception remains observable");
            Check(!exceptional.Release(()=>{throw new Exception("retried commit");}),"commit exception cannot retain held or retry");
            Check(exceptional.Press(()=>true),"commit exception permits next gesture");
            bool nestedRelease=true;
            exceptional.Release(()=>nestedRelease=exceptional.Release(()=>{}));
            Check(!nestedRelease,"held is cleared before commit callback");

            var badPrepare=new ReleaseTrigger();threw=false;
            try {badPrepare.Press(()=>{throw new InvalidOperationException("synthetic");});}
            catch(InvalidOperationException){threw=true;}
            Check(threw && !badPrepare.Release(()=>{}),"prepare exception never arms release");

            var pin=new ReleaseTrigger();var delete=new ReleaseTrigger();int deleted=0,pinned=0;
            pin.Press(()=>true);
            Check(delete.Press(()=>{deleted++;return true;}),"Delete remains independent while Insert is held");
            Check(delete.Press(()=>{deleted++;return true;}),"Delete repeat swallowed independently");
            Check(delete.Release(()=>{}),"Delete release accepted independently");
            Check(deleted==1 && pinned==0,"Delete acts once on down and never commits Insert");
            Check(pin.Press(()=>{throw new Exception("Insert lost held");}),"Delete release preserves Insert held");
            Check(pin.Release(()=>pinned++),"Insert release remains available after Delete");
            Check(pinned==1 && deleted==1,"independent keys each act exactly once");
            Console.WriteLine("ALL PASS: "+count+" pure ReleaseTrigger assertions; no UI, hook, key injection, or clipboard access.");
            return 0;
        } catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
