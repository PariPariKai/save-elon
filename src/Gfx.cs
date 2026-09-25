// OpenGL plumbing: context, the one big fragment shader, low-res upscaling and GDI+ text sprites.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

class Gfx
{
    IntPtr dc;
    uint prog, fbTex, wallTex;
    int fbW, fbH;
    D_Id useProgram;
    D_GetUniformLocation getLoc;
    D_Uniform1f u1; D_Uniform2f u2; D_Uniform3f u3; D_Uniform4f u4; D_Uniform4fv u4v;
    readonly Dictionary<string, int> locs = new Dictionary<string, int>();

    class Sprite { public uint tex; public int w, h; }
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    T Proc<T>(string name) where T : class
    {
        IntPtr p = W.wglGetProcAddress(name);
        if (p == IntPtr.Zero) throw new Exception("OpenGL 2.0 function missing: " + name);
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    public void Init(IntPtr hwnd, bool vsync)
    {
        dc = W.GetDC(hwnd);
        var pfd = new PFD();
        pfd.nSize = (ushort)Marshal.SizeOf(typeof(PFD));
        pfd.nVersion = 1;
        pfd.dwFlags = 0x25; // DRAW_TO_WINDOW | SUPPORT_OPENGL | DOUBLEBUFFER
        pfd.cColorBits = 32;
        W.SetPixelFormat(dc, W.ChoosePixelFormat(dc, ref pfd), ref pfd);
        IntPtr rc = W.wglCreateContext(dc);
        if (rc == IntPtr.Zero || !W.wglMakeCurrent(dc, rc)) throw new Exception("Could not create an OpenGL context.");
        useProgram = Proc<D_Id>("glUseProgram");
        getLoc = Proc<D_GetUniformLocation>("glGetUniformLocation");
        u1 = Proc<D_Uniform1f>("glUniform1f");
        u2 = Proc<D_Uniform2f>("glUniform2f");
        u3 = Proc<D_Uniform3f>("glUniform3f");
        u4 = Proc<D_Uniform4f>("glUniform4f");
        u4v = Proc<D_Uniform4fv>("glUniform4fv");
        IntPtr si = W.wglGetProcAddress("wglSwapIntervalEXT");
        if (si != IntPtr.Zero) ((D_SwapInterval)Marshal.GetDelegateForFunctionPointer(si, typeof(D_SwapInterval)))(vsync ? 1 : 0);
        W.glGenTextures(1, out fbTex);
        W.glGenTextures(1, out wallTex);
    }

    public static string LoadShaderSource()
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("game.frag.gz"))
        using (var gz = new GZipStream(s, CompressionMode.Decompress))
        using (var r = new StreamReader(gz)) return r.ReadToEnd();
    }

    public void Compile(string fs)
    {
        var createShader = Proc<D_CreateShader>("glCreateShader");
        var shaderSource = Proc<D_ShaderSource>("glShaderSource");
        var compile = Proc<D_Id>("glCompileShader");
        var getShaderiv = Proc<D_GetIv>("glGetShaderiv");
        var shaderLog = Proc<D_GetLog>("glGetShaderInfoLog");
        var createProgram = Proc<D_CreateProgram>("glCreateProgram");
        var attach = Proc<D_Attach>("glAttachShader");
        var link = Proc<D_Id>("glLinkProgram");
        var getProgramiv = Proc<D_GetIv>("glGetProgramiv");
        var programLog = Proc<D_GetLog>("glGetProgramInfoLog");

        string vs = "#version 120\nvoid main(){gl_Position=gl_Vertex;}";
        prog = createProgram();
        foreach (var pair in new[] { Tuple.Create(0x8B31u, vs), Tuple.Create(0x8B30u, fs) })
        {
            uint s = createShader(pair.Item1);
            shaderSource(s, 1, new[] { pair.Item2 }, null);
            compile(s);
            int ok; getShaderiv(s, 0x8B81, out ok);
            if (ok == 0) throw new Exception("Shader compile error:\n" + Log(shaderLog, s));
            attach(prog, s);
        }
        link(prog);
        int linked; getProgramiv(prog, 0x8B82, out linked);
        if (linked == 0) throw new Exception("Shader link error:\n" + Log(programLog, prog));
    }

    static string Log(D_GetLog f, uint id)
    {
        var b = new byte[16384]; int len;
        f(id, b.Length, out len, b);
        return Encoding.ASCII.GetString(b, 0, Math.Max(0, len));
    }

    // one texel per maze lattice point telling which walls start there
    public void UploadWalls(byte[] data, int w, int h)
    {
        W.glBindTexture(W.TEX2D, wallTex);
        W.glPixelStorei(0x0CF5, 1);
        W.glTexImage2D(W.TEX2D, 0, 0x8058, w, h, 0, 0x1908, 0x1401, data);
        W.glTexParameteri(W.TEX2D, 0x2801, 0x2600);
        W.glTexParameteri(W.TEX2D, 0x2800, 0x2600);
        W.glTexParameteri(W.TEX2D, 0x2802, 0x812F);
        W.glTexParameteri(W.TEX2D, 0x2803, 0x812F);
    }

    int L(string n)
    {
        int l;
        if (!locs.TryGetValue(n, out l)) { l = getLoc(prog, n); locs[n] = l; }
        return l;
    }

    public void U(string n, float a) { u1(L(n), a); }
    public void U(string n, float a, float b) { u2(L(n), a, b); }
    public void U(string n, float a, float b, float c) { u3(L(n), a, b, c); }
    public void U(string n, float a, float b, float c, float d) { u4(L(n), a, b, c, d); }
    public void U(string n, float[] v) { u4v(L(n), v.Length / 4, v); }

    public void BeginScene(int rw, int rh)
    {
        W.glViewport(0, 0, rw, rh);
        useProgram(prog);
        W.glBindTexture(W.TEX2D, wallTex);
    }

    public void DrawScene() { W.glRecti(-1, -1, 1, 1); }

    // the scene may be rendered at lower resolution; stretch it over the whole window
    public void Upscale(int w, int h, int rw, int rh)
    {
        useProgram(0);
        if (rw == w && rh == h) return;
        W.glBindTexture(W.TEX2D, fbTex);
        if (fbW != w || fbH != h)
        {
            fbW = w; fbH = h;
            W.glTexImage2D(W.TEX2D, 0, 0x8051, w, h, 0, 0x1907, 0x1401, IntPtr.Zero);
            W.glTexParameteri(W.TEX2D, 0x2801, 0x2601);
            W.glTexParameteri(W.TEX2D, 0x2800, 0x2601);
            W.glTexParameteri(W.TEX2D, 0x2802, 0x812F);
            W.glTexParameteri(W.TEX2D, 0x2803, 0x812F);
        }
        W.glCopyTexSubImage2D(W.TEX2D, 0, 0, 0, 0, 0, rw, rh);
        W.glViewport(0, 0, w, h);
        W.glEnable(W.TEX2D);
        W.glColor4f(1, 1, 1, 1);
        float u = (float)rw / w, v = (float)rh / h;
        W.glBegin(W.QUADS);
        W.glTexCoord2f(0, 0); W.glVertex2f(-1, -1);
        W.glTexCoord2f(u, 0); W.glVertex2f(1, -1);
        W.glTexCoord2f(u, v); W.glVertex2f(1, 1);
        W.glTexCoord2f(0, v); W.glVertex2f(-1, 1);
        W.glEnd();
        W.glDisable(W.TEX2D);
    }

    // ------------------------------------------------------------------ 2D overlay
    public void Begin2D(int w, int h)
    {
        useProgram(0);
        W.glViewport(0, 0, w, h);
        W.glMatrixMode(0x1701);
        W.glLoadIdentity();
        W.glOrtho(0, w, h, 0, -1, 1);
        W.glMatrixMode(0x1700);
        W.glLoadIdentity();
        W.glEnable(0x0BE2);
        W.glBlendFunc(0x0302, 0x0303);
    }

    public void End2D()
    {
        W.glDisable(0x0BE2);
        W.glColor4f(1, 1, 1, 1);
        W.glMatrixMode(0x1701);
        W.glLoadIdentity();
        W.glMatrixMode(0x1700);
        W.glLoadIdentity();
    }

    public void Clear() { W.glClearColor(0, 0, 0, 1); W.glClear(0x4000); }

    public void Tri(float x1, float y1, float x2, float y2, float x3, float y3, float r, float g, float b, float a)
    {
        W.glDisable(W.TEX2D);
        W.glColor4f(r, g, b, a);
        W.glBegin(4);
        W.glVertex2f(x1, y1); W.glVertex2f(x2, y2); W.glVertex2f(x3, y3);
        W.glEnd();
    }

    public void Ellipse(float cx, float cy, float rx, float ry, float r, float g, float b, float a)
    {
        W.glDisable(W.TEX2D);
        W.glColor4f(r, g, b, a);
        W.glBegin(6);
        W.glVertex2f(cx, cy);
        for (int i = 0; i <= 48; i++)
        {
            double t = i * Math.PI * 2 / 48;
            W.glVertex2f(cx + rx * (float)Math.Cos(t), cy + ry * (float)Math.Sin(t));
        }
        W.glEnd();
    }

    // a ring segment from angle a0 to a1 (radians, screen space: +y is down)
    public void Arc(float cx, float cy, float r, float width, float a0, float a1, float cr, float cg, float cb, float ca)
    {
        W.glDisable(W.TEX2D);
        W.glColor4f(cr, cg, cb, ca);
        W.glBegin(8);
        int n = Math.Max(4, (int)(Math.Abs(a1 - a0) * 24));
        for (int i = 0; i <= n; i++)
        {
            double t = a0 + (a1 - a0) * i / n;
            float c = (float)Math.Cos(t), s = (float)Math.Sin(t);
            W.glVertex2f(cx + c * (r - width / 2), cy + s * (r - width / 2));
            W.glVertex2f(cx + c * (r + width / 2), cy + s * (r + width / 2));
        }
        W.glEnd();
    }

    public void Line(float x1, float y1, float x2, float y2, float width, float r, float g, float b, float a)
    {
        float dx = x2 - x1, dy = y2 - y1, l = (float)Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-3f) return;
        float nx = -dy / l * width / 2, ny = dx / l * width / 2;
        W.glDisable(W.TEX2D);
        W.glColor4f(r, g, b, a);
        W.glBegin(W.QUADS);
        W.glVertex2f(x1 + nx, y1 + ny); W.glVertex2f(x2 + nx, y2 + ny); W.glVertex2f(x2 - nx, y2 - ny); W.glVertex2f(x1 - nx, y1 - ny);
        W.glEnd();
    }

    public void Rect(float x, float y, float w, float h, float r, float g, float b, float a)
    {
        W.glDisable(W.TEX2D);
        W.glColor4f(r, g, b, a);
        W.glBegin(W.QUADS);
        W.glVertex2f(x, y); W.glVertex2f(x + w, y); W.glVertex2f(x + w, y + h); W.glVertex2f(x, y + h);
        W.glEnd();
    }

    // align: 0 left, 1 center, 2 right. Returns the drawn width.
    public float Text(string s, float size, Color col, float x, float y, float alpha, int align)
    {
        if (string.IsNullOrEmpty(s) || alpha <= 0.003f) return 0;
        var sp = GetSprite(s, (int)Math.Max(8, size), col);
        float dx = align == 1 ? x - sp.w * 0.5f : align == 2 ? x - sp.w : x;
        W.glEnable(W.TEX2D);
        W.glBindTexture(W.TEX2D, sp.tex);
        W.glColor4f(1, 1, 1, alpha);
        W.glBegin(W.QUADS);
        W.glTexCoord2f(0, 0); W.glVertex2f(dx, y);
        W.glTexCoord2f(1, 0); W.glVertex2f(dx + sp.w, y);
        W.glTexCoord2f(1, 1); W.glVertex2f(dx + sp.w, y + sp.h);
        W.glTexCoord2f(0, 1); W.glVertex2f(dx, y + sp.h);
        W.glEnd();
        W.glDisable(W.TEX2D);
        return sp.w;
    }

    public float Measure(string s, float size)
    {
        return string.IsNullOrEmpty(s) ? 0 : GetSprite(s, (int)Math.Max(8, size), Color.White).w;
    }

    Sprite GetSprite(string text, int size, Color col)
    {
        string key = size + "|" + col.ToArgb() + "|" + text;
        Sprite sp;
        if (sprites.TryGetValue(key, out sp)) return sp;
        if (sprites.Count > 96)
        {
            foreach (var old in sprites.Values) { uint t = old.tex; W.glDeleteTextures(1, ref t); }
            sprites.Clear();
        }
        int pad = Math.Max(2, size / 6);
        using (var path = new GraphicsPath())
        using (var family = new FontFamily("Segoe UI"))
        {
            path.AddString(text, family, (int)FontStyle.Bold, size, new PointF(pad, pad), StringFormat.GenericTypographic);
            var b = path.GetBounds();
            int w = Math.Max(4, (int)Math.Ceiling(b.Right + pad));
            int h = (int)Math.Ceiling(size * 1.3f + pad * 2);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var pen = new Pen(Color.FromArgb(210, 0, 0, 0), Math.Max(2f, size * 0.14f)) { LineJoin = LineJoin.Round })
                using (var brush = new SolidBrush(col))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.DrawPath(pen, path);
                    g.FillPath(brush, path);
                }
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                sp = new Sprite { w = w, h = h };
                W.glGenTextures(1, out sp.tex);
                W.glBindTexture(W.TEX2D, sp.tex);
                W.glPixelStorei(0x0CF5, 4);
                W.glTexImage2D(W.TEX2D, 0, 0x8058, w, h, 0, 0x80E1, 0x1401, bd.Scan0);
                W.glTexParameteri(W.TEX2D, 0x2801, 0x2601);
                W.glTexParameteri(W.TEX2D, 0x2800, 0x2601);
                W.glTexParameteri(W.TEX2D, 0x2802, 0x812F);
                W.glTexParameteri(W.TEX2D, 0x2803, 0x812F);
                bmp.UnlockBits(bd);
            }
        }
        sprites[key] = sp;
        return sp;
    }

    public void Present() { W.SwapBuffers(dc); }

    public void SavePng(string path, int w, int h)
    {
        var px = new byte[w * h * 4];
        W.glFinish();
        W.glReadPixels(0, 0, w, h, 0x80E1, 0x1401, px);
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb))
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, (h - 1 - y) * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    public byte[] ReadLuma(int w, int h)
    {
        var px = new byte[w * h * 4];
        W.glFinish();
        W.glReadPixels(0, 0, w, h, 0x80E1, 0x1401, px);
        var l = new byte[w * h];
        for (int i = 0; i < l.Length; i++) l[i] = (byte)((px[i * 4] * 29 + px[i * 4 + 1] * 150 + px[i * 4 + 2] * 77) >> 8);
        return l;
    }

    public string GpuName()
    {
        return Marshal.PtrToStringAnsi(W.glGetString(0x1F01)) + " / " + Marshal.PtrToStringAnsi(W.glGetString(0x1F02));
    }
}
