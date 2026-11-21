using System;
using System.Linq;
using System.Threading.Tasks;
using WindowsUdk.ApplicationModel.DataTransfer;
namespace ClipboardExtensionClean
{
    internal static class DirectPin
    {
        internal static async Task<string> ToggleAsync(string id,PinState expected,Func<bool> verify)
        {
            Guid key;
            if(!Guid.TryParse(id,out key))throw new InvalidOperationException("선택 항목 ID가 올바르지 않습니다.");
            var history=ClipboardHistory.Current;
            var items=await ActionRules.Within(history.GetItemsAsync().AsTask(),1200);
            var item=items.FirstOrDefault(x=>x.Id==key);
            if(item==null || !item.CanPinUnpin)throw new InvalidOperationException("선택 항목을 고정할 수 없습니다.");
            bool pinned=item.IsPinned;
            if((pinned?PinState.Pinned:PinState.Unpinned)!=expected || !verify())
                throw new InvalidOperationException("고정 상태 또는 선택 항목이 바뀌었습니다.");
            Wire.Log("direct pin API begin: "+(pinned?"unpin":"pin"));
            if(pinned)await history.UnpinItemAsync(key).AsTask();
            else await history.PinItemAsync(key).AsTask();
            // Confirm via the data API, never by invoking or moving a UI control.
            var after=await ActionRules.Within(history.GetItemsAsync().AsTask(),1200);
            var changed=after.FirstOrDefault(x=>x.Id==key);
            if(changed==null || changed.IsPinned==pinned)
                throw new InvalidOperationException("Windows의 고정 변경 결과를 확인하지 못했습니다.");
            return changed.IsPinned?"항목을 고정했습니다.":"고정을 해제했습니다.";
        }
    }
}
