using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Threading;

namespace ClipboardExtensionClean
{
    internal sealed class ContextFence
    {
        private long value=1;
        internal long Value {get{return Interlocked.Read(ref value);}}
        internal void Advance(long next)
        {
            long current;
            do {current=Value;if(next<=current)return;}
            while(Interlocked.CompareExchange(ref value,next,current)!=current);
        }
    }

    public sealed class Packet
    {
        public string Kind, Operation, Id, Pin, Error, Request;
        public long Root, Owner, Stamp, Epoch;
        public bool Visible, Focused;
    }

    internal static class Wire
    {
        internal static void LogAsync(string message)
        {
            string captured="mono="+ActionRules.Now+" "+message;
            ThreadPool.QueueUserWorkItem(_=>Log(captured));
        }
        public static string Encode(Packet packet) { return new JavaScriptSerializer().Serialize(packet); }
        public static Packet Decode(string line) { return new JavaScriptSerializer().Deserialize<Packet>(line); }
        public static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipPlus");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "activity.log");
                lock (typeof(Wire))
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 256000)
                    {
                        File.Copy(path, path + ".previous", true);
                        File.WriteAllText(path, "");
                    }
                    File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
