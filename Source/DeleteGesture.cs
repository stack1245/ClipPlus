using System;
using System.Threading;
namespace ClipboardExtensionClean
{
    internal sealed class DeleteGesture : IDisposable
    {
        internal const int Interval=400;
        private readonly object gate=new object();
        private readonly Action<Packet,string> dispatch;
        private Packet first;
        private Timer timer;
        private bool disposed;
        internal DeleteGesture(Action<Packet,string> execute){dispatch=execute;}
        internal void Press(Packet state)
        {
            lock(gate)
            {
                if(disposed)return;
                long now=ActionRules.Now;
                if(first!=null && ActionRules.Fresh(first.Stamp,now,Interval)
                    && first.Epoch==state.Epoch && first.Root==state.Root && first.Owner==state.Owner
                    && string.Equals(first.Id,state.Id,StringComparison.OrdinalIgnoreCase))
                {var clear=first;CancelLocked();dispatch(clear,"clear");}
                else
                {
                    CancelLocked();
                    var saved=new Packet {Id=state.Id,Root=state.Root,Owner=state.Owner,Epoch=state.Epoch,Stamp=now};
                    first=saved;
                    timer=new Timer(_=>Single(saved),null,Interval,Timeout.Infinite);
                }
            }
        }
        private void Single(Packet saved)
        {
            lock(gate)
            {
                if(disposed || !ReferenceEquals(first,saved))return;
                int remaining=Interval-(int)(ActionRules.Now-saved.Stamp);
                if(remaining>0){timer.Change(remaining,Timeout.Infinite);return;}
                CancelLocked();
                try{dispatch(saved,"delete");}
                catch(Exception error){Wire.Log("delayed delete: "+error.GetType().Name);}
            }
        }
        private void CancelLocked(){first=null;if(timer!=null){timer.Dispose();timer=null;}}
        internal void Cancel(){lock(gate)CancelLocked();}
        public void Dispose(){lock(gate){disposed=true;CancelLocked();}}
    }
}
