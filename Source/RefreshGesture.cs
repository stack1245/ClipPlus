namespace ClipboardExtensionClean
{
    internal sealed class RefreshGesture
    {
        private bool held;
        private long previous=-1;
        internal bool Observe(int key,bool down,bool up,bool win,long now)
        {
            if(key==86 && up){held=false;return false;}
            if(!down)return false;
            if(key!=86){if(key!=91 && key!=92)previous=-1;return false;}
            if(held)return false;
            held=true;
            if(!win){previous=-1;return false;}
            bool twice=ActionRules.Fresh(previous,now,ActionRules.DoublePressMilliseconds);
            previous=twice?-1:now;
            return twice;
        }
    }
}
