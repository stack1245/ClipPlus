using System;
using System.Threading.Tasks;
namespace ClipboardExtensionClean
{
    internal static class ViewportReset
    {
        internal static async Task<bool> Apply(Func<bool> valid,Func<Action> resolve,int delay=160)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            if(!valid())return false;
            var scroll=resolve();
            if(scroll==null || !valid())return false;
            scroll();return true;
        }
    }
}
