using System;
namespace ClipboardExtensionClean
{
    internal sealed class DeleteGesture : IDisposable
    {
        internal const int Interval=ActionRules.DoublePressMilliseconds;
        private readonly object gate=new object();
        private readonly Action<Packet,string> dispatch;
        private Packet first;
        private bool disposed;
        internal DeleteGesture(Action<Packet,string> execute){dispatch=execute;}
        internal bool TryComplete(Func<Packet,bool> valid)
        {
            lock(gate)
            {
                var saved=first;
                if(disposed || saved==null)return false;
                first=null;
                if(!ActionRules.Fresh(saved.Stamp,ActionRules.Now,Interval) || !valid(saved))return false;
                dispatch(new Packet {Id=saved.Id,Root=saved.Root,Owner=saved.Owner,Epoch=saved.Epoch,Stamp=ActionRules.Now},"clear");
                return true;
            }
        }
        internal void Press(Packet state)
        {
            lock(gate)
            {
                if(disposed)return;
                var saved=new Packet {Id=state.Id,Root=state.Root,Owner=state.Owner,Epoch=state.Epoch,Stamp=ActionRules.Now};
                first=saved;
                dispatch(saved,"delete");
            }
        }
        internal void Cancel(){lock(gate)first=null;}
        public void Dispose(){lock(gate){disposed=true;first=null;}}
    }
}
