namespace ClipboardExtensionClean
{
    internal static class ClipboardInputScope
    {
        internal static bool Owns(Packet latest,Packet bound,long epoch,long now,bool busy,long foreground,bool visible)
        {
            return latest!=null && bound!=null && bound.Root!=0 && bound.Owner!=0
                && bound.Visible && bound.Focused && latest.Stamp>=0 && now>=latest.Stamp
                && latest.Epoch==epoch && bound.Epoch==epoch && latest.Visible
                && (latest.Owner==0 || latest.Owner==bound.Owner)
                && latest.Root==bound.Root && foreground==bound.Owner && visible
                && (ActionRules.Fresh(latest.Stamp,now,1500) || busy);
        }
    }
}
