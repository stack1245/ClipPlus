using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.ApplicationModel.DataTransfer;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace ClipboardExtensionClean
{
    internal interface ISurface
    {
        Packet Probe();
        void Reset(long owner, long epoch, int pause);
        Task<string> Change(Packet request);
    }

    internal sealed class ClipboardSurface : ISurface
    {
        private readonly ContextFence fence;
        private readonly Action<Packet> progress;
        private IntPtr root;
        private long owner, epoch = 1, notBefore, nextDiscovery;
        private readonly List<IntPtr> candidates = new List<IntPtr>();
        private static readonly Regex ItemPattern = new Regex(@"^item-ClipboardHistory-(\{[0-9a-fA-F-]{36}\})", RegexOptions.Compiled);
        internal ClipboardSurface(ContextFence authority,Action<Packet> report=null) {fence=authority;progress=report;}

        public void Reset(long newOwner, long newEpoch, int pause)
        {
            if(newEpoch<epoch)return;
            fence.Advance(newEpoch);
            owner = newOwner; epoch = newEpoch; notBefore = ActionRules.Now + pause;
            root = IntPtr.Zero; nextDiscovery = 0;
        }

        private static AutomationElement Panel(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !Native.IsWindowVisible(handle)) return null;
            var result = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "expanded-view-list-ClipboardHistory"));
            return result != null && !result.Current.IsOffscreen ? result : null;
        }

        private IntPtr FindPanel()
        {
            if(root!=IntPtr.Zero)
            {
                try {if(Panel(root)!=null)return root;}catch { }
                root=IntPtr.Zero;
            }
            if (ActionRules.Now >= nextDiscovery)
            {
                nextDiscovery = ActionRules.Now + 750;
                var hosts = new HashSet<uint>();
                candidates.Clear();
                int session = Process.GetCurrentProcess().SessionId;
                foreach (var process in Process.GetProcessesByName("TextInputHost"))
                {
                    try {
                        if(process.SessionId!=session)continue;
                        hosts.Add((uint)process.Id);
                        foreach(ProcessThread thread in process.Threads)
                        {
                            var info=new Native.GuiThread {Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.GuiThread))};
                            if(Native.GetGUIThreadInfo((uint)thread.Id,ref info))
                            {
                                string type=Native.ClassName(info.Focus);
                                IntPtr focused=type=="Windows.UI.Core.CoreComponentInputSource"?
                                    Native.GetParent(info.Focus):info.Focus;
                                if(Native.ClassName(focused)=="Windows.UI.Core.CoreWindow" && Native.IsWindowVisible(focused))
                                    candidates.Add(focused);
                            }
                            Native.EnumThreadWindows((uint)thread.Id,delegate(IntPtr handle,IntPtr ignored)
                            {
                                if(Native.IsWindowVisible(handle) && Native.ClassName(handle)=="Windows.UI.Core.CoreWindow")
                                    candidates.Add(handle);
                                return true;
                            },IntPtr.Zero);
                        }
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                Native.EnumWindows(delegate(IntPtr handle, IntPtr ignored)
                {
                    uint pid; Native.GetWindowThreadProcessId(handle, out pid);
                    if (hosts.Contains(pid) && Native.IsWindowVisible(handle)
                        && Native.ClassName(handle)=="Windows.UI.Core.CoreWindow") candidates.Add(handle);
                    return true;
                }, IntPtr.Zero);
            }
            foreach (IntPtr candidate in candidates.Distinct())
            {
                try { if (Panel(candidate) != null) return root = candidate; }
                catch { }
            }
            root = IntPtr.Zero;
            return root;
        }

        private static string RowId(AutomationElement element)
        {
            var match = ItemPattern.Match(element.Current.AutomationId ?? "");
            Guid parsed;
            return match.Success && Guid.TryParse(match.Groups[1].Value, out parsed) ? match.Groups[1].Value : "";
        }

        private static AutomationElement FocusedRow(IntPtr handle)
        {
            var window = AutomationElement.FromHandle(handle);
            foreach (AutomationElement focused in window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)))
            {
                var node = focused;
                for (int depth = 0; depth != 16 && node != null; depth++)
                {
                    if (!node.Current.IsOffscreen && RowId(node).Length != 0) return node;
                    node = TreeWalker.ControlViewWalker.GetParent(node);
                }
            }
            return null;
        }

        private static AutomationElement PinButton(AutomationElement row, out PinState state)
        {
            state = PinState.Unknown;
            foreach (AutomationElement element in row.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                if (element.Current.IsOffscreen || !element.Current.IsEnabled) continue;
                string name = element.Current.Name;
                if (name == "항목 고정 해제" || name == "Unpin item")
                { state = PinState.Pinned; return element; }
                if (name == "항목 고정" || name == "Pin item")
                { state = PinState.Unpinned; return element; }
            }
            return null;
        }

        public Packet Probe()
        {
            var state = new Packet { Kind="state", Epoch=epoch, Stamp=ActionRules.Now, Pin="Unknown" };
            if (ActionRules.Now < notBefore || owner==0) return state;
            IntPtr handle = FindPanel();
            if (handle == IntPtr.Zero) return state;
            state.Root = handle.ToInt64(); state.Visible = true;
            var row = FocusedRow(handle);
            if (row == null) return state;
            // Win+V is non-activating: native foreground may remain the caller.
            // Bind only while a real clipboard row reports keyboard focus.
            if (owner == 0) owner = Native.GetForegroundWindow().ToInt64();
            if (Native.GetForegroundWindow().ToInt64() != owner) return state;
            PinState pin; PinButton(row, out pin);
            state.Owner = owner; state.Id = RowId(row); state.Pin = pin.ToString(); state.Focused = true;
            state.Stamp=ActionRules.Now;
            return state;
        }

        private Packet Verify(Packet request)
        {
            if (request.Epoch != epoch || request.Epoch!=fence.Value || !ActionRules.Fresh(request.Stamp, ActionRules.Now, 1200))
                throw new InvalidOperationException("요청이 만료됐습니다.");
            Packet current = Probe();
            if (!current.Focused || current.Root != request.Root || current.Owner != request.Owner
                || !string.Equals(current.Id, request.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("선택 항목 또는 입력 창이 바뀌었습니다.");
            return current;
        }
        private bool VerifyPopup(Packet request)
        {
            if(request.Epoch!=epoch || request.Epoch!=fence.Value
                || !ActionRules.Fresh(request.Stamp,ActionRules.Now,1200)
                || owner!=request.Owner || Native.GetForegroundWindow().ToInt64()!=request.Owner
                || Panel(new IntPtr(request.Root))==null)
                throw new InvalidOperationException("클립보드 창이 바뀌었거나 요청이 만료됐습니다.");
            return true;
        }

        public async Task<string> Change(Packet request)
        {
            if(request.Operation=="clear")
            {
                string result=await StaClipboard.ClearAsync(()=> {
                    return VerifyPopup(request);
                },()=>request.Epoch==fence.Value && ActionRules.Fresh(request.Stamp,ActionRules.Now,1200)
                    && Native.GetForegroundWindow().ToInt64()==request.Owner
                    && Native.IsWindowVisible(new IntPtr(request.Root)));
                try
                {
                    bool restored=await ViewportReset.Apply(()=>request.Epoch==fence.Value
                        && ActionRules.Fresh(request.Stamp,ActionRules.Now,1200)
                        && Native.GetForegroundWindow().ToInt64()==request.Owner
                        && Native.IsWindowVisible(new IntPtr(request.Root)),()=> {
                        var panel=Panel(new IntPtr(request.Root));
                        if(panel==null)return null;
                        foreach(AutomationElement row in panel.FindAll(TreeScope.Descendants,Condition.TrueCondition))
                        {
                            if(RowId(row).Length==0)continue;
                            object pattern;
                            if(row.TryGetCurrentPattern(ScrollItemPattern.Pattern,out pattern))
                                return (Action)(()=>((ScrollItemPattern)pattern).ScrollIntoView());
                            break;
                        }
                        return null;
                    });
                    Wire.Log("clear list start restored: "+restored);
                }
                catch(Exception error){Wire.Log("clear scroll reset: "+error.GetType().Name);}
                return result;
            }
            Packet before = Verify(request);
            PinState pin; Enum.TryParse(before.Pin, out pin);
            if (request.Operation == "delete")
            {
                if (!ActionRules.CanDelete(request.Id, before.Id, pin)) return "고정 항목은 삭제하지 않습니다.";
                return await StaClipboard.DeleteAsync(request.Id,()=> {
                    Packet final=Verify(request);PinState checkedPin;Enum.TryParse(final.Pin,out checkedPin);
                    return ActionRules.CanDelete(request.Id,final.Id,checkedPin);
                },()=>request.Epoch==fence.Value && ActionRules.Fresh(request.Stamp,ActionRules.Now,1200)
                    && Native.GetForegroundWindow().ToInt64()==request.Owner
                    && Native.IsWindowVisible(new IntPtr(request.Root)));
            }
            if (request.Operation != "toggle") throw new InvalidOperationException("알 수 없는 동작입니다.");
            if (!ActionRules.CanToggle(request.Id, before.Id, pin)) return "고정 상태를 확인하지 못했습니다.";
            return await DirectPin.ToggleAsync(request.Id,pin,()=> {
                var final=Verify(request);
                return final.Pin==pin.ToString() && request.Epoch==fence.Value;
            });
        }
    }
    internal sealed class MockSurface : ISurface
    {
        private readonly ContextFence fence;
        private readonly Dictionary<string,PinState> items = new Dictionary<string,PinState> {
            {"A",PinState.Unpinned},{"B",PinState.Pinned} };
        private string selected = "A";
        private long epoch = 1;
        internal MockSurface(ContextFence authority){fence=authority;}
        public void Reset(long owner, long next, int pause) { if(next>=epoch){epoch=next;fence.Advance(next);} }
        public Packet Probe() { return new Packet {Kind="state",Root=42,Owner=1,Epoch=epoch,Stamp=ActionRules.Now,
            Id=selected,Visible=true,Focused=items.ContainsKey(selected),
            Pin=items.ContainsKey(selected)?items[selected].ToString():"Unknown"}; }
        public async Task<string> Change(Packet request)
        {
            if (request.Operation=="hold-test") {await new TaskCompletionSource<bool>().Task;return "unreachable";}
            if (request.Operation=="select") { selected=request.Id; return "selected"; }
            if(request.Operation=="message")return "고정 항목 보호";
            if(request.Operation=="delete-delay")await Task.Delay(400);
            if (request.Epoch!=epoch || request.Epoch!=fence.Value || request.Root!=42 || request.Owner!=1 ||
                !ActionRules.Fresh(request.Stamp,ActionRules.Now,1200))
                throw new InvalidOperationException("expired");
            var current=Probe(); PinState pin; Enum.TryParse(current.Pin,out pin);
            if(request.Operation=="clear")
            {
                foreach(var id in items.Where(x=>x.Value==PinState.Unpinned).Select(x=>x.Key).ToArray())items.Remove(id);
                return "cleared";
            }
            if ((request.Operation=="delete" || request.Operation=="delete-delay") && ActionRules.CanDelete(request.Id,current.Id,pin))
            { items.Remove(request.Id);return "deleted"; }
            if (request.Operation=="toggle" && ActionRules.CanToggle(request.Id,current.Id,pin))
            { items[request.Id]=pin==PinState.Pinned?PinState.Unpinned:PinState.Pinned;return "toggled"; }
            return "blocked";
        }
    }
}
