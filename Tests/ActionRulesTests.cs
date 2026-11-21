using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ClipboardExtensionClean;

internal static class ActionRulesTests
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        checks++;
        Console.WriteLine("PASS: " + label);
    }
    private static async Task Expect<T>(Task operation, string label) where T : Exception
    {
        try { await operation.ConfigureAwait(false); }
        catch (T) { Check(true, label); return; }
        throw new Exception("FAIL: " + label + " (expected " + typeof(T).Name + ")");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AbandonLateFault()
    {
        var source = new TaskCompletionSource<int>();
        try { ActionRules.Within(source.Task, 0).GetAwaiter().GetResult(); }
        catch (TimeoutException) { }
        source.SetException(new InvalidOperationException("discarded late error"));
        return new WeakReference(source.Task);
    }
    private static async Task Test()
    {
                Check(ActionRules.ValidPinBounds(10,10,32,32) && ActionRules.ValidPinBounds(-120,10,32,32), "valid pin bounds support multiple monitors");
        Check(!ActionRules.ValidPinBounds(double.NaN,0,32,32) && !ActionRules.ValidPinBounds(0,double.PositiveInfinity,32,32), "invalid pin coordinates rejected");
        Check(!ActionRules.ValidPinBounds(0,0,500,100) && !ActionRules.ValidPinBounds(0,0,0,32), "row-sized or empty click targets rejected");
        Check(ActionRules.SameControlRoot(42,42), "pin click requires the verified clipboard root");
        Check(!ActionRules.SameControlRoot(42,99), "covered pin controls cannot target a different window");
        Check(!ActionRules.SameControlRoot(0,0) && !ActionRules.SameControlRoot(42,0), "missing control windows never count as matching");
        Check(ActionRules.Fresh(10, 10, 0), "same instant is fresh");
        Check(ActionRules.Fresh(10, 110, 100), "deadline boundary is inclusive");
        Check(!ActionRules.Fresh(10, 111, 100), "expired observation is rejected");
        Check(!ActionRules.Fresh(11, 10, 100), "future observation is rejected");
        Check(!ActionRules.Fresh(-1, 10, 100), "negative timestamp is rejected");
        Check(!ActionRules.Fresh(0, -1, 100), "negative current time is rejected");
        Check(!ActionRules.Fresh(0, 0, -1), "negative freshness budget is rejected");
        Check(ActionRules.Fresh(long.MaxValue - 1, long.MaxValue, 1), "large timestamps remain safe");
        Check(!ActionRules.Fresh(2147483640L, 5, 100), "wrapped old clock is not fresh");
        long before = ActionRules.Now;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(30).ConfigureAwait(false);
        long after = ActionRules.Now;
        Check(after >= before && after - before >= 15 && after - before <= elapsed.ElapsedMilliseconds + 50,
            "monotonic clock advances in milliseconds");

        string id = "{ABC-def}";
        Check(ActionRules.CanDelete(id, "{abc-DEF}", PinState.Unpinned), "Delete accepts matching unpinned item");
        Check(!ActionRules.CanDelete(id, id, PinState.Pinned), "Delete preserves pinned item");
        Check(!ActionRules.CanDelete(id, id, PinState.Unknown), "Delete rejects unknown pin state");
        Check(!ActionRules.CanDelete(id, "other", PinState.Unpinned), "Delete rejects changed selection");
        Check(!ActionRules.CanDelete(null, id, PinState.Unpinned)
            && !ActionRules.CanDelete(id, " ", PinState.Unpinned), "Delete rejects missing identity");
        Check(ActionRules.CanToggle(id, "{abc-DEF}", PinState.Pinned), "Insert accepts pinned item for unpinning");
        Check(ActionRules.CanToggle(id, id, PinState.Unpinned), "Insert accepts unpinned item for pinning");
        Check(!ActionRules.CanToggle(id, id, PinState.Unknown), "Insert rejects unknown state");
        Check(!ActionRules.CanToggle(id, "other", PinState.Pinned), "Insert rejects changed selection");
        Check(!ActionRules.CanToggle("", id, PinState.Pinned), "Insert rejects missing identity");
        Check(!ActionRules.CanToggle(id, id, (PinState)99)
            && !ActionRules.CanDelete(id, id, (PinState)99), "invalid enum states fail closed");

        Check(await ActionRules.Within(Task.FromResult(42), 0).ConfigureAwait(false) == 42,
            "already completed result works at zero deadline");
        var delayed = new TaskCompletionSource<int>();
        var success = ActionRules.Within(delayed.Task, 2000);
        delayed.SetResult(9);
        Check(await success.ConfigureAwait(false) == 9, "asynchronous successful result propagates");
        await Expect<ArgumentNullException>(ActionRules.Within<int>(null, 1), "null task rejected");
        await Expect<ArgumentOutOfRangeException>(ActionRules.Within(Task.FromResult(1), -1), "negative timeout rejected");
        var unfinished = new TaskCompletionSource<int>();
        var clock = Stopwatch.StartNew();
        await Expect<TimeoutException>(ActionRules.Within(unfinished.Task, 25), "unfinished read times out");
        Check(clock.ElapsedMilliseconds < 1500 && !unfinished.Task.IsCompleted,
            "timeout does not complete or cancel original read");
        unfinished.SetResult(7);
        Check(unfinished.Task.Result == 7, "original read can finish independently after timeout");
        var fault = new TaskCompletionSource<int>();
        var exactError = new InvalidOperationException("test error");
        var faultRead = ActionRules.Within(fault.Task, 2000);
        fault.SetException(exactError);
        try { await faultRead.ConfigureAwait(false); throw new Exception("missing fault"); }
        catch (InvalidOperationException error) { Check(ReferenceEquals(error, exactError), "fault identity propagates"); }
        var canceled = new TaskCompletionSource<int>();
        var cancellationRead = ActionRules.Within(canceled.Task, 2000);
        canceled.SetCanceled();
        await Expect<TaskCanceledException>(cancellationRead, "cancellation propagates");
        var lateSuccess = new TaskCompletionSource<int>();
        bool mutation = false;
        Func<Task> caller = async () => {
            await ActionRules.Within(lateSuccess.Task, 20).ConfigureAwait(false);
            mutation = true;
        };
        await Expect<TimeoutException>(caller(), "timed-out caller stops before mutation");
        lateSuccess.SetResult(1);
        await Task.Delay(20).ConfigureAwait(false);
        Check(!mutation, "late success cannot re-enter mutation continuation");
        var lateFailure = new TaskCompletionSource<int>();
        await Expect<TimeoutException>(ActionRules.Within(lateFailure.Task, 20), "late-fault setup times out");
        lateFailure.SetException(new InvalidOperationException("late error"));
        await Task.Delay(20).ConfigureAwait(false);
        Check(lateFailure.Task.IsFaulted, "late-fault observation preserves original task state");
        int unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> observe = (sender, args) => {
            Interlocked.Increment(ref unobserved);
            args.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += observe;
        try {
            WeakReference abandoned = AbandonLateFault();
            for (int attempt = 0; attempt < 30 && abandoned.IsAlive; attempt++) {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Task.Delay(10).ConfigureAwait(false);
            }
            Check(!abandoned.IsAlive, "discarded late-fault task is actually collected");
            Check(unobserved == 0, "late fault is observed before task finalization");
        } finally { TaskScheduler.UnobservedTaskException -= observe; }
    }
    public static int Main()
    {
        try {
            Test().GetAwaiter().GetResult();
            Console.WriteLine("ALL PASS: " + checks + " non-destructive assertions.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
