// Tiny loader in the spirit of kkrunchy: the real game is stored gzip-compressed inside this exe
// and is unpacked into memory at startup (nothing is written to disk).
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

static class Stub
{
    [STAThread]
    static int Main(string[] args)
    {
        byte[] raw;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("game"))
        using (var z = new GZipStream(s, CompressionMode.Decompress))
        using (var m = new MemoryStream())
        {
            z.CopyTo(m);
            raw = m.ToArray();
        }
        object r = Assembly.Load(raw).EntryPoint.Invoke(null, new object[] { args });
        return r is int ? (int)r : 0;
    }
}
