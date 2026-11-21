using System;
using ClipboardExtensionClean;
class GestureTests {
 static int Main(){int n=0;Action<bool> c=x=>{if(!x)throw new Exception("gesture "+n);n++;};var g=new RefreshGesture();
 c(!g.Observe(86,true,false,true,100));c(!g.Observe(86,true,false,true,120));g.Observe(86,false,true,true,130);c(g.Observe(86,true,false,true,300));g.Observe(86,false,true,true,310);c(!g.Observe(86,true,false,true,400));g.Observe(86,false,true,true,410);c(!g.Observe(86,true,false,true,1600));g.Observe(86,false,true,true,1610);g.Observe(65,true,false,false,1700);c(!g.Observe(86,true,false,true,1800));g.Observe(86,false,true,true,1810);c(!g.Observe(86,true,false,false,1900));g.Observe(86,false,true,false,1910);c(!g.Observe(86,true,false,true,2000));Console.WriteLine(n+" gesture checks passed; no real key input");return 0;}
}
