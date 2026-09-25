// Every sound in the game is computed here at startup: no samples are stored in the exe.
using System;
using System.Threading;
using System.Threading.Tasks;

class Rng
{
    uint s;
    public Rng(uint seed) { s = seed * 2654435761u + 1; }
    public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 8388608f - 1f; }   // -1..1
    public float Unit() { return Next() * 0.5f + 0.5f; }
}

// Chamberlin state-variable filter
class Svf
{
    double low, band;
    public double Low, Band, High;
    public void Run(double x, double fc, double q)
    {
        double f = 2 * Math.Sin(Math.PI * Math.Min(fc, 6000.0) / Syn.SR);
        low += f * band;
        double high = x - low - q * band;
        band += f * high;
        Low = low; Band = band; High = high;
    }
}

class Bus
{
    public readonly float[] L, R;
    public readonly int Len;
    public readonly bool Wrap;
    public Bus(int len, bool wrap) { L = new float[len]; R = new float[len]; Len = len; Wrap = wrap; }
    public void Add(int i, double l, double r)
    {
        if (Wrap) { i %= Len; if (i < 0) i += Len; }
        else if (i < 0 || i >= Len) return;
        L[i] += (float)l; R[i] += (float)r;
    }
}

static class Syn
{
    public const int SR = 44100;
    const double TAU = Math.PI * 2;

    public static double Mtof(double m) { return 440.0 * Math.Pow(2, (m - 69) / 12.0); }

    static double Blep(double t, double dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }
    static double Saw(double ph, double dt) { return 2 * ph - 1 - Blep(ph, dt); }
    static double Sq(double ph, double dt)
    {
        double v = ph < 0.5 ? 1 : -1;
        v += Blep(ph, dt);
        double p2 = ph + 0.5; if (p2 >= 1) p2 -= 1;
        return v - Blep(p2, dt);
    }
    static double Wrap1(double ph) { return ph >= 1 ? ph - 1 : ph; }
    static int S(double sec) { return (int)(sec * SR); }

    // ---------------------------------------------------------------- instruments
    public static void Kick(Bus b, double t0, double vol)
    {
        int s0 = S(t0); double ph = 0;
        var r = new Rng((uint)s0);
        for (int i = 0; i < S(0.5); i++)
        {
            double t = (double)i / SR;
            ph += TAU * (42 + 120 * Math.Exp(-t * 28)) / SR;
            double v = (Math.Sin(ph) * Math.Exp(-t * 6.5) + r.Next() * Math.Exp(-t * 400) * 0.25) * vol;
            b.Add(s0 + i, v, v);
        }
    }

    public static void Snare(Bus dry, Bus wet, double t0, double vol)
    {
        int s0 = S(t0); double lp = 0;
        var r = new Rng((uint)s0 + 7);
        for (int i = 0; i < S(0.35); i++)
        {
            double t = (double)i / SR;
            double n = r.Next(); lp += (n - lp) * 0.3;
            double v = (Math.Sin(TAU * 185 * t) * Math.Exp(-t * 25) * 0.55 + (n - lp) * Math.Exp(-t * 13)) * vol;
            dry.Add(s0 + i, v, v);
            wet.Add(s0 + i, v * 0.45, v * 0.45);
        }
    }

    public static void Hat(Bus b, double t0, double vol, bool open, double pan)
    {
        int s0 = S(t0); double lp = 0;
        var r = new Rng((uint)s0 + 3);
        double dur = open ? 0.3 : 0.06, k = open ? 13 : 70;
        for (int i = 0; i < S(dur); i++)
        {
            double t = (double)i / SR;
            double n = r.Next(); lp += (n - lp) * 0.55;
            double v = (n - lp) * Math.Exp(-t * k) * vol;
            b.Add(s0 + i, v * (1 - pan) * 0.5 * 1.4, v * (1 + pan) * 0.5 * 1.4);
        }
    }

    public static void Clang(Bus dry, Bus wet, double t0, double vol, double f, double pan)
    {
        int s0 = S(t0);
        for (int i = 0; i < S(0.7); i++)
        {
            double t = (double)i / SR;
            double idx = 5 * Math.Exp(-t * 9);
            double v = Math.Sin(TAU * f * t + idx * Math.Sin(TAU * f * 1.414 * t)) * Math.Exp(-t * 7) * vol;
            dry.Add(s0 + i, v * (1 - pan) * 0.5, v * (1 + pan) * 0.5);
            wet.Add(s0 + i, v * 0.3, v * 0.3);
        }
    }

    public static void Bass(Bus b, double t0, double len, int midi, double vol, double bright)
    {
        int s0 = S(t0);
        double f = Mtof(midi), dt = f / SR, ph = 0;
        var svf = new Svf();
        int n = S(len + 0.06);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double env = Math.Min(1, t / 0.004) * (t < len ? 1 : Math.Exp(-(t - len) * 70));
            double osc = Saw(ph, dt) * 0.7 + Sq(ph, dt) * 0.3;
            svf.Run(osc, 90 + bright * (350 + 1700 * Math.Exp(-t * 9)), 0.35);
            double v = (svf.Low + 0.35 * Math.Sin(TAU * ph)) * env * vol;
            ph = Wrap1(ph + dt);
            b.Add(s0 + i, v, v);
        }
    }

    // two detuned saws per note, one per ear; "choir" swaps the lowpass for two vowel formants
    public static void Pad(Bus dry, Bus wet, double t0, double len, int[] notes, double vol, bool choir)
    {
        int s0 = S(t0);
        int n = S(len + 1.3);
        foreach (int m in notes)
        {
            double f = Mtof(m), dtl = f * Math.Pow(2, -8 / 1200.0) / SR, dtr = f * Math.Pow(2, 8 / 1200.0) / SR;
            double pl = 0.13 * (m % 5), pr = 0.71 * (m % 3) % 1;
            var fl = new Svf(); var fr = new Svf(); var fl2 = new Svf(); var fr2 = new Svf();
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR;
                double env = Math.Min(1, t / 0.5) * (t < len ? 1 : Math.Exp(-(t - len) * 3.5));
                double a = Saw(pl, dtl), c = Saw(pr, dtr);
                pl = Wrap1(pl + dtl); pr = Wrap1(pr + dtr);
                double l, r;
                if (choir)
                {
                    fl.Run(a, 650, 0.25); fl2.Run(a, 1080, 0.3); fr.Run(c, 650, 0.25); fr2.Run(c, 1080, 0.3);
                    l = fl.Band * 0.8 + fl2.Band * 0.5; r = fr.Band * 0.8 + fr2.Band * 0.5;
                }
                else
                {
                    double fc = 750 + 300 * Math.Sin(TAU * 0.15 * (t0 + t));
                    fl.Run(a, fc, 0.6); fr.Run(c, fc, 0.6);
                    l = fl.Low; r = fr.Low;
                }
                double g = env * vol / notes.Length;
                dry.Add(s0 + i, l * g * 0.7, r * g * 0.7);
                wet.Add(s0 + i, l * g * 0.5, r * g * 0.5);
            }
        }
    }

    public static void Pluck(Bus b, Bus wet, double t0, int midi, double vol, double pan, double decay)
    {
        int s0 = S(t0);
        double f = Mtof(midi), dt = f / SR, ph = 0;
        var svf = new Svf();
        for (int i = 0; i < S(0.6); i++)
        {
            double t = (double)i / SR;
            double osc = Sq(ph, dt) * 0.6 + Saw(ph, dt) * 0.4;
            ph = Wrap1(ph + dt);
            svf.Run(osc, 300 + 4000 * Math.Exp(-t * 16), 0.5);
            double v = svf.Low * Math.Exp(-t * decay) * Math.Min(1, t / 0.002) * vol;
            b.Add(s0 + i, v * (1 - pan) * 0.5, v * (1 + pan) * 0.5);
            wet.Add(s0 + i, v * 0.25, v * 0.25);
        }
    }

    public static void Bell(Bus b, Bus wet, double t0, int midi, double vol, double pan)
    {
        int s0 = S(t0);
        double f = Mtof(midi);
        for (int i = 0; i < S(2.2); i++)
        {
            double t = (double)i / SR;
            double v = Math.Sin(TAU * f * t + 2.5 * Math.Exp(-t * 2.5) * Math.Sin(TAU * f * 3.5 * t)) * Math.Exp(-t * 2.2) * Math.Min(1, t / 0.002) * vol;
            b.Add(s0 + i, v * (1 - pan) * 0.5, v * (1 + pan) * 0.5);
            wet.Add(s0 + i, v * 0.5, v * 0.5);
        }
    }

    public static void Lead(Bus b, Bus wet, double t0, double len, int midi, double vol)
    {
        int s0 = S(t0);
        double f = Mtof(midi);
        double p0 = 0, p1 = 0.3, p2 = 0.6;
        var fl = new Svf(); var fr = new Svf();
        int n = S(len + 0.1);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double vib = t > 0.15 ? Math.Pow(2, 0.25 / 12 * Math.Sin(TAU * 5.5 * t)) : 1;
            double d0 = f * vib / SR, d1 = d0 * 1.006, d2 = d0 * 0.994;
            double a = Saw(p0, d0), bb = Saw(p1, d1), c = Saw(p2, d2);
            p0 = Wrap1(p0 + d0); p1 = Wrap1(p1 + d1); p2 = Wrap1(p2 + d2);
            double env = Math.Min(1, t / 0.005) * (t < len ? 1 : Math.Exp(-(t - len) * 40));
            double fc = 1100 + 1900 * Math.Exp(-t * 6);
            fl.Run(a + bb * 0.6, fc, 0.5); fr.Run(a + c * 0.6, fc, 0.5);
            b.Add(s0 + i, fl.Low * env * vol, fr.Low * env * vol);
            wet.Add(s0 + i, fl.Low * env * vol * 0.3, fr.Low * env * vol * 0.3);
        }
    }

    // ---------------------------------------------------------------- effects
    // ping-pong echo; a looping bus is processed twice so the tail wraps seamlessly
    static void Echo(Bus src, Bus dst, int delay, double fb, double mix)
    {
        var bl = new float[delay]; var br = new float[delay];
        int w = 0;
        for (int pass = 0; pass < (src.Wrap ? 2 : 1); pass++)
            for (int i = 0; i < src.Len; i++)
            {
                float dl = bl[w], dr = br[w];
                bl[w] = (float)(src.L[i] * 0.7 + src.R[i] * 0.3 + dr * fb);
                br[w] = (float)(dl * fb);
                if (++w == delay) w = 0;
                if (pass == (src.Wrap ? 1 : 0)) { dst.L[i] += (float)(dl * mix); dst.R[i] += (float)(dr * mix); }
            }
    }

    static void Reverb(Bus src, Bus dst, double gain)
    {
        int[] cl = { 1116, 1188, 1277, 1356 };
        int[] al = { 556, 441 };
        for (int ch = 0; ch < 2; ch++)
        {
            float[] x = ch == 0 ? src.L : src.R, y = ch == 0 ? dst.L : dst.R;
            int spread = ch * 23;
            var combs = new float[4][]; var ci = new int[4]; var filt = new double[4];
            for (int k = 0; k < 4; k++) combs[k] = new float[cl[k] + spread];
            var aps = new float[2][]; var ai = new int[2];
            for (int k = 0; k < 2; k++) aps[k] = new float[al[k] + spread];
            for (int pass = 0; pass < (src.Wrap ? 2 : 1); pass++)
                for (int i = 0; i < src.Len; i++)
                {
                    double input = x[i] * 0.3, o = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        float[] c = combs[k];
                        double v = c[ci[k]];
                        filt[k] = v * 0.75 + filt[k] * 0.25;
                        c[ci[k]] = (float)(input + filt[k] * 0.84);
                        if (++ci[k] == c.Length) ci[k] = 0;
                        o += v;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float[] a = aps[k];
                        double bv = a[ai[k]];
                        a[ai[k]] = (float)(o + bv * 0.5);
                        o = bv - o;
                        if (++ai[k] == a.Length) ai[k] = 0;
                    }
                    if (pass == (src.Wrap ? 1 : 0)) y[i] += (float)(o * gain);
                }
        }
    }

    static short[] Master(Bus dry, Bus echo, Bus wet, int echoDelay, double echoFb, double revGain)
    {
        var mix = new Bus(dry.Len, dry.Wrap);
        Array.Copy(dry.L, mix.L, dry.Len); Array.Copy(dry.R, mix.R, dry.Len);
        Echo(echo, mix, echoDelay, echoFb, 0.55);
        for (int i = 0; i < dry.Len; i++) { mix.L[i] += echo.L[i]; mix.R[i] += echo.R[i]; }
        Reverb(wet, mix, revGain);
        double peak = 1e-6;
        for (int i = 0; i < mix.Len; i++) peak = Math.Max(peak, Math.Max(Math.Abs(mix.L[i]), Math.Abs(mix.R[i])));
        double g = 1.4 / peak;
        var o = new short[mix.Len * 2];
        for (int i = 0; i < mix.Len; i++)
        {
            o[2 * i] = (short)(Math.Tanh(mix.L[i] * g) * 0.85 * 32767);
            o[2 * i + 1] = (short)(Math.Tanh(mix.R[i] * g) * 0.85 * 32767);
        }
        return o;
    }

    static int[] Chord(int root, bool minor) { return new[] { root, root + (minor ? 3 : 4), root + 7 }; }

    // ---------------------------------------------------------------- music
    // 0 catacombs, 1 foundry, 2 sanctum, 3 boss, 4 victory (not looped)
    public static short[] Track(int id)
    {
        double bpm = new[] { 92.0, 122, 76, 148, 104 }[id];
        int bars = new[] { 16, 16, 12, 16, 5 }[id];
        double beat = 60 / bpm, bar = beat * 4, s16 = beat / 4;
        bool loop = id != 4;
        int len = S(bars * bar + (loop ? 0 : 3.5));
        var dry = new Bus(len, loop); var echo = new Bus(len, loop); var wet = new Bus(len, loop);
        var r = new Rng((uint)(id * 97 + 5));
        int[] roots; bool[] minor;
        switch (id)
        {
            case 0: roots = new[] { 57, 57, 53, 53, 50, 50, 52, 52 }; minor = new[] { true, true, false, false, true, true, false, false }; break;
            case 1: roots = new[] { 50, 50, 46, 48, 50, 50, 55, 45 }; minor = new[] { true, true, false, false, true, true, true, false }; break;
            case 2: roots = new[] { 52, 53, 52, 50, 52, 53, 55, 53, 52, 53, 50, 52 }; minor = new[] { true, false, true, true, true, false, false, false, true, false, true, true }; break;
            case 3: roots = new[] { 48, 48, 44, 46, 48, 48, 53, 55 }; minor = new[] { true, true, false, false, true, true, true, false }; break;
            default: roots = new[] { 60, 57, 53, 55, 60 }; minor = new[] { false, true, false, false, false }; break;
        }
        for (int b = 0; b < bars; b++)
        {
            int ci = b % roots.Length;
            int root = roots[ci];
            var ch = Chord(root, minor[ci]);
            double t0 = b * bar;
            bool second = b >= bars / 2;
            if (id == 0)
            {
                Pad(dry, wet, t0, bar, ch, 0.5, false);
                int[] bp = { 0, -1, 0, 12, -1, 0, 7, -1 };
                for (int k = 0; k < 8; k++) if (bp[k] >= 0) Bass(dry, t0 + k * beat / 2, beat * 0.4, root - 12 + bp[k], 0.55, 0.6);
                Kick(dry, t0, 0.9); Kick(dry, t0 + beat * 2.5, 0.7);
                Snare(dry, wet, t0 + beat * 2, 0.5);
                if (b >= 2) for (int k = 0; k < 8; k++) Hat(dry, t0 + k * beat / 2, k % 2 == 0 ? 0.12 : 0.2, false, 0.3);
                int[] arp = { 0, 1, 2, 3, 2, 1, 0, 2 };
                if (b >= 4)
                    for (int k = 0; k < 16; k++)
                    {
                        int a = arp[k % 8];
                        int note = (a == 3 ? root + 12 : ch[a]) + (second ? 24 : 12);
                        Pluck(echo, wet, t0 + k * s16, note, 0.11, (k % 2) * 0.8 - 0.4, 9);
                    }
            }
            else if (id == 1)
            {
                Pad(dry, wet, t0, bar, new[] { ch[0] - 12, ch[1], ch[2] }, 0.35, false);
                for (int k = 0; k < 16; k++) Bass(dry, t0 + k * s16, s16 * 0.6, root - 12 + (k % 4 == 2 ? 12 : 0), 0.45, 0.9);
                for (int k = 0; k < 4; k++) Kick(dry, t0 + k * beat, 1.0);
                Snare(dry, wet, t0 + beat, 0.6); Snare(dry, wet, t0 + beat * 3, 0.6);
                for (int k = 0; k < 4; k++) Hat(dry, t0 + k * beat + beat / 2, 0.22, true, -0.2);
                for (int k = 0; k < 16; k++) Hat(dry, t0 + k * s16, 0.08, false, 0.4);
                int[] cl = { 3, 7, 14 };
                foreach (int k in cl) Clang(dry, wet, t0 + k * s16, 0.12, k == 7 ? 520 : 380, k == 3 ? -0.6 : 0.6);
                if (second) for (int k = 0; k < 8; k += 3) Pluck(echo, wet, t0 + k * beat / 2, ch[k % 3] + 24, 0.12, 0.3, 7);
            }
            else if (id == 2)
            {
                Pad(dry, wet, t0, bar, new[] { ch[0], ch[1], ch[2], ch[0] + 12 }, 0.6, true);
                Bass(dry, t0, bar * 0.95, root - 24, 0.35, 0.15);
                Kick(dry, t0, 0.8); Kick(dry, t0 + 0.28, 0.5);
                Kick(dry, t0 + beat * 2, 0.7); Kick(dry, t0 + beat * 2 + 0.28, 0.45);
                for (int k = 0; k < 8; k++)
                    if (r.Unit() < (second ? 0.6 : 0.4))
                        Bell(echo, wet, t0 + k * beat / 2, ch[(int)(r.Unit() * 2.99f)] + 24, 0.09, r.Next() * 0.7);
            }
            else if (id == 3)
            {
                Pad(dry, wet, t0, bar, ch, 0.35, false);
                for (int k = 0; k < 16; k++) Bass(dry, t0 + k * s16, s16 * 0.55, root - 12 + (k % 2 == 1 ? 12 : 0), 0.5, 1.0);
                for (int k = 0; k < 4; k++) Kick(dry, t0 + k * beat, 1.0);
                Kick(dry, t0 + beat * 2.75, 0.6);
                Snare(dry, wet, t0 + beat, 0.7); Snare(dry, wet, t0 + beat * 3, 0.7);
                for (int k = 0; k < 16; k++) Hat(dry, t0 + k * s16, k % 4 == 2 ? 0.2 : 0.09, k % 4 == 2, 0.3);
                int[] riff = { 0, 2, 1, 0, 2, 1, 3, 2 };
                var tones = new[] { ch[0], ch[1], ch[2], ch[0] + 12 };
                for (int k = 0; k < 8; k++)
                    Lead(dry, wet, t0 + k * beat / 2, beat * 0.4, tones[riff[(k + (second ? 3 : 0)) % 8]] + 12, 0.16);
            }
            else
            {
                bool last = b == bars - 1;
                Pad(dry, wet, t0, last ? bar * 1.5 : bar, new[] { ch[0], ch[1], ch[2], ch[0] + 12 }, 0.5, false);
                Bass(dry, t0, last ? bar : bar * 0.9, root - 24, 0.4, 0.3);
                Kick(dry, t0, 0.5);
                for (int k = 0; k < (last ? 4 : 8); k++)
                    Bell(echo, wet, t0 + k * beat / 2, (k % 4 == 3 ? ch[0] + 12 : ch[k % 3]) + 12 + (k >= 4 ? 12 : 0), 0.13, k % 2 == 0 ? -0.4 : 0.4);
            }
        }
        return Master(dry, echo, wet, S(beat * 0.75), 0.4, id == 2 ? 0.5 : 0.3);
    }

    // ---------------------------------------------------------------- sound effects (mono)
    static float[] Norm(float[] o, double peak)
    {
        double m = 1e-6;
        foreach (var v in o) m = Math.Max(m, Math.Abs(v));
        for (int i = 0; i < o.Length; i++) o[i] = (float)(o[i] / m * peak);
        return o;
    }

    public static float[] Shot()
    {
        var o = new float[S(0.3)]; var r = new Rng(11); var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph = Wrap1(ph + (180 + 1400 * Math.Exp(-t * 30)) / SR);
            double v = (ph < 0.5 ? 0.45 : -0.45) * Math.Exp(-t * 18) + r.Next() * 0.5 * Math.Exp(-t * 50) + Math.Sin(TAU * 70 * t) * Math.Exp(-t * 25) * 0.6;
            f.Run(v, 400 + 5000 * Math.Exp(-t * 8), 0.7);
            o[i] = (float)f.Low;
        }
        int d = S(0.07);
        for (int i = o.Length - 1; i >= d; i--) o[i] += o[i - d] * 0.25f;
        return Norm(o, 0.8);
    }

    public static float[] EnemyShot()
    {
        var o = new float[S(0.3)]; var r = new Rng(12); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph += TAU * (900 + 1500 * Math.Exp(-t * 20)) / SR;
            o[i] = (float)(Math.Sin(ph + 2 * Math.Sin(ph * 2.01)) * Math.Exp(-t * 10) * 0.7 + r.Next() * 0.15 * Math.Exp(-t * 60));
        }
        return Norm(o, 0.7);
    }

    public static float[] HeavyShot()
    {
        var o = new float[S(0.55)]; var r = new Rng(13); var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph = Wrap1(ph + (60 + 300 * Math.Exp(-t * 12)) / SR);
            f.Run((ph < 0.5 ? 1 : -1) * 0.7 + r.Next() * 0.4, 200 + 2500 * Math.Exp(-t * 10), 0.6);
            o[i] = (float)(f.Low * Math.Exp(-t * 6));
        }
        return Norm(o, 0.8);
    }

    public static float[] Explode(bool big)
    {
        double dur = big ? 3.2 : 1.3;
        var o = new float[S(dur)];
        foreach (double at in big ? new[] { 0.0, 0.45, 1.0 } : new[] { 0.0 })
        {
            var r = new Rng((uint)(at * 1000 + (big ? 21 : 14))); var f = new Svf(); double ph = 0;
            for (int i = S(at); i < o.Length; i++)
            {
                double t = (double)(i - S(at)) / SR;
                f.Run(r.Next(), (big ? 1800 : 3000) * Math.Exp(-t * (big ? 1.5 : 3)) + 120, 0.8);
                ph += TAU * ((big ? 38 : 50) + 40 * Math.Exp(-t * 8)) / SR;
                double crack = r.Unit() < 0.002 * Math.Exp(-t * 2) ? r.Next() * 2 : 0;
                double v = (f.Low * 1.6 + Math.Sin(ph) * Math.Exp(-t * (big ? 1.2 : 4)) + crack) * Math.Exp(-t * (big ? 1.3 : 3.2));
                o[i] += (float)(v * (at == 0 ? 1 : 0.7));
            }
        }
        return Norm(o, 0.9);
    }

    public static float[] Hurt()
    {
        var o = new float[S(0.4)]; var r = new Rng(15); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 900, 0.8);
            o[i] = (float)((Math.Tanh(Math.Sin(TAU * 110 * t) * 3) * 0.6 + f.Low * 0.5) * Math.Exp(-t * 9));
        }
        return Norm(o, 0.8);
    }

    public static float[] Pickup()
    {
        var o = new float[S(0.7)];
        double[] fr = { 1047, 1319, 1568, 2093 };
        for (int k = 0; k < 4; k++)
            for (int i = S(k * 0.065); i < o.Length; i++)
            {
                double t = (double)(i - S(k * 0.065)) / SR;
                o[i] += (float)((Math.Sin(TAU * fr[k] * t) + 0.3 * Math.Sin(TAU * fr[k] * 2 * t)) * Math.Exp(-t * 7) * 0.4);
            }
        return Norm(o, 0.6);
    }

    public static float[] Alert()
    {
        var o = new float[S(0.3)];
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double f = t < 0.12 ? 1200 : 900, lt = t < 0.12 ? t : t - 0.14;
            double env = lt < 0 || lt > 0.1 ? 0 : Math.Sin(Math.PI * lt / 0.1);
            o[i] = (float)(Math.Sign(Math.Sin(TAU * f * t)) * 0.3 * env);
        }
        return Norm(o, 0.5);
    }

    public static float[] Hit()
    {
        var o = new float[S(0.12)];
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            o[i] = (float)(Math.Sin(TAU * 1700 * t + 2 * Math.Exp(-t * 40) * Math.Sin(TAU * 4600 * t)) * Math.Exp(-t * 35));
        }
        return Norm(o, 0.5);
    }

    public static float[] Shield()
    {
        var o = new float[S(0.35)];
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            o[i] = (float)(Math.Sin(TAU * 2000 * t + 3 * Math.Exp(-t * 12) * Math.Sin(TAU * 5400 * t)) * Math.Exp(-t * 12));
        }
        return Norm(o, 0.5);
    }

    public static float[] PortalOpen()
    {
        var o = new float[S(2.4)]; var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double fr = 110 * Math.Pow(8, Math.Min(t / 1.4, 1));
            ph = Wrap1(ph + fr / SR);
            f.Run(Saw(ph, fr / SR), fr * 3, 0.3);
            o[i] = (float)(f.Band * 0.5 * Math.Min(1, t * 3) * Math.Exp(-Math.Max(0, t - 1.4) * 3));
        }
        int[] bells = { 84, 88, 91, 96 };
        for (int k = 0; k < 4; k++)
            for (int i = S(0.9 + k * 0.12); i < o.Length; i++)
            {
                double t = (double)(i - S(0.9 + k * 0.12)) / SR, fq = Mtof(bells[k]);
                o[i] += (float)(Math.Sin(TAU * fq * t + 2 * Math.Exp(-t * 3) * Math.Sin(TAU * fq * 3.5 * t)) * Math.Exp(-t * 2.5) * 0.25);
            }
        return Norm(o, 0.7);
    }

    public static float[] PortalEnter()
    {
        var o = new float[S(1.6)]; var r = new Rng(17); var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 300 * Math.Pow(20, t / 1.6), 0.4);
            ph += TAU * (200 * Math.Pow(6, t / 1.6)) / SR;
            double env = Math.Sin(Math.PI * Math.Min(1, t / 1.6));
            o[i] = (float)((f.Band * 0.8 + Math.Sin(ph) * 0.3) * env);
        }
        return Norm(o, 0.7);
    }

    public static float[] Step(int theme)
    {
        var o = new float[S(0.16)]; var r = new Rng((uint)(31 + theme)); var f = new Svf(); var lp = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR, v;
            if (theme == 0) { f.Run(r.Next(), 400, 0.9); v = f.Low * Math.Exp(-t * 40) + Math.Sin(TAU * 90 * t) * Math.Exp(-t * 35) * 0.6; }
            else if (theme == 1) { lp.Run(r.Next(), 3000, 0.9); v = Math.Sin(TAU * 320 * t + 3 * Math.Exp(-t * 30) * Math.Sin(TAU * 790 * t)) * Math.Exp(-t * 25) * 0.5 + lp.High * Math.Exp(-t * 80) * 0.3; }
            else { f.Run(r.Next(), 1200, 0.5); v = f.Band * Math.Exp(-t * 45) + Math.Sin(TAU * 140 * t) * Math.Exp(-t * 50) * 0.3; }
            o[i] = (float)v;
        }
        return Norm(o, 0.6);
    }

    public static float[] CageOpen()
    {
        var o = new float[S(2.6)]; var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double fr = 220 * Math.Pow(0.25, Math.Min(t / 2.0, 1));
            ph = Wrap1(ph + fr / SR);
            f.Run(Saw(ph, fr / SR), 900, 0.5);
            o[i] = (float)(f.Low * 0.5 * Math.Exp(-t * 0.8));
        }
        foreach (double at in new[] { 0.0, 0.35, 0.7 })
            for (int i = S(at); i < o.Length; i++)
            {
                double t = (double)(i - S(at)) / SR;
                o[i] += (float)(Math.Sin(TAU * 260 * t + 4 * Math.Exp(-t * 10) * Math.Sin(TAU * 367 * t)) * Math.Exp(-t * 6) * 0.4);
            }
        return Norm(o, 0.75);
    }

    public static float[] BossWake()
    {
        var o = new float[S(2.8)]; var r = new Rng(41); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double fc = 55 - 10 * t / 2.8;
            double idx = 6 * (0.5 + 0.5 * Math.Sin(t * 9));
            double growl = Math.Sin(TAU * fc * t + idx * Math.Sin(TAU * fc * 1.5 * t));
            f.Run(r.Next(), 300 + 900 * t / 2.8, 0.7);
            double env = Math.Min(1, t / 0.3) * Math.Exp(-Math.Max(0, t - 1.8) * 3);
            o[i] = (float)((growl * 0.7 + f.Low * 0.6) * env);
        }
        return Norm(o, 0.9);
    }

    public static float[] Death()
    {
        var o = new float[S(1.8)]; var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double fr = 300 * Math.Pow(0.13, t / 1.8);
            ph = Wrap1(ph + fr / SR);
            f.Run(Saw(ph, fr / SR), 1200 * Math.Exp(-t * 1.5) + 100, 0.5);
            o[i] = (float)(f.Low * Math.Exp(-t * 1.4));
        }
        return Norm(o, 0.8);
    }

    public static float[] ShotgunBlast()
    {
        var o = new float[S(0.75)]; var r = new Rng(51); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 200 + 2600 * Math.Exp(-t * 8), 0.7);
            double v = f.Low * 1.4 * Math.Exp(-t * 7) + Math.Sin(TAU * (50 + 60 * Math.Exp(-t * 20)) * t) * Math.Exp(-t * 11) * 0.9;
            // pump: two metallic clacks
            foreach (double at in new[] { 0.38, 0.5 })
                if (t > at) { double lt = t - at; v += Math.Sin(TAU * 900 * lt + 3 * Math.Exp(-lt * 60) * Math.Sin(TAU * 2300 * lt)) * Math.Exp(-lt * 70) * 0.35; }
            o[i] = (float)v;
        }
        return Norm(o, 0.9);
    }

    public static float[] RocketLaunch()
    {
        var o = new float[S(0.9)]; var r = new Rng(52); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 400 + 1400 * Math.Min(1, t * 3), 0.35);
            double v = f.Band * Math.Min(1, t * 25) * Math.Exp(-t * 3.5) * 1.3 + Math.Sin(TAU * 90 * t) * Math.Exp(-t * 15) * 0.7;
            o[i] = (float)v;
        }
        return Norm(o, 0.8);
    }

    public static float[] PlasmaShot()
    {
        var o = new float[S(0.2)]; double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph += TAU * (700 + 900 * Math.Exp(-t * 25)) / SR;
            o[i] = (float)((Math.Sin(ph + 3 * Math.Exp(-t * 20) * Math.Sin(ph * 2.5)) * 0.7 + Math.Sin(TAU * 2600 * t) * Math.Exp(-t * 40) * 0.3) * Math.Exp(-t * 16));
        }
        return Norm(o, 0.6);
    }

    public static float[] WeaponPickup()
    {
        var o = new float[S(0.9)];
        foreach (double at in new[] { 0.0, 0.12 })
            for (int i = S(at); i < o.Length; i++)
            {
                double t = (double)(i - S(at)) / SR;
                o[i] += (float)(Math.Sin(TAU * 300 * t + 4 * Math.Exp(-t * 25) * Math.Sin(TAU * 740 * t)) * Math.Exp(-t * 18) * 0.5);
            }
        int[] notes = { 72, 76, 79, 84 };
        for (int k = 0; k < 4; k++)
            for (int i = S(0.25 + k * 0.07); i < o.Length; i++)
            {
                double t = (double)(i - S(0.25 + k * 0.07)) / SR, fq = Mtof(notes[k]);
                o[i] += (float)(Math.Sin(TAU * fq * t + 1.5 * Math.Exp(-t * 4) * Math.Sin(TAU * fq * 2 * t)) * Math.Exp(-t * 5) * 0.3);
            }
        return Norm(o, 0.7);
    }

    public static float[] AmmoPickup()
    {
        var o = new float[S(0.2)];
        foreach (double at in new[] { 0.0, 0.07 })
            for (int i = S(at); i < o.Length; i++)
            {
                double t = (double)(i - S(at)) / SR;
                o[i] += (float)(Math.Sin(TAU * 900 * t + 2 * Math.Exp(-t * 60) * Math.Sin(TAU * 2100 * t)) * Math.Exp(-t * 50));
            }
        return Norm(o, 0.5);
    }

    public static float[] DryClick()
    {
        var o = new float[S(0.07)]; var r = new Rng(53); double lp = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR, n = r.Next();
            lp += (n - lp) * 0.3;
            o[i] = (float)(((n - lp) * 0.6 + Math.Sin(TAU * 2000 * t) * 0.4) * Math.Exp(-t * 150));
        }
        return Norm(o, 0.4);
    }

    public static float[] WeaponSwitch()
    {
        var o = new float[S(0.25)]; var r = new Rng(54); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 1500, 0.4);
            double v = f.Band * Math.Exp(-t * 20) * 0.6;
            if (t > 0.15) { double lt = t - 0.15; v += Math.Sin(TAU * 1200 * lt) * Math.Exp(-lt * 80) * 0.5; }
            o[i] = (float)v;
        }
        return Norm(o, 0.4);
    }

    // a short "oof!": a gliding voice-like tone through two vowel formants
    public static float[] Cry()
    {
        var o = new float[S(0.4)]; var f1 = new Svf(); var f2 = new Svf(); var r = new Rng(61); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            double pitch = 150 + 70 * Math.Sin(Math.PI * Math.Min(1, t / 0.3)) + 6 * Math.Sin(TAU * 6 * t);
            ph = Wrap1(ph + pitch / SR);
            double src = Saw(ph, pitch / SR) + r.Next() * 0.08;
            f1.Run(src, 620, 0.2); f2.Run(src, 1100, 0.25);
            double env = Math.Min(1, t / 0.03) * Math.Exp(-Math.Max(0, t - 0.12) * 9);
            o[i] = (float)((f1.Band + f2.Band * 0.7) * env);
        }
        return Norm(o, 0.7);
    }

    public static float[] Kamikaze()
    {
        var o = new float[S(1.4)];
        double at = 0, gap = 0.22;
        while (at < 1.3)
        {
            for (int i = S(at); i < Math.Min(o.Length, S(at + 0.05)); i++)
            {
                double t = (double)(i - S(at)) / SR;
                o[i] += (float)((Math.Sin(TAU * 1500 * t) > 0 ? 0.35 : -0.35) * Math.Sin(Math.PI * t / 0.05));
            }
            at += gap; gap = Math.Max(0.05, gap * 0.78);
        }
        double ph = 0, bph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph += TAU * (400 + 1000 * t / 1.4) / SR;
            o[i] += (float)(Math.Sin(ph) * 0.15 * Math.Min(1, t * 4));
            // the saw blade spinning up
            double bf = 180 + 260 * t / 1.4;
            bph = Wrap1(bph + bf / SR);
            o[i] += (float)(Saw(bph, bf / SR) * 0.2 * Math.Min(1, t * 3) * (0.7 + 0.3 * Math.Sin(TAU * 30 * t)));
        }
        return Norm(o, 0.6);
    }

    public static float[] Jump()
    {
        var o = new float[S(0.25)]; var r = new Rng(71); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 500 + 2500 * t / 0.25, 0.5);
            o[i] = (float)(f.Band * Math.Sin(Math.PI * t / 0.25) + Math.Sin(TAU * 110 * t) * Math.Exp(-t * 40) * 0.5);
        }
        return Norm(o, 0.5);
    }

    public static float[] Land()
    {
        var o = new float[S(0.25)]; var r = new Rng(72); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 300, 0.8);
            o[i] = (float)(f.Low * Math.Exp(-t * 25) * 1.5 + Math.Sin(TAU * (70 - 30 * t) * t) * Math.Exp(-t * 18));
        }
        return Norm(o, 0.7);
    }

    // gulps and a rising power-up chime
    public static float[] Energy()
    {
        var o = new float[S(1.2)];
        foreach (double at in new[] { 0.0, 0.16, 0.32 })
            for (int i = S(at); i < S(at + 0.12); i++)
            {
                double t = (double)(i - S(at)) / SR;
                o[i] += (float)(Math.Sin(TAU * (180 + 400 * t) * t) * Math.Sin(Math.PI * t / 0.12) * 0.6);
            }
        double ph = 0;
        for (int i = S(0.45); i < o.Length; i++)
        {
            double t = (double)(i - S(0.45)) / SR;
            ph += TAU * (400 * Math.Pow(4, t / 0.6)) / SR;
            o[i] += (float)((Math.Sin(ph) + 0.4 * Math.Sin(ph * 2)) * Math.Min(1, t * 20) * Math.Exp(-t * 3) * 0.4);
        }
        return Norm(o, 0.7);
    }

    // out of breath: two panting gasps of filtered noise
    public static float[] Breath()
    {
        var o = new float[S(1.1)]; var r = new Rng(73); var f = new Svf();
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR, lt = t % 0.55;
            f.Run(r.Next(), 1200 + 600 * Math.Sin(Math.PI * lt / 0.55), 0.6);
            o[i] = (float)(f.Band * Math.Pow(Math.Sin(Math.PI * Math.Min(1, lt / 0.45)), 2));
        }
        return Norm(o, 0.5);
    }

    public static float[] ArmorUp()
    {
        var o = new float[S(0.7)]; double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            ph += TAU * (300 * Math.Pow(3, Math.Min(1, t / 0.35))) / SR;
            o[i] = (float)((Math.Sin(ph) + 0.5 * Math.Sin(ph * 2.01) + 0.3 * Math.Sin(ph * 3.02)) * Math.Min(1, t * 30) * Math.Exp(-t * 4));
        }
        return Norm(o, 0.6);
    }

    public static float[] DoorOpen()
    {
        var o = new float[S(0.9)]; var r = new Rng(81); var f = new Svf(); double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / SR;
            f.Run(r.Next(), 2800, 0.5);
            ph += TAU * (220 + 260 * Math.Min(1, t / 0.6)) / SR;
            o[i] = (float)(f.Band * Math.Exp(-t * 5) * 0.9 + Math.Sin(TAU * 70 * t) * Math.Exp(-t * 18) * 0.8
                         + Math.Sin(ph) * 0.18 * Math.Sin(Math.PI * Math.Min(1, t / 0.7)));
        }
        return Norm(o, 0.7);
    }

    public static float[] Blip()
    {
        var o = new float[S(0.09)];
        for (int i = 0; i < o.Length; i++) { double t = (double)i / SR; o[i] = (float)(Math.Sin(TAU * 880 * t) * Math.Exp(-t * 40)); }
        return Norm(o, 0.4);
    }
}

// all generated audio, filled in parallel on startup
class Bank
{
    public short[][] Music = new short[5][];
    public float[] Shot, EnemyShot, HeavyShot, Boom, BigBoom, Hurt, Pickup, Alert, Hit, Shield,
        PortalOpen, PortalEnter, CageOpen, BossWake, Death, Blip,
        ShotgunBlast, RocketLaunch, PlasmaShot, WeaponPickup, AmmoPickup, DryClick, WeaponSwitch, Cry, Kamikaze, Jump, Land, Energy, Breath, ArmorUp, DoorOpen;
    public float[][] Steps = new float[3][];
    int done;
    public const int Jobs = 6;
    public float Progress { get { return Thread.VolatileRead(ref done) / (float)Jobs; } }

    public void Generate()
    {
        Action[] jobs =
        {
            () => Music[0] = Syn.Track(0),
            () => Music[1] = Syn.Track(1),
            () => Music[2] = Syn.Track(2),
            () => Music[3] = Syn.Track(3),
            () => Music[4] = Syn.Track(4),
            () =>
            {
                Shot = Syn.Shot(); EnemyShot = Syn.EnemyShot(); HeavyShot = Syn.HeavyShot();
                Boom = Syn.Explode(false); BigBoom = Syn.Explode(true); Hurt = Syn.Hurt(); Pickup = Syn.Pickup();
                Alert = Syn.Alert(); Hit = Syn.Hit(); Shield = Syn.Shield(); PortalOpen = Syn.PortalOpen();
                PortalEnter = Syn.PortalEnter(); CageOpen = Syn.CageOpen(); BossWake = Syn.BossWake();
                Death = Syn.Death(); Blip = Syn.Blip();
                ShotgunBlast = Syn.ShotgunBlast(); RocketLaunch = Syn.RocketLaunch(); PlasmaShot = Syn.PlasmaShot();
                WeaponPickup = Syn.WeaponPickup(); AmmoPickup = Syn.AmmoPickup(); DryClick = Syn.DryClick(); WeaponSwitch = Syn.WeaponSwitch(); Cry = Syn.Cry(); Kamikaze = Syn.Kamikaze(); Jump = Syn.Jump(); Land = Syn.Land(); Energy = Syn.Energy(); Breath = Syn.Breath(); ArmorUp = Syn.ArmorUp(); DoorOpen = Syn.DoorOpen();
                for (int i = 0; i < 3; i++) Steps[i] = Syn.Step(i);
            },
        };
        Parallel.For(0, jobs.Length, i => { jobs[i](); Interlocked.Increment(ref done); });
    }
}
