using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;using Clipboard=Windows.ApplicationModel.DataTransfer.Clipboard;
using ClipboardExtensionClean;
class DirectBackendSmoke {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber(); static uint ownedSequence; static int code=1;static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 [STAThread]static int Main(){Application.OleRequired();var d=new Control();var handle=d.Handle;SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());d.BeginInvoke((Action)(async()=>{try{await Run();code=0;}catch(Exception e){Console.WriteLine("FAIL "+e.GetType().Name+": "+e.Message);}finally{Application.ExitThread();}}));Application.Run();d.Dispose();return code;}
 static async Task Run(){
  var before=await Clipboard.GetHistoryItemsAsync().AsTask();Check(before.Status==ClipboardHistoryItemsResultStatus.Success,"history readable");Check(before.Items.Count<20,"history capacity guard");
  var ids=new HashSet<string>(before.Items.Select(x=>x.Id),StringComparer.OrdinalIgnoreCase);
  var current=Clipboard.GetContent();var restore=new DataPackage();var formats=current.AvailableFormats.ToArray();
  foreach(var f in formats)restore.SetData(f,await current.GetDataAsync(f).AsTask());
  Console.WriteLine("Original clipboard format count="+formats.Length); string originalText=current.Contains(StandardDataFormats.Text)?await current.GetTextAsync().AsTask():null;
  string marker="ClipPlus isolated backend verification "+Guid.NewGuid().ToString("N");string fixture=null;
  var test=new DataPackage();test.SetText(marker);
  try{
   Check(Clipboard.SetContentWithOptions(test,new ClipboardContentOptions{IsAllowedInHistory=true,IsRoamable=false}),"unique fixture submitted");
   var deadline=DateTime.UtcNow.AddSeconds(4);
   while(fixture==null&&DateTime.UtcNow<deadline){await Task.Delay(60);var list=await Clipboard.GetHistoryItemsAsync().AsTask();foreach(var item in list.Items){if(ids.Contains(item.Id)||!item.Content.Contains(StandardDataFormats.Text))continue;if(await item.Content.GetTextAsync().AsTask()==marker){fixture=item.Id;break;}}}
   Check(fixture!=null,"unique fixture found without touching existing entries");
   string pinned=await DirectPin.ToggleAsync(fixture,PinState.Unpinned,()=>true);Check(pinned=="항목을 고정했습니다.","direct pin and native readback");
   string blocked=await StaClipboard.DeleteAsync(fixture,()=>true,()=>true);Check(blocked=="고정 항목은 삭제하지 않습니다.","native pinned state blocks real deletion");
   string unpinned=await DirectPin.ToggleAsync(fixture,PinState.Pinned,()=>true);Check(unpinned=="고정을 해제했습니다.","direct unpin and native readback");
   string deleted=await StaClipboard.DeleteAsync(fixture,()=>true,()=>true);ownedSequence=GetClipboardSequenceNumber(); Check(deleted=="선택 항목을 삭제했습니다.","STA deletes only unpinned fixture");
   var after=await Clipboard.GetHistoryItemsAsync().AsTask();Check(!after.Items.Any(x=>x.Id.Equals(fixture,StringComparison.OrdinalIgnoreCase)),"fixture absent after delete");Check(ids.SetEquals(after.Items.Select(x=>x.Id)),"all original history identities preserved");
  }finally{
   if(fixture!=null){var remaining=await WindowsUdk.ApplicationModel.DataTransfer.ClipboardHistory.Current.GetItemsAsync().AsTask();var entry=remaining.FirstOrDefault(x=>x.Id==Guid.Parse(fixture));if(entry!=null){if(entry.IsPinned)await WindowsUdk.ApplicationModel.DataTransfer.ClipboardHistory.Current.UnpinItemAsync(entry.Id).AsTask();await StaClipboard.DeleteAsync(fixture,()=>true,()=>true);ownedSequence=GetClipboardSequenceNumber();}}
   var now=Clipboard.GetContent();if((now.Contains(StandardDataFormats.Text)&&await now.GetTextAsync().AsTask()==marker) || (now.AvailableFormats.Count==0&&ownedSequence!=0&&ownedSequence==GetClipboardSequenceNumber())){Check(Clipboard.SetContentWithOptions(restore,new ClipboardContentOptions{IsAllowedInHistory=false,IsRoamable=false}),"original clipboard restored without history duplication");var restored=Clipboard.GetContent();Check(formats.All(f=>restored.AvailableFormats.Contains(f)),"original clipboard formats preserved");if(originalText!=null)Check(await restored.GetTextAsync().AsTask()==originalText,"original text preserved exactly");}
   else Console.WriteLine("Current clipboard changed externally; newer data left untouched.");
  }
  Console.WriteLine("ALL PASS "+checks+" real background backend checks; original history preserved; no foreground input.");
 }
}
