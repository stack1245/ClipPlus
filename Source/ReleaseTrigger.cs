using System;
namespace ClipboardExtensionClean
{
    internal sealed class ReleaseTrigger
    {
        private bool held;
        internal bool Press(Func<bool> prepare)
        {
            if(held)return true;
            held=prepare();return held;
        }
        internal bool Release(Action commit)
        {
            if(!held)return false;
            held=false;
            commit();return true;
        }
    }
}
