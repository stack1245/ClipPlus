using System;
using System.Threading.Tasks;
using ClipboardExtensionClean;

internal static class ViewportResetTests
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
    static async Task Tests()
    {
        int resolved=0,scrolled=0;bool valid=true;
        var delayed=ViewportReset.Apply(()=>valid,()=>{resolved++;return ()=>scrolled++;},40);
        valid=false;
        Check(!await delayed,"cancellation during delay returns false");
        Check(resolved==0&&scrolled==0,"delay cancellation skips resolve and action");

        valid=true;
        Check(!await ViewportReset.Apply(()=>valid,()=>{resolved++;valid=false;return ()=>scrolled++;},0),"cancellation inside resolve returns false");
        Check(resolved==1&&scrolled==0,"post-resolve guard blocks stale action");
        Check(!await ViewportReset.Apply(()=>true,()=>null,0),"missing row or pattern returns false");

        int validations=0;resolved=0;scrolled=0;
        Check(await ViewportReset.Apply(()=>{validations++;return true;},()=>{resolved++;return ()=>scrolled++;},0),"valid request scrolls successfully");
        Check(validations==2&&resolved==1&&scrolled==1,"success checks twice and resolves and scrolls exactly once");

        foreach(string context in new[]{"epoch","age","owner","root-visible","root-identity"}) {
            bool contextValid=false;int action=0,lookup=0;
            Check(!await ViewportReset.Apply(()=>contextValid,()=>{lookup++;return ()=>action++;},0),context+" guard rejects invalid context");
            Check(lookup==0&&action==0,context+" rejection has no side effects");
        }
        bool threw=false;
        try {await ViewportReset.Apply(()=>{throw new InvalidOperationException("guard");},()=>()=>{},0);}
        catch(InvalidOperationException error){threw=error.Message=="guard";}
        Check(threw,"guard exception propagates to caller");
        threw=false;
        try {await ViewportReset.Apply(()=>true,()=>{throw new InvalidOperationException("resolve");},0);}
        catch(InvalidOperationException error){threw=error.Message=="resolve";}
        Check(threw,"resolve exception propagates to caller");
        int attempts=0;threw=false;
        try {await ViewportReset.Apply(()=>true,()=>()=>{attempts++;throw new InvalidOperationException("scroll");},0);}
        catch(InvalidOperationException error){threw=error.Message=="scroll";}
        Check(threw&&attempts==1,"scroll exception propagates without retry");

        var resolvedSignal=new TaskCompletionSource<bool>();
        var completion=ViewportReset.Apply(()=>true,()=>{resolvedSignal.SetResult(true);return ()=>{};},40);
        Check(!resolvedSignal.Task.IsCompleted,"resolve waits for configured delay");
        Check(await completion&&resolvedSignal.Task.IsCompleted,"delayed success eventually completes");
    }
    static int Main(){try{Tests().GetAwaiter().GetResult();Console.WriteLine("ALL PASS: "+checks+" pure viewport guard checks; no UI or clipboard access.");return 0;}catch(Exception error){Console.WriteLine(error);return 1;}}
}
