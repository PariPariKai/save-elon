// Tiny software mixer on top of waveOut: looping music with crossfades plus pitched, panned one-shot voices.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

class Mixer
{
    [StructLayout(LayoutKind.Sequential)] struct WFX { public ushort tag, ch; public uint rate, bps; public ushort align, bits, cb; }
    [StructLayout(LayoutKind.Sequential)] struct WHDR { public IntPtr data; public uint len, rec; public IntPtr user; public uint flags, loops; public IntPtr next, res; }
    [DllImport("winmm")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WFX f, IntPtr cb, IntPtr inst, uint fl);
    [DllImport("winmm")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int sz);
    [DllImport("winmm")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int sz);
    [DllImport("winmm")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm")] static extern int waveOutClose(IntPtr h);

    const int SR = Syn.SR, N = 512, NB = 8;
    IntPtr wo;
    readonly IntPtr[] hdr = new IntPtr[NB], buf = new IntPtr[NB];
    int hsz, flagOff;
    Thread th;
    volatile bool run;

    class Voice { public float[] s; public double pos; public float rate, gl, gr, fade = 1; public bool stopping; }
    readonly List<Voice> voices = new List<Voice>();
    short[] musA, musB;
    int posA, posB;
    bool loopA, loopB;
    float xf = 1, xfStep, musGain = 0;
    public volatile bool MusicOn = true;
    public float MusicVol = 0.45f;

    public static Mixer TryStart()
    {
        try { var m = new Mixer(); return m.Start() ? m : null; }
        catch { return null; }
    }

    bool Start()
    {
        var f = new WFX { tag = 1, ch = 2, rate = SR, bps = SR * 4, align = 4, bits = 16, cb = 0 };
        if (waveOutOpen(out wo, 0xFFFFFFFF, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) return false;
        hsz = Marshal.SizeOf(typeof(WHDR));
        flagOff = (int)Marshal.OffsetOf(typeof(WHDR), "flags");
        for (int i = 0; i < NB; i++)
        {
            buf[i] = Marshal.AllocHGlobal(N * 4);
            hdr[i] = Marshal.AllocHGlobal(hsz);
            Marshal.StructureToPtr(new WHDR { data = buf[i], len = N * 4 }, hdr[i], false);
            waveOutPrepareHeader(wo, hdr[i], hsz);
        }
        run = true;
        th = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        th.Start();
        return true;
    }

    public void Stop()
    {
        run = false;
        if (th != null) th.Join(300);
        waveOutReset(wo);
        waveOutClose(wo);
    }

    public void Music(short[] track, bool loop, float fadeSec)
    {
        lock (voices)
        {
            if (track == musA) return;
            musB = musA; posB = posA; loopB = loopA;
            musA = track; posA = 0; loopA = loop;
            xf = 0; xfStep = 1f / Math.Max(1, fadeSec * SR);
        }
    }

    // returns a handle that StopVoice accepts
    public object Play(float[] s, float vol, float pan, float rate)
    {
        if (s == null || vol <= 0.001f) return null;
        pan = Math.Max(-1, Math.Min(1, pan));
        var v = new Voice { s = s, rate = rate, gl = vol * (float)Math.Sqrt((1 - pan) * 0.5), gr = vol * (float)Math.Sqrt((1 + pan) * 0.5) };
        lock (voices) { if (voices.Count < 48) voices.Add(v); else return null; }
        return v;
    }

    // fades a playing sound out over ~15 ms (a hard cut would click)
    public void StopVoice(object h)
    {
        var v = h as Voice;
        if (v != null) lock (voices) v.stopping = true;
    }

    void Loop()
    {
        var tmp = new short[N * 2];
        for (int i = 0; i < NB; i++) Submit(i, tmp);
        int next = 0;
        while (run)
        {
            // buffers finish in the order they were queued, so refill them in the same order
            while (run && (Marshal.ReadInt32(hdr[next], flagOff) & 1) != 0) { Submit(next, tmp); next = (next + 1) % NB; }
            Thread.Sleep(2);
        }
    }

    static float Mus(short[] m, ref int pos, bool loop, out float r)
    {
        r = 0;
        if (m == null) return 0;
        if (pos >= m.Length) { if (!loop) return 0; pos = 0; }
        float l = m[pos] / 32768f; r = m[pos + 1] / 32768f;
        pos += 2;
        return l;
    }

    void Submit(int bi, short[] o)
    {
        lock (voices)
        {
            float target = MusicOn ? MusicVol : 0;
            for (int k = 0; k < N; k++)
            {
                musGain += (target - musGain) * 0.0004f;
                float ar, br;
                float al = Mus(musA, ref posA, loopA, out ar);
                float bl = Mus(musB, ref posB, loopB, out br);
                float l = (al * xf + bl * (1 - xf)) * musGain, r = (ar * xf + br * (1 - xf)) * musGain;
                if (xf < 1) { xf = Math.Min(1, xf + xfStep); if (xf >= 1) musB = null; }
                for (int v = voices.Count - 1; v >= 0; v--)
                {
                    var vo = voices[v];
                    int i = (int)vo.pos;
                    if (i + 1 >= vo.s.Length) { voices.RemoveAt(v); continue; }
                    if (vo.stopping) { vo.fade -= 1f / (0.015f * SR); if (vo.fade <= 0) { voices.RemoveAt(v); continue; } }
                    float fr = (float)(vo.pos - i);
                    float s = (vo.s[i] + (vo.s[i + 1] - vo.s[i]) * fr) * vo.fade;
                    l += s * vo.gl; r += s * vo.gr;
                    vo.pos += vo.rate;
                }
                l = l / (1 + Math.Abs(l) * 0.25f);
                r = r / (1 + Math.Abs(r) * 0.25f);
                o[2 * k] = (short)(Math.Max(-1f, Math.Min(1f, l)) * 32000);
                o[2 * k + 1] = (short)(Math.Max(-1f, Math.Min(1f, r)) * 32000);
            }
        }
        Marshal.Copy(o, 0, buf[bi], N * 2);
        waveOutWrite(wo, hdr[bi], hsz);
    }
}
