// Win32 / OpenGL / waveOut imports. Only DLLs that ship with every Windows are used.
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
struct PFD
{
    public ushort nSize, nVersion;
    public uint dwFlags;
    public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift,
        cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
        cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
    public uint dwLayerMask, dwVisibleMask, dwDamageMask;
}

static class W
{
    [DllImport("user32")] public static extern bool SetProcessDPIAware();
    [DllImport("user32")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("gdi32")] public static extern int ChoosePixelFormat(IntPtr dc, ref PFD pfd);
    [DllImport("gdi32")] public static extern bool SetPixelFormat(IntPtr dc, int f, ref PFD pfd);
    [DllImport("gdi32")] public static extern bool SwapBuffers(IntPtr dc);
    [DllImport("kernel32")] public static extern IntPtr LoadLibrary(string n);
    [DllImport("opengl32")] public static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32")] public static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);
    [DllImport("opengl32")] public static extern IntPtr wglGetProcAddress(string n);
    [DllImport("opengl32")] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32")] public static extern void glRecti(int a, int b, int c, int d);
    [DllImport("opengl32")] public static extern void glEnable(uint c);
    [DllImport("opengl32")] public static extern void glDisable(uint c);
    [DllImport("opengl32")] public static extern void glGenTextures(int n, out uint t);
    [DllImport("opengl32")] public static extern void glDeleteTextures(int n, ref uint t);
    [DllImport("opengl32")] public static extern void glBindTexture(uint target, uint t);
    [DllImport("opengl32")] public static extern void glTexParameteri(uint target, uint pname, int v);
    [DllImport("opengl32")] public static extern void glTexImage2D(uint target, int level, int ifmt, int w, int h, int border, uint fmt, uint type, IntPtr data);
    [DllImport("opengl32")] public static extern void glTexImage2D(uint target, int level, int ifmt, int w, int h, int border, uint fmt, uint type, byte[] data);
    [DllImport("opengl32")] public static extern void glCopyTexSubImage2D(uint target, int level, int xo, int yo, int x, int y, int w, int h);
    [DllImport("opengl32")] public static extern void glPixelStorei(uint p, int v);
    [DllImport("opengl32")] public static extern void glBegin(uint m);
    [DllImport("opengl32")] public static extern void glEnd();
    [DllImport("opengl32")] public static extern void glTexCoord2f(float s, float t);
    [DllImport("opengl32")] public static extern void glVertex2f(float x, float y);
    [DllImport("opengl32")] public static extern void glColor4f(float r, float g, float b, float a);
    [DllImport("opengl32")] public static extern void glBlendFunc(uint s, uint d);
    [DllImport("opengl32")] public static extern void glMatrixMode(uint m);
    [DllImport("opengl32")] public static extern void glLoadIdentity();
    [DllImport("opengl32")] public static extern void glOrtho(double l, double r, double b, double t, double n, double f);
    [DllImport("opengl32")] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32")] public static extern void glClear(uint mask);
    [DllImport("opengl32")] public static extern void glReadPixels(int x, int y, int w, int h, uint fmt, uint type, byte[] data);
    [DllImport("opengl32")] public static extern void glFinish();
    [DllImport("opengl32")] public static extern IntPtr glGetString(uint n);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm")] public static extern uint timeEndPeriod(uint ms);

    public const uint TEX2D = 0x0DE1, QUADS = 7;
}

delegate uint D_CreateShader(uint type);
delegate void D_ShaderSource(uint s, int n, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] src, int[] len);
delegate void D_Id(uint id);
delegate void D_GetIv(uint id, uint p, out int v);
delegate void D_GetLog(uint id, int max, out int len, byte[] log);
delegate uint D_CreateProgram();
delegate void D_Attach(uint p, uint s);
delegate int D_GetUniformLocation(uint p, string n);
delegate void D_Uniform1f(int l, float a);
delegate void D_Uniform2f(int l, float a, float b);
delegate void D_Uniform3f(int l, float a, float b, float c);
delegate void D_Uniform4f(int l, float a, float b, float c, float d);
delegate void D_Uniform4fv(int l, int count, float[] v);
delegate int D_SwapInterval(int i);
