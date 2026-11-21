using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;
using Clipboard=Windows.ApplicationModel.DataTransfer.Clipboard;
namespace ClipboardExtensionClean
{
    internal static class StaClipboard
    {
        private static readonly Control dispatcher;
        static StaClipboard()
        {
            Control created=null;Exception failure=null;
            using(var ready=new ManualResetEvent(false))
            {
                var thread=new Thread(()=> {
                    try
                    {
                        Application.OleRequired();
                        created=new Control();var handle=created.Handle;
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        ready.Set();Application.Run();
                    }
                    catch(Exception error){failure=error;ready.Set();}
                }) {IsBackground=true,Name="ClipPlus STA clipboard API"};
                thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne();
            }
            if(failure!=null)throw new InvalidOperationException("클립보드 API 스레드를 시작하지 못했습니다.",failure);
            dispatcher=created;
        }
        private static Task<T> Run<T>(Func<Task<T>> operation)
        {
            var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.BeginInvoke((Action)(async ()=> {
                try {result.TrySetResult(await operation());}
                catch(Exception error){result.TrySetException(error);}
            }));
            return result.Task;
        }
        internal static Task<string> HealthAsync()
        {
            return Run(async ()=> {
                var history=await ActionRules.Within(Clipboard.GetHistoryItemsAsync().AsTask(),1200);
                return Thread.CurrentThread.GetApartmentState()+" status="+history.Status+" count="+history.Items.Count;
            });
        }
        internal static Task<string> ClearAsync(Func<bool> verifyOnMta,Func<bool> finalFence)
        {
            return Run(async ()=> {
                if(!await Task.Run(verifyOnMta) || !finalFence())
                    throw new InvalidOperationException("모두 지우기 전에 입력 창이 바뀌었습니다.");
                if(!Clipboard.ClearHistory())throw new InvalidOperationException("Windows가 모두 지우기를 거부했습니다.");
                return "고정 항목을 제외한 이력을 모두 지웠습니다.";
            });
        }
        internal static Task<string> DeleteAsync(string id,Func<bool> verifyOnMta,Func<bool> finalFence)
        {
            return Run(async ()=> {
                var history=await ActionRules.Within(Clipboard.GetHistoryItemsAsync().AsTask(),1200);
                if(history.Status!=ClipboardHistoryItemsResultStatus.Success)
                    throw new InvalidOperationException("이력을 조회하지 못했습니다.");
                var item=history.Items.FirstOrDefault(x=>string.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));
                if(item==null)throw new InvalidOperationException("선택 항목이 이력에 없습니다.");
                // UIA verification stays on MTA; WinRT objects stay on this STA.
                if(!await Task.Run(verifyOnMta))return "고정 항목은 삭제하지 않습니다.";
                Guid key;
                if(!Guid.TryParse(id,out key))throw new InvalidOperationException("삭제 항목 ID가 올바르지 않습니다.");
                var nativeItems=await ActionRules.Within(WindowsUdk.ApplicationModel.DataTransfer.ClipboardHistory.Current.GetItemsAsync().AsTask(),1200);
                var nativeItem=nativeItems.FirstOrDefault(x=>x.Id==key);
                if(nativeItem==null)throw new InvalidOperationException("선택 항목이 이력에 없습니다.");
                if(nativeItem.IsPinned)return "고정 항목은 삭제하지 않습니다.";
                if(!finalFence())throw new InvalidOperationException("삭제 전에 입력 창이 바뀌었습니다.");
                if(!Clipboard.DeleteItemFromHistory(item))throw new InvalidOperationException("Windows가 삭제를 거부했습니다.");
                return "선택 항목을 삭제했습니다.";
            });
        }
    }
}
