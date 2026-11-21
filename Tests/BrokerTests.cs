using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ClipboardExtensionClean;
internal static class BrokerTests
{
    private static int Main(string[] args)
    {
        object instance=null;
        var type=typeof(Packet).Assembly.GetType("ClipboardExtensionClean.WorkerClient");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try {
            instance=Activator.CreateInstance(type,flags,null,new object[]{true,System.IO.Path.GetFullPath(args[0])},null);
            var latest=type.GetProperty("Latest",flags);
            var healthy=type.GetProperty("Healthy",flags);
            var error=type.GetProperty("HealthError",flags);
            var reset=type.GetMethod("Reset",flags);
            var timer=Stopwatch.StartNew();
            while(!((Packet)latest.GetValue(instance)).Focused) {
                if(timer.ElapsedMilliseconds>5000)throw new Exception("initial snapshot timeout: "+error.GetValue(instance));
                Thread.Sleep(20);
            }
            Console.WriteLine("PASS: production broker receives mock worker snapshot");
            for(int i=0;i<20;i++) {reset.Invoke(instance,new object[]{1L,0});Thread.Sleep(5);}
            while(((Packet)latest.GetValue(instance)).Epoch!=21) {
                if(timer.ElapsedMilliseconds>7000)throw new Exception("reset delivery failed: "+error.GetValue(instance));
                Thread.Sleep(20);
            }
            Console.WriteLine("PASS: ordered reset burst converges to the current epoch");
            var stable=Stopwatch.StartNew();
            while(stable.ElapsedMilliseconds<10000) {
                if(!(bool)healthy.GetValue(instance))throw new Exception("broker unhealthy: "+error.GetValue(instance));
                Thread.Sleep(50);
            }
            Console.WriteLine("PASS: production broker maintains a live worker for ten seconds");
            return 0;
        } catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally {if(instance!=null)((IDisposable)instance).Dispose();}
    }
}
