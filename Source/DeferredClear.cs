namespace ClipboardExtensionClean
{
    internal sealed class DeferredClear
    {
        private Packet saved;
        private string after;
        internal void Store(Packet state,string request)
        {
            saved=new Packet {Id=state.Id,Root=state.Root,Owner=state.Owner,Epoch=state.Epoch,Stamp=state.Stamp};
            after=request;
        }
        internal Packet Take(string completed,long epoch,long now)
        {
            if(saved==null || after!=completed)return null;
            var result=saved;Cancel();
            return result.Epoch==epoch && ActionRules.Fresh(result.Stamp,now,1200)?result:null;
        }
        internal void Cancel(){saved=null;after=null;}
    }
}
