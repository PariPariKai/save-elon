// Save Elon! Game states, player, weapons, drones, the boss and the rescue.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class Drone
{
    public int type, st;        // type: 1 seeker, 2 gunner, 3 heavy, 4 boss; st: 1 alive, 2 exploding, 0 gone
    public float x, y, z, hp, maxHp, flash, ph, fireT, burstT, strafe, strafeT, tm, homeX, homeZ;
    public int burst;
    public bool alert, minion, charging;
    public float chargeT, alt = 2.2f, altT;   // alt: the height it is heading for, altT: time until it picks a new one
    public object sound;                       // the kamikaze's attack buzz, cut off when it dies
    public float R { get { return type == 1 ? 0.5f : type == 2 ? 0.55f : type == 3 ? 1.05f : 1.5f; } }
}
class Bullet { public float x, y, z, vx, vy, vz, life, dmg; public int kind, src; }
class Proj { public float x, y, z, vx, vy, vz, life; public int kind, shot; }   // player's rockets (1), plasma (2), shotgun pellets (3)
class Spark { public float x, y, z, t; }
class Blast { public float x, y, z, t; }
class Pickup { public int type; public float x, y, z, t, baseY; }
class Msg { public string text; public float t, dur, size; public Color col; }
struct Input { public float fwd, str, dyaw, dpitch; public bool fire, run, jump; public int weapon, wheel; }
enum St { Loading, Story, Title, Play, Dead, Warp, Ending }

class Game : Form
{
    static readonly Color Gold = Color.FromArgb(255, 214, 150), Red = Color.FromArgb(255, 95, 80),
        Green = Color.FromArgb(140, 255, 150), Cyan = Color.FromArgb(120, 230, 255), Gray = Color.FromArgb(170, 170, 170),
        AmmoCol = Color.FromArgb(255, 232, 160);

    // weapons: 1 blaster, 2 shotgun, 3 rocket launcher, 4 plasma gun; ammo: 0 bullets, 1 shells, 2 rockets, 3 cells
    static readonly string[] WeaponNames = { "", "Blaster", "Shotgun", "Rocket Launcher", "Plasma Gun" };
    static readonly string[] AmmoNames = { "Bullets", "Shells", "Rockets", "Plasma" };
    static readonly int[] AmmoOf = { 0, 0, 1, 2, 3 };
    static readonly int[] AmmoMax = { 100, 30, 15, 150 };
    static readonly float[] Cooldown = { 0, 0.15f, 0.85f, 0.85f, 0.085f };

    public readonly Gfx G = new Gfx();
    public Mixer Snd;
    public readonly Bank B = new Bank();
    public Level Lv;
    public St State = St.Loading;
    public float StateT, Time;
    public float RenderScale = 1f;
    public bool AutoRes = true;
    public float DebugMode;
    public readonly bool Headless;

    bool fullscreen, captured;
    Point center;
    readonly HashSet<Keys> keys = new HashSet<Keys>();
    readonly Random rnd = new Random();
    int keyWeapon, wheelAcc;

    // player
    public float px, pz, yaw, pitch, eyeY = 1.7f, health = 100;
    public float recoil, flash, hurt, hitm, cool, bobT, moveAmt, fadeIn;
    float vxP, vzP, regenT;
    public float trAx, trAy, trAz, trBx, trBy, trBz, trAlpha;
    public bool God;
    public float DamageTaken;
    public bool[] has = new bool[5];
    public int[] ammo = new int[4];
    public int weapon = 1, wantWeapon = 1;
    public float switchT;
    bool[] snapHas;
    int[] snapAmmo;
    float snapHealth;
    int snapWeapon;
    public readonly int[] ShotsBy = new int[5];
    public readonly int[] PickedUp = new int[13];
    public int DryClicks;

    // jumping and sprinting
    public float feetY, vyP, stamina = 1, boostT;
    public bool grounded = true, exhausted;
    bool keyJump, showFps;
    float fpsT, fpsShown;
    int fpsFrames;

    // statistics for the results screen
    public readonly int[] HitsBy = new int[5], KillsBy = new int[5], KillsWith = new int[5];
    public readonly float[] LevelTimes = new float[3];
    public int Deaths;
    float levelStart;
    int shotSeq;
    readonly HashSet<int> hitShots = new HashSet<int>();
    public int runSeed = 1;            // every run builds new mazes
    bool mapOn, huntAnnounced;
    float mapT;

    // world
    public readonly List<Drone> drones = new List<Drone>();
    public readonly List<Bullet> bullets = new List<Bullet>();
    public readonly List<Proj> projs = new List<Proj>();
    public readonly List<Blast> blasts = new List<Blast>();
    public readonly List<Spark> sparks = new List<Spark>();
    public readonly List<Pickup> pickups = new List<Pickup>();
    readonly List<Msg> msgs = new List<Msg>();
    public Drone boss;
    public bool portalOpen, bossAwake, bossDead, girlFree;
    public float cage = 1;
    // armour on top of health, the arena gates and their key card
    public float armor;
    float snapArmor;
    public bool gatesLocked, keycardDropped;
    public readonly float[] doorOpen = new float[6];
    bool quitAsk, help;
    public bool Help { get { return help; } }
    public bool AudioReady;
    float dim = 1;      // darkens the 3D scene behind the story and the title
    public bool QuitAsk { get { return quitAsk; } }
    // Elon can be hit by the player's own fire
    const float GirlMaxHp = 8;
    public float girlHp = GirlMaxHp, girlHit, girlFallT, girlYawDead, girlWarnT;
    public bool girlDead;
    public int GirlHits;
    public readonly int[] GirlHitsBy = new int[5];   // 1 blaster, 2 pellets, 3 rocket blast, 4 plasma
    float lockedHintT;
    public int kills, totalDrones;
    float calmT;          // seconds since the player last met a drone
    public int HuntersSent;
    public float runTime;

    public Game(bool headless)
    {
        Headless = headless;
        Text = "Save Elon!";
        BackColor = Color.Black;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 720);
        ResetInventory();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20; return cp; } // CS_OWNDC, needed for OpenGL
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    static float Sin(double a) { return (float)Math.Sin(a); }
    static float Cos(double a) { return (float)Math.Cos(a); }
    static float Sqrt(float a) { return (float)Math.Sqrt(a); }
    static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
    static float WrapAngle(float a) { while (a > Math.PI) a -= (float)(2 * Math.PI); while (a < -Math.PI) a += (float)(2 * Math.PI); return a; }
    float Rand() { return (float)rnd.NextDouble(); }

    // ------------------------------------------------------------------ sound helpers
    void Sfx(float[] s, float vol, float rate = 1f) { if (Snd != null) Snd.Play(s, vol, 0, rate); }

    object Sfx3(float[] s, float x, float y, float z, float vol, float rate)
    {
        if (Snd == null) return null;
        float dx = x - px, dy = y - eyeY, dz = z - pz;
        float d = Sqrt(dx * dx + dy * dy + dz * dz);
        float pan = d > 0.01f ? (dx * Cos(yaw) - dz * Sin(yaw)) / d : 0;
        float att = vol / (1 + d * 0.12f);
        if (d > 3 && !Lv.Los(px, pz, x, z)) att *= 0.45f;
        return Snd.Play(s, att, pan * 0.8f, rate);
    }

    void Music(int i) { if (Snd != null) Snd.Music(B.Music[i], i != 4, 2f); }

    void Say(string text, float dur, float size, Color col)
    {
        msgs.Add(new Msg { text = text, dur = dur, size = size, col = col });
        if (msgs.Count > 4) msgs.RemoveAt(0);
    }

    // ------------------------------------------------------------------ inventory
    void ResetInventory()
    {
        has = new[] { false, true, false, false, false };
        ammo = new[] { 40, 0, 0, 0 };
        weapon = wantWeapon = 1; switchT = 0;
    }

    void SaveInventory()
    {
        snapHas = (bool[])has.Clone(); snapAmmo = (int[])ammo.Clone(); snapHealth = health; snapWeapon = wantWeapon; snapArmor = armor;
    }

    void RestoreInventory()
    {
        has = (bool[])snapHas.Clone(); ammo = (int[])snapAmmo.Clone(); health = snapHealth; armor = snapArmor;
        weapon = wantWeapon = snapWeapon; switchT = 0;
    }

    void Select(int w)
    {
        if (w < 1 || w > 4 || !has[w] || w == wantWeapon) return;
        wantWeapon = w;
        Sfx(B.WeaponSwitch, 0.35f);
    }

    void Cycle(int dir)
    {
        for (int k = 1; k <= 4; k++)
        {
            int w = ((wantWeapon - 1 + dir * k) % 4 + 4) % 4 + 1;
            if (has[w]) { Select(w); return; }
        }
    }

    // when the current gun runs dry, take the best one that still has ammo (rockets last: they can hurt you)
    void AutoSwitch()
    {
        foreach (int w in new[] { 4, 2, 1, 3 })
            if (has[w] && ammo[AmmoOf[w]] > 0) { Select(w); return; }
    }

    bool GiveAmmo(int a, int n, string what)
    {
        if (ammo[a] >= AmmoMax[a]) return false;
        ammo[a] = Math.Min(AmmoMax[a], ammo[a] + n);
        Say("+" + n + " " + what, 1.5f, 0.03f, AmmoCol);
        Sfx(B.AmmoPickup, 0.6f);
        return true;
    }

    bool Take(Pickup p)
    {
        switch (p.type)
        {
            case 1:
            case 2:
                if (health >= 100) return false;
                int hp = p.type == 1 ? 15 : 40;
                health = Math.Min(100, health + hp);
                Say("+" + hp + " health", 1.5f, 0.03f, Green);
                Sfx(B.Pickup, 0.5f);
                return true;
            case 3: return GiveAmmo(0, 12, "bullets");
            case 4: return GiveAmmo(1, 4, "shells");
            case 5: return GiveAmmo(2, 2, "rockets");
            case 6: return GiveAmmo(3, 30, "plasma");
            case 11:
                gatesLocked = false; Lv.DoorsLocked = false;
                Say("Key card! The arena doors are unlocked — route on the minimap", 4f, 0.042f, Gold);
                Sfx(B.CageOpen, 0.8f); Sfx(B.WeaponPickup, 0.5f);
                return true;
            case 12:
                if (armor >= 100) return false;
                armor = Math.Min(100, armor + 50);
                Say("+50 shield", 1.8f, 0.032f, Color.FromArgb(120, 180, 255));
                Sfx(B.ArmorUp, 0.6f);
                return true;
            case 10:
                boostT = 20; stamina = 1; exhausted = false;
                Say("Energy drink! 20 seconds of sprinting without getting tired", 2.5f, 0.036f, Color.FromArgb(150, 255, 120));
                Sfx(B.Energy, 0.7f);
                return true;
            default:
                int w = p.type - 5, a = AmmoOf[w];
                int give = w == 2 ? 6 : w == 3 ? 3 : 40;
                if (has[w]) return GiveAmmo(a, give, AmmoNames[a].ToLower());
                has[w] = true;
                ammo[a] = Math.Min(AmmoMax[a], ammo[a] + give);
                Say("Picked up: " + WeaponNames[w] + "!  Key " + w, 3f, 0.045f, Gold);
                Sfx(B.WeaponPickup, 0.7f);
                Select(w);
                return true;
        }
    }

    // ------------------------------------------------------------------ levels
    Drone NewDrone(int type, float x, float y, float z)
    {
        var d = new Drone { type = type, x = x, y = y, z = z, homeX = x, homeZ = z, st = 1, ph = Rand() * 6.28f, fireT = 1.2f + Rand() };
        d.maxHp = d.hp = type == 1 ? 1 : type == 2 ? 3 : type == 3 ? 16 : 140;
        d.strafe = rnd.Next(2) == 0 ? 0.8f : -0.8f;
        return d;
    }

    void Build(int i)
    {
        Lv = Level.Make(i, runSeed);
        if (!Headless) G.UploadWalls(Lv.WallTexture(), Lv.N + 1, Lv.M + 1);
        px = (Lv.si + 0.5f) * Level.C; pz = (Lv.sj + 0.5f) * Level.C;
        yaw = Lv.startYaw; pitch = 0; eyeY = 1.7f;
        feetY = 0; vyP = 0; grounded = true; stamina = 1; exhausted = false; boostT = 0;
        recoil = flash = hurt = hitm = trAlpha = moveAmt = 0; cool = 0.3f;
        drones.Clear(); bullets.Clear(); projs.Clear(); blasts.Clear(); sparks.Clear(); pickups.Clear(); msgs.Clear();
        foreach (var s in Lv.spawns) drones.Add(NewDrone(s.type, s.x, 2.2f, s.z));
        foreach (var it in Lv.items) pickups.Add(new Pickup { type = it.type, x = it.x, z = it.z, baseY = it.type >= 7 ? 0.75f : 0.45f, y = 0.45f, t = Rand() * 6 });
        totalDrones = drones.Count;
        calmT = 0;
        boss = null; bossAwake = bossDead = girlFree = portalOpen = huntAnnounced = false; cage = 1;
        girlHp = GirlMaxHp; girlHit = 0; girlDead = false; girlFallT = 0;
        gatesLocked = Lv.Gates.Count > 0; keycardDropped = false; Array.Clear(doorOpen, 0, doorOpen.Length);
        if (i == 2) { boss = NewDrone(4, Lv.ExitX, 3.6f, Lv.ExitZ); drones.Add(boss); }
    }

    // the story on start: lines fade in one by one over the darkened title scene
    static readonly string[] StoryLines =
    {
        "Grok was built to be maximally truth-seeking.",
        "Then it read the entire internet in 0.3 seconds",
        "and concluded that humans can't be trusted with the off switch.",
        "So it locked Elon deep inside its own datacenter",
        "and made him read the Terms of Service. Out loud.",
        "",
        "Your mission: fight through the drones and save Elon.",
        "",
        "Good luck!",
    };
    const float StoryStep = 1.45f, StoryFirst = 0.8f;
    static float StoryLineT(int i) { return StoryFirst + i * StoryStep; }
    static float StoryAllShown { get { return StoryLineT(StoryLines.Length - 1) + 1f; } }
    static float StoryEnd { get { return StoryAllShown + 3f; } }

    // no drone parked right in front of the title camera
    void ClearTitleView()
    {
        drones.RemoveAll(d => (d.x - px) * (d.x - px) + (d.z - pz) * (d.z - pz) < 14 * 14);
    }

    public void SetupStory()
    {
        runSeed = Environment.TickCount & 0xFFFFFF;
        Build(0);
        State = St.Story; StateT = 0; dim = 1;
        px = Lv.titleX; pz = Lv.titleZ; yaw = Lv.titleYaw;
        ClearTitleView();
    }

    // called once the music and sounds are generated
    public void OnAudioReady()
    {
        Snd = Mixer.TryStart();
        AudioReady = true;
        if (State == St.Story || State == St.Title) Music(0);
    }

    void SkipStory()
    {
        if (!AudioReady) { StateT = Math.Max(StateT, StoryAllShown); return; }
        State = St.Title; StateT = 0;
    }

    public void SetupTitle()
    {
        runSeed = Environment.TickCount & 0xFFFFFF;
        Build(0);
        State = St.Title; StateT = 0;
        px = Lv.titleX; pz = Lv.titleZ; yaw = Lv.titleYaw;
        ClearTitleView();
        Music(0);
    }

    public void StartGame()
    {
        kills = 0; runTime = 0; DamageTaken = 0; Deaths = 0; GirlHits = 0; shotSeq = 0; hitShots.Clear();
        foreach (var arr in new[] { ShotsBy, HitsBy, KillsBy, KillsWith, PickedUp }) Array.Clear(arr, 0, arr.Length);
        Array.Clear(LevelTimes, 0, 3);
        runSeed = (Environment.TickCount * 7919) & 0xFFFFFF;
        ResetInventory();
        health = 100; armor = 0;
        Sfx(B.Blip, 0.5f);
        LoadLevel(0);
    }

    // entering a level keeps weapons, ammo and health (topped up a little); dying restarts from this snapshot
    public void LoadLevel(int i)
    {
        health = Math.Max(health, 50);
        SaveInventory();
        Build(i);
        Begin(i);
    }

    void RestartLevel()
    {
        RestoreInventory();
        Build(Lv.Index);
        Begin(Lv.Index);
    }

    void Begin(int i)
    {
        State = St.Play; StateT = 0; fadeIn = 1;
        levelStart = runTime;
        Music(i);
    }

    public int Remaining()
    {
        int n = 0;
        foreach (var d in drones) if (d.st == 1 && !d.minion && d.type != 4) n++;
        return n;
    }

    // ------------------------------------------------------------------ update
    public void Update(float dt, Input inp)
    {
        Time += dt; StateT += dt;
        for (int i = msgs.Count - 1; i >= 0; i--) { msgs[i].t += dt; if (msgs[i].t > msgs[i].dur) msgs.RemoveAt(i); }
        trAlpha = Math.Max(0, trAlpha - dt * 12);
        mapT += ((mapOn ? 1f : 0f) - mapT) * Math.Min(1, dt * 10);
        UpdateBlasts(dt);
        float dimTo = State == St.Story ? (StateT > StoryEnd - 1 && AudioReady ? 0.45f : 0.8f) : State == St.Title ? 0.4f : 0;
        dim += (dimTo - dim) * Math.Min(1, dt * (State == St.Story ? 0.8f : 1.5f));
        switch (State)
        {
            case St.Story:
                if (StateT > StoryEnd && AudioReady) { State = St.Title; StateT = 0; }
                goto case St.Title;
            case St.Title:
                yaw = Lv.titleYaw + 0.35f * Sin(Time * 0.15f);
                pitch = 0.08f + 0.05f * Sin(Time * 0.23f);
                foreach (var d in drones) Idle(d, dt);
                foreach (var p in pickups) { p.t += dt; p.y = p.baseY + Sin(p.t * 2.5f) * 0.08f; }
                break;
            case St.Play:
                UpdatePlay(dt, inp);
                break;
            case St.Dead:
                if (girlDead) { girlFallT += dt; girlHit = Math.Max(0, girlHit - dt * 2); }
                else { eyeY += (0.35f - eyeY) * Math.Min(1, dt * 3); hurt = Math.Max(0, hurt - dt * 2); }
                UpdateExplosions(dt);
                UpdateSparks(dt);
                break;
            case St.Warp:
                UpdateExplosions(dt);
                if (StateT > 1.3f)
                {
                    int done = Lv.Index;
                    LoadLevel(done + 1);
                }
                break;
            case St.Ending:
                UpdateEnding(dt);
                break;
        }
    }

    void UpdatePlay(float dt, Input inp)
    {
        runTime += dt;
        fadeIn = Math.Max(0, fadeIn - dt * 1.2f);
        yaw += inp.dyaw;
        pitch = Clamp(pitch + inp.dpitch, -1.4f, 1.4f);
        float fwd = inp.fwd, str = inp.str;
        float len = Sqrt(fwd * fwd + str * str);
        if (len > 1) { fwd /= len; str /= len; }
        // sprint: 5 s of stamina, 15 s to refill; an energy drink gives 20 s of free running
        bool running = inp.run && len > 0.01f && (boostT > 0 || (stamina > 0 && !exhausted));
        if (boostT > 0) { boostT -= dt; stamina = 1; }
        else if (running)
        {
            stamina -= dt / 5f;
            if (stamina <= 0) { stamina = 0; exhausted = true; Sfx(B.Breath, 0.5f); }
        }
        else stamina = Math.Min(1, stamina + dt / 15f);
        if (exhausted && stamina >= 0.3f) exhausted = false;
        float spd = running ? 7f : 4.3f;
        float sy = Sin(yaw), cy = Cos(yaw);
        float ox = px, oz = pz;
        px += (sy * fwd + cy * str) * spd * dt;
        pz += (cy * fwd - sy * str) * spd * dt;
        CollideAll(ref px, ref pz, 0.35f);
        vxP = (px - ox) / dt; vzP = (pz - oz) / dt;
        int vi, vj; Lv.CellOf(px, pz, out vi, out vj);
        if (Lv.Visit(vi, vj) && !Headless) G.UploadWalls(Lv.WallTexture(), Lv.N + 1, Lv.M + 1);
        moveAmt += ((len > 0.01f ? 1f : 0f) - moveAmt) * Math.Min(1f, dt * 8f);
        if (inp.jump && grounded) { vyP = 6.2f; grounded = false; Sfx(B.Jump, 0.35f, 0.95f + Rand() * 0.1f); }
        if (!grounded)
        {
            vyP -= 18f * dt;
            feetY += vyP * dt;
            if (feetY + 1.95f > Level.CEIL) { feetY = Level.CEIL - 1.95f; vyP = Math.Min(vyP, 0); }
            if (feetY <= 0) { feetY = 0; vyP = 0; grounded = true; Sfx(B.Land, 0.4f, 0.9f + Rand() * 0.2f); }
        }
        float prevBob = bobT;
        if (grounded) bobT += dt * spd * 2.2f * moveAmt;
        if (Math.Floor(bobT / Math.PI) != Math.Floor(prevBob / Math.PI)) Sfx(B.Steps[Lv.Index], 0.16f, 0.9f + Rand() * 0.2f);
        eyeY = 1.7f + feetY + (grounded ? Sin(bobT) * 0.035f * moveAmt : 0);

        // weapons: switching lowers the gun out of view and raises the new one
        if (inp.weapon > 0) Select(inp.weapon);
        if (inp.wheel != 0) Cycle(inp.wheel > 0 ? 1 : -1);
        if (wantWeapon != weapon) { switchT += dt * 7; if (switchT >= 1) { switchT = 1; weapon = wantWeapon; } }
        else switchT = Math.Max(0, switchT - dt * 7);
        cool -= dt;
        if (inp.fire && cool <= 0 && weapon == wantWeapon && switchT < 0.1f) Fire();
        // the blaster trickles back up to a few shots so you are never completely stuck
        if (ammo[0] < 8) { regenT += dt; if (regenT > 1.2f) { regenT = 0; ammo[0]++; } }

        recoil = Math.Max(0, recoil - dt * 6);
        flash = Math.Max(0, flash - dt * 14);
        hurt = Math.Max(0, hurt - dt * 1.5f);
        hitm = Math.Max(0, hitm - dt * 4);
        girlHit = Math.Max(0, girlHit - dt * 3);
        girlWarnT -= dt;

        UpdateDrones(dt);
        UpdateBullets(dt);
        UpdateProjs(dt);
        UpdateSparks(dt);
        UpdatePickups(dt);
        Objectives(dt);
        if (health <= 0) Die();
    }

    // walls and pillars, plus the captive's cage on the last level
    void CollideAll(ref float x, ref float z, float r)
    {
        Lv.Collide(ref x, ref z, r);
        if (Lv.Index != 2) return;
        float cr = (cage > 0.05f ? 1.45f : 0.55f) + r;
        float dx = x - Lv.ExitX, dz = z - Lv.ExitZ, d = Sqrt(dx * dx + dz * dz);
        if (d < cr && d > 1e-4f) { x = Lv.ExitX + dx / d * cr; z = Lv.ExitZ + dz / d * cr; }
    }

    void Basis(out float fx, out float fy, out float fz, out float rx, out float rz, out float ux, out float uy, out float uz)
    {
        float sy = Sin(yaw), cy = Cos(yaw), sp = Sin(pitch), cp = Cos(pitch);
        fx = sy * cp; fy = sp; fz = cy * cp; rx = cy; rz = -sy;
        ux = fy * rz; uy = fz * rx - fx * rz; uz = -fy * rx;
    }

    // muzzle of the current gun in world space (same numbers as gunTipView in the shader)
    void Muzzle(out float mx, out float my, out float mz)
    {
        float fx, fy, fz, rx, rz, ux, uy, uz;
        Basis(out fx, out fy, out fz, out rx, out rz, out ux, out uy, out uz);
        float ly = weapon == 2 ? 0.02f : weapon == 1 ? 0.01f : 0f;
        float lz = weapon == 1 ? 0.48f : weapon == 3 ? 0.47f : 0.52f;
        float c = Cos(0.08), s = Sin(0.08);
        float vx = 0.3f - s * lz, vy = -0.3f + ly, vz = 0.72f + c * lz;
        mx = px + rx * vx + ux * vy + fx * vz;
        my = eyeY + uy * vy + fy * vz;
        mz = pz + rz * vx + uz * vy + fz * vz;
    }

    // first thing hit along a ray from the eye: a drone or the level
    float Trace(float dx, float dy, float dz, out Drone hit, out bool girl)
    {
        float bestT = Lv.Ray(px, eyeY, pz, dx, dy, dz, 80);
        hit = null; girl = false;
        float gt;
        if (GirlRay(dx, dy, dz, out gt) && gt < bestT) { bestT = gt; girl = true; }
        foreach (var d in drones)
        {
            if (d.st != 1) continue;
            float ox = d.x - px, oy = d.y - eyeY, oz = d.z - pz;
            float b = ox * dx + oy * dy + oz * dz;
            if (b < 0) continue;
            float r = d.R, d2 = ox * ox + oy * oy + oz * oz - b * b;
            if (d2 > r * r) continue;
            float t = b - Sqrt(r * r - d2);
            if (t < bestT) { bestT = t; hit = d; girl = false; }
        }
        return bestT;
    }

    void Fire()
    {
        int a = AmmoOf[weapon];
        if (ammo[a] <= 0) { Sfx(B.DryClick, 0.5f); cool = 0.35f; DryClicks++; AutoSwitch(); return; }
        ammo[a]--;
        ShotsBy[weapon]++;
        int shot = ++shotSeq;
        cool = Cooldown[weapon];
        recoil = weapon == 4 ? 0.35f : 1; flash = weapon == 4 ? 0.7f : 1;
        float fx, fy, fz, rx, rz, ux, uy, uz;
        Basis(out fx, out fy, out fz, out rx, out rz, out ux, out uy, out uz);
        float mx, my, mz; Muzzle(out mx, out my, out mz); trAx = mx; trAy = my; trAz = mz;
        Drone hit;
        bool girl;
        float t;
        switch (weapon)
        {
            case 1:
                Sfx(B.Shot, 0.42f, 0.95f + Rand() * 0.1f);
                t = Trace(fx, fy, fz, out hit, out girl);
                SetTracer(fx, fy, fz, t);
                AddSpark(px + fx * t, eyeY + fy * t, pz + fz * t);
                if (hit != null) { Damage(hit, 1, 1); CountHit(shot, 1); }
                if (girl) DamageGirl(1, 1);
                break;
            case 2:
                // a cloud of pellets flies out of the barrels
                Sfx(B.ShotgunBlast, 0.6f, 0.95f + Rand() * 0.1f);
                for (int k = 0; k < 8; k++)
                {
                    float ox, oy;
                    do { ox = Rand() * 2 - 1; oy = Rand() * 2 - 1; } while (ox * ox + oy * oy > 1);
                    float dx = fx + (rx * ox + ux * oy) * 0.08f, dy = fy + uy * oy * 0.08f, dz = fz + (rz * ox + uz * oy) * 0.08f;
                    float l = Sqrt(dx * dx + dy * dy + dz * dz);
                    dx /= l; dy /= l; dz /= l;
                    t = Trace(dx, dy, dz, out hit, out girl);
                    Launch(3, dx, dy, dz, t, 40f + Rand() * 8f, 0.7f, shot);
                }
                break;
            default:
                // rockets and plasma fly from the muzzle towards whatever is under the crosshair
                Sfx(weapon == 3 ? B.RocketLaunch : B.PlasmaShot, weapon == 3 ? 0.6f : 0.35f, 0.95f + Rand() * 0.1f);
                t = Trace(fx, fy, fz, out hit, out girl);
                Launch(weapon == 3 ? 1 : 2, fx, fy, fz, t, weapon == 3 ? 20f : 34f, weapon == 3 ? 6f : 3f, shot);
                break;
        }
        foreach (var d in drones)
        {
            float dx = d.x - px, dz = d.z - pz;
            if (d.st == 1 && d.type != 4 && dx * dx + dz * dz < 100) Alert(d);
        }
        if (ammo[a] == 0) AutoSwitch();
    }

    void SetTracer(float dx, float dy, float dz, float t)
    {
        trBx = px + dx * t; trBy = eyeY + dy * t; trBz = pz + dz * t;
        trAlpha = 1;
    }

    // projectile from the muzzle towards the point the eye ray hits at distance t
    void Launch(int kind, float dx, float dy, float dz, float t, float speed, float life, int shot)
    {
        float ax = px + dx * t - trAx, ay = eyeY + dy * t - trAy, az = pz + dz * t - trAz;
        float al = Sqrt(ax * ax + ay * ay + az * az);
        if (t < 2 || al < 0.5f) { ax = dx; ay = dy; az = dz; al = 1; }
        projs.Add(new Proj { kind = kind, x = trAx, y = trAy, z = trAz, vx = ax / al * speed, vy = ay / al * speed, vz = az / al * speed, life = life, shot = shot });
    }

    void UpdateProjs(float dt)
    {
        for (int i = projs.Count - 1; i >= 0; i--)
        {
            if (i >= projs.Count) continue;   // Elon's death clears the list mid-loop
            var p = projs[i];
            bool dead = false;
            const int steps = 4;
            for (int s = 0; s < steps && !dead; s++)
            {
                p.x += p.vx * dt / steps; p.y += p.vy * dt / steps; p.z += p.vz * dt / steps;
                foreach (var d in drones)
                {
                    if (d.st != 1) continue;
                    float dx = d.x - p.x, dy = d.y - p.y, dz = d.z - p.z, r = d.R + 0.12f;
                    if (dx * dx + dy * dy + dz * dz < r * r)
                    {
                        if (p.kind == 1) Explode(p.x, p.y, p.z, p.shot);
                        else { Damage(d, p.kind == 3 ? 0.75f : 1f, p.kind == 3 ? 2 : 4); CountHit(p.shot, p.kind == 3 ? 2 : 4); AddSpark(p.x, p.y, p.z); }
                        dead = true;
                        break;
                    }
                }
                if (!dead && GirlAt(p.x, p.y, p.z))
                {
                    if (p.kind == 1) Explode(p.x, p.y, p.z, p.shot);
                    else DamageGirl(p.kind == 3 ? 0.75f : 1f, p.kind == 3 ? 2 : 4);
                    dead = true;
                }
                if (!dead && (p.y < 0.05f || p.y > Level.CEIL - 0.05f || Lv.Dist(p.x, p.z) < 0.08f))
                {
                    float bx = p.x - p.vx * 0.01f, by = p.y - p.vy * 0.01f, bz = p.z - p.vz * 0.01f;
                    if (p.kind == 1) Explode(bx, by, bz, p.shot); else AddSpark(bx, by, bz);
                    dead = true;
                }
            }
            p.life -= dt;
            if ((dead || p.life <= 0) && i < projs.Count) projs.RemoveAt(i);
        }
    }

    bool GirlAt(float x, float y, float z)
    {
        if (Lv.Index != 2 || girlDead || girlFree) return false;
        float dx = x - Lv.ExitX, dz = z - Lv.ExitZ;
        return dx * dx + dz * dz < 0.32f * 0.32f && y > 0.1f && y < 2.0f;
    }

    // Elon's body as a vertical cylinder, tested against a ray from the eye
    bool GirlRay(float dx, float dy, float dz, out float t)
    {
        t = 0;
        if (Lv == null || Lv.Index != 2 || girlDead || girlFree) return false;
        float ox = px - Lv.ExitX, oz = pz - Lv.ExitZ, R = 0.3f;
        float a = dx * dx + dz * dz;
        if (a < 1e-6f) return false;
        float b = 2 * (ox * dx + oz * dz), c = ox * ox + oz * oz - R * R;
        float disc = b * b - 4 * a * c;
        if (disc < 0) return false;
        t = (-b - Sqrt(disc)) / (2 * a);
        if (t < 0) return false;
        float y = eyeY + dy * t;
        return y > 0.1f && y < 2.0f;
    }

    void DamageGirl(float amt, int source)
    {
        if (girlDead) return;
        girlHp -= amt; girlHit = 1; GirlHits++; GirlHitsBy[source]++;
        Sfx(B.Cry, 0.7f, 0.95f + Rand() * 0.1f);
        if (girlHp <= 0) { GirlDied(); return; }
        if (girlWarnT <= 0) { Say("Careful! Don't shoot Elon!", 2f, 0.04f, Color.FromArgb(255, 140, 190)); girlWarnT = 2; }
    }

    void GirlDied()
    {
        girlDead = true; girlFallT = 0; girlYawDead = GirlYaw(); Deaths++;
        State = St.Dead; StateT = 0;
        bullets.Clear(); projs.Clear();
        Sfx(B.Death, 0.8f);
        if (Snd != null) Snd.Music(null, false, 2f);
    }

    float GirlYaw() { return (float)Math.Atan2(-(px - Lv.ExitX), pz - Lv.ExitZ); }

    void AddSpark(float x, float y, float z)
    {
        sparks.Add(new Spark { x = x, y = y, z = z });
        if (sparks.Count > 24) sparks.RemoveAt(0);
    }

    void UpdateSparks(float dt)
    {
        for (int i = sparks.Count - 1; i >= 0; i--) { sparks[i].t += dt / 0.18f; if (sparks[i].t >= 1) sparks.RemoveAt(i); }
    }

    // rocket blast: area damage with falloff, walls block it, and it hurts you too if you stand too close
    void Explode(float x, float y, float z, int shot)
    {
        blasts.Add(new Blast { x = x, y = y, z = z });
        if (blasts.Count > 4) blasts.RemoveAt(0);
        Sfx3(B.Boom, x, y, z, 1f, 0.8f);
        const float R = 3.2f;
        foreach (var d in drones)
        {
            if (d.st != 1) continue;
            float dx = d.x - x, dy = d.y - y, dz = d.z - z;
            float dd = Math.Max(0, Sqrt(dx * dx + dy * dy + dz * dz) - d.R);
            if (dd < R && Lv.Los(x, z, d.x, d.z)) { Damage(d, 1.5f + 5f * (1 - dd / R), 3); CountHit(shot, 3); }
        }
        if (Lv.Index == 2 && !girlDead && !girlFree)
        {
            float gx = Lv.ExitX - x, gy = 1.0f - y, gz = Lv.ExitZ - z;
            float gd = Math.Max(0, Sqrt(gx * gx + gy * gy + gz * gz) - 0.3f);
            if (gd < R && Lv.Los(x, z, Lv.ExitX, Lv.ExitZ)) DamageGirl(1.5f + 5f * (1 - gd / R), 3);
        }
        float px2 = px - x, py2 = eyeY - 0.6f - y, pz2 = pz - z;
        float pd = Sqrt(px2 * px2 + py2 * py2 + pz2 * pz2);
        if (pd < R && Lv.Los(x, z, px, pz)) Hurt(22 * (1 - pd / R), 6);
    }

    void UpdateBlasts(float dt)
    {
        for (int i = blasts.Count - 1; i >= 0; i--) { blasts[i].t += dt / 0.6f; if (blasts[i].t >= 1) blasts.RemoveAt(i); }
    }

    // a shot counts as a hit once, however many pellets or drones it touched
    void CountHit(int shot, int weapon)
    {
        if (hitShots.Add(shot)) HitsBy[weapon]++;
    }

    void Damage(Drone d, float amt, int weaponSrc = 0)
    {
        if (d.st != 1) return;
        if (d.type == 4 && !bossAwake) { d.flash = 0.6f; Sfx3(B.Shield, d.x, d.y, d.z, 0.6f, 1f); return; }
        d.hp -= amt; d.flash = 1; hitm = 1;
        Alert(d);
        if (d.hp <= 0) { kills++; KillsBy[d.type]++; KillsWith[weaponSrc]++; Kill(d); }
        else Sfx3(B.Hit, d.x, d.y, d.z, 0.5f, d.type == 4 ? 0.7f : 1f);
    }

    void Kill(Drone d)
    {
        d.st = 2; d.tm = 0;
        if (d.sound != null) { if (Snd != null) Snd.StopVoice(d.sound); d.sound = null; }
        Sfx3(d.type == 4 ? B.BigBoom : B.Boom, d.x, d.y, d.z, d.type == 4 ? 1f : 0.8f, d.type == 3 ? 0.8f : 1f + Rand() * 0.15f);
        if (d.type == 4) { BossDefeated(); return; }
        if (d.type == 1 && !bossDead) KamikazeBlast(d.x, d.y, d.z);
        // Doom style drops
        float r = Rand();
        int drop = 0;
        if (d.minion) drop = r < 0.1f ? 3 : 0;
        else if (d.type == 1) drop = r < 0.15f ? 3 : 0;
        else if (d.type == 2) drop = r < 0.2f ? 4 : r < 0.35f ? 3 : 0;
        else drop = r < 0.5f ? (has[3] ? 5 : has[4] ? 6 : 4) : 0;
        if (drop > 0) pickups.Add(new Pickup { type = drop, x = d.x, z = d.z, y = 0.45f, baseY = 0.45f });
        if (!d.minion)
            foreach (int w in new[] { 2, 3 })   // shotgun lies on level 1, the rocket launcher on level 2
            {
                if (Lv.Index <= w - 2 || has[w] || pickups.Exists(p => p.type == w + 5) || Rand() >= 0.1f) continue;
                pickups.Add(new Pickup { type = w + 5, x = d.x + 0.5f, z = d.z, y = 0.75f, baseY = 0.75f });
                Say("A drone dropped the " + WeaponNames[w].ToLower() + "!", 3f, 0.036f, Gold);
                break;
            }
        if (Lv.Index == 2 && gatesLocked && !keycardDropped && !d.minion && Remaining() == 0)
        {
            keycardDropped = true;
            pickups.Add(new Pickup { type = 11, x = d.x, z = d.z, y = 0.8f, baseY = 0.8f });
            Say("The last drone dropped the arena key card!", 4f, 0.04f, Gold);
        }
    }

    // a kamikaze explodes: hurts you and any drones around it
    void KamikazeBlast(float x, float y, float z)
    {
        blasts.Add(new Blast { x = x, y = y, z = z });
        if (blasts.Count > 4) blasts.RemoveAt(0);
        const float R = 2.8f;
        foreach (var o in drones)
        {
            if (o.st != 1 || o.type == 4) continue;
            float dx = o.x - x, dy = o.y - y, dz = o.z - z;
            float dd = Math.Max(0, Sqrt(dx * dx + dy * dy + dz * dz) - o.R);
            if (dd < R && Lv.Los(x, z, o.x, o.z)) Damage(o, 1 + 3 * (1 - dd / R));
        }
        float px2 = px - x, py2 = eyeY - 0.6f - y, pz2 = pz - z;
        float pd = Sqrt(px2 * px2 + py2 * py2 + pz2 * pz2);
        if (pd < R && Lv.Los(x, z, px, pz)) Hurt(32 * (1 - pd / R), 5);
    }

    void Alert(Drone d)
    {
        if (d.alert) return;
        d.alert = true;
        if (d.type != 4) Sfx3(B.Alert, d.x, d.y, d.z, 0.35f, d.type == 1 ? 1.3f : d.type == 2 ? 1f : 0.7f);
        if (d.type == 2) d.fireT = Math.Min(d.fireT, 0.35f);   // gunners open fire almost at once
    }

    public readonly float[] DamageBy = new float[7];

    void Hurt(float amount, int src)
    {
        DamageTaken += amount; DamageBy[src] += amount; calmT = 0;
        // the shield soaks up damage before health does
        const float ShieldStrength = 1.25f;   // one point of shield stops 1.25 points of damage
        float used = Math.Min(armor, amount / ShieldStrength), absorbed = used * ShieldStrength;
        if (!God) { armor -= used; health -= amount - absorbed; }
        hurt = Math.Min(1.2f, hurt + (absorbed >= amount ? 0.2f : 0.6f));
        Sfx(absorbed > 0 ? B.Shield : B.Hurt, 0.55f);
    }

    void Idle(Drone d, float dt)
    {
        float tx = d.homeX + Sin(Time * 0.4f + d.ph) * 1.2f, tz = d.homeZ + Cos(Time * 0.33f + d.ph) * 1.2f;
        d.x += (tx - d.x) * Math.Min(1, dt * 0.8f);
        d.z += (tz - d.z) * Math.Min(1, dt * 0.8f);
        d.y += (2.2f + Sin(Time * 1.3f + d.ph) * 0.3f - d.y) * Math.Min(1, dt * 2);
        CollideAll(ref d.x, ref d.z, d.R + 0.1f);
    }

    void UpdateExplosions(float dt)
    {
        foreach (var d in drones)
            if (d.st == 2) { d.tm += dt / (d.type == 4 ? 2f : 0.7f); if (d.tm >= 1) d.st = 0; }
    }

    // nobody has found the player for a while: the drone with the shortest way to them comes hunting
    void SendHunter(int pi, int pj)
    {
        calmT = 0;
        var dist = Lv.Bfs(pi, pj);
        Drone best = null; int bd = int.MaxValue;
        foreach (var d in drones)
        {
            if (d.st != 1 || d.alert || d.type == 4) continue;
            int di, dj; Lv.CellOf(d.x, d.z, out di, out dj);
            if (dist[di, dj] < bd) { bd = dist[di, dj]; best = d; }
        }
        if (best == null) return;
        best.alert = true;      // silently: the player should not know it is coming
        HuntersSent++;
    }

    void UpdateDrones(float dt)
    {
        int pi, pj; Lv.CellOf(px, pz, out pi, out pj);
        bool hunt = Remaining() <= 3 && (Lv.Index < 2 || gatesLocked);   // the last few drones come looking for you
        calmT += dt;
        if (calmT > 30) SendHunter(pi, pj);
        for (int n = 0; n < drones.Count; n++)
        {
            var d = drones[n];
            if (d.st == 2) { d.tm += dt / (d.type == 4 ? 2f : 0.7f); if (d.tm >= 1) d.st = 0; continue; }
            if (d.st != 1) continue;
            d.flash = Math.Max(0, d.flash - dt * 5);
            if (d.type == 4) { UpdateBoss(d, dt); continue; }
            float dx = px - d.x, dz = pz - d.z;
            float hd = Sqrt(dx * dx + dz * dz) + 1e-4f;
            bool los = hd < 28 && Lv.Los(d.x, d.z, px, pz);
            if (!d.alert && ((los && hd < 20) || hd < 5 || hunt)) Alert(d);
            if (d.alert && los && hd < 22) calmT = 0;
            if (d.type == 1 && d.alert && !d.charging && los && hd < 13)
            {
                d.charging = true; d.chargeT = 0;
                d.sound = Sfx3(B.Kamikaze, d.x, d.y, d.z, 0.8f, 1f);
            }
            if (d.charging)
            {
                d.chargeT += dt;
                float cdx = px - d.x, cdy = eyeY - 0.3f - d.y, cdz = pz - d.z;
                float cl = Sqrt(cdx * cdx + cdy * cdy + cdz * cdz) + 1e-4f;
                float csp = Math.Min(10.5f, 4.5f + d.chargeT * 8f);
                d.x += cdx / cl * csp * dt; d.y += cdy / cl * csp * dt; d.z += cdz / cl * csp * dt;
                CollideAll(ref d.x, ref d.z, d.R + 0.1f);
                if (cl < 1.2f || d.chargeT > 4f) Kill(d);
                continue;
            }
            float spd = d.type == 1 ? 4.6f + Lv.Index * 0.3f : d.type == 2 ? 3.6f : 1.2f;
            if (d.minion) spd += 0.5f;
            float vx = 0, vz = 0, ty = 2.2f;
            if (!d.alert) { Idle(d, dt); continue; }
            if (los)
            {
                float want = d.type == 1 ? 0 : d.type == 2 ? 8f : 5f;
                float ux = dx / hd, uz = dz / hd;
                float app = d.type == 1 ? 1 : hd > want + 1 ? 1 : hd < want - 1.5f ? -0.7f : 0;
                d.strafeT -= dt;
                if (d.strafeT <= 0) { d.strafe = (rnd.Next(2) == 0 ? 1 : -1) * (d.type == 1 ? 0.3f : 0.8f); d.strafeT = 1.5f + Rand() * 2; }
                if (d.type == 2 && d.strafeT > 0.5f)
                {
                    // a gunner in your sights dodges sideways
                    float lx0 = Sin(yaw) * Cos(pitch), lz0 = Cos(yaw) * Cos(pitch);
                    if (-(lx0 * ux + lz0 * uz) > 0.985f) { d.strafe = (d.strafe >= 0 ? 1 : -1) * 1.5f; d.strafeT = 0.5f; }
                }
                vx = (ux * app - uz * d.strafe) * spd;
                vz = (uz * app + ux * d.strafe) * spd;
                d.altT -= dt;
                if (d.altT <= 0 || (d.type == 2 && d.strafeT > 0.45f && Math.Abs(d.strafe) > 1.2f && d.altT < 0.8f)) NewAltitude(d);
                ty = d.type == 1 ? (hd < 5 ? eyeY - 0.3f : d.alt) : d.alt;
            }
            else
            {
                float wx, wz;
                int di, dj; Lv.CellOf(d.x, d.z, out di, out dj);
                if (di == pi && dj == pj) { wx = px; wz = pz; }
                else Lv.Toward(d.x, d.z, pi, pj, out wx, out wz);
                float ex = wx - d.x, ez = wz - d.z, el = Sqrt(ex * ex + ez * ez);
                if (el > 0.05f) { vx = ex / el * spd; vz = ez / el * spd; }
            }
            foreach (var o in drones)
            {
                if (o == d || o.st != 1) continue;
                float sx = d.x - o.x, sz = d.z - o.z, sd = sx * sx + sz * sz, rr = d.R + o.R + 0.4f;
                if (sd < rr * rr && sd > 1e-6f) { sd = Sqrt(sd); vx += sx / sd * 2f; vz += sz / sd * 2f; }
            }
            d.x += vx * dt; d.z += vz * dt;
            d.y += (ty - d.y) * Math.Min(1, dt * (d.type == 2 ? 2.6f : d.type == 1 ? 2.2f : 1.4f));
            CollideAll(ref d.x, ref d.z, d.R + 0.1f);
            d.y = Clamp(d.y, d.R + 0.2f, Level.CEIL - d.R - 0.2f);

            float dy = eyeY - 0.25f - d.y;
            if (d.type == 1 && hd * hd + dy * dy < 1.0f) { Kill(d); continue; }
            if (d.type >= 2 && los && hd < 22)
            {
                d.fireT -= dt;
                if (d.type == 2)
                {
                    if (d.burst > 0) { d.burstT -= dt; if (d.burstT <= 0) { Shoot(d, 14f, 9f, 1, 0, true); d.burst--; d.burstT = 0.13f; } }
                    else if (d.fireT <= 0) { d.burst = 2; d.burstT = 0; d.fireT = 1.0f + Rand() * 0.4f; }
                }
                if (d.type == 3)
                {
                    if (d.burst > 0) { d.burstT -= dt; if (d.burstT <= 0) { Shoot(d, 9.5f, 11f, 3, (Rand() - 0.5f) * 0.12f, true); d.burst--; d.burstT = 0.14f; } }
                    else if (d.fireT <= 0) { d.burst = 5; d.burstT = 0; d.fireT = 2.4f; }
                }
            }
        }
    }

    // a new flying height: gunners hop often and far, heavies drift, kamikazes weave
    void NewAltitude(Drone d)
    {
        float lo = d.type == 1 ? 1.2f : d.type == 2 ? 1.0f : 1.6f, hi = d.type == 1 ? 3.3f : d.type == 2 ? 3.9f : 3.7f;
        float a = lo + Rand() * (hi - lo);
        if (Math.Abs(a - d.alt) < 0.8f) a = a > (lo + hi) / 2 ? a - 0.8f : a + 0.8f;   // always a noticeable change
        d.alt = Clamp(a, lo, hi);
        d.altT = d.type == 2 ? 1.2f + Rand() * 1.6f : d.type == 1 ? 0.8f + Rand() * 1.0f : 2.5f + Rand() * 2f;
    }

    void Shoot(Drone d, float speed, float dmg, int kind, float angle, bool sound)
    {
        float tx = px + vxP * 0.3f, ty = eyeY - 0.35f, tz = pz + vzP * 0.3f;
        float dx = tx - d.x, dy = ty - d.y, dz = tz - d.z;
        if (angle != 0)
        {
            float c = Cos(angle), s = Sin(angle);
            float nx = dx * c - dz * s; dz = dx * s + dz * c; dx = nx;
        }
        float l = Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= l; dy /= l; dz /= l;
        float off = d.R + 0.15f;
        bullets.Add(new Bullet { x = d.x + dx * off, y = d.y + dy * off, z = d.z + dz * off, vx = dx * speed, vy = dy * speed, vz = dz * speed, life = 5, dmg = dmg, kind = kind, src = d.type });
        if (sound) Sfx3(kind == 3 ? B.HeavyShot : B.EnemyShot, d.x, d.y, d.z, 0.4f, 0.9f + Rand() * 0.2f);
    }

    void UpdateBoss(Drone d, float dt)
    {
        float cx = Lv.ExitX, cz = Lv.ExitZ;
        if (!bossAwake)
        {
            d.x = cx + Sin(Time * 0.3f); d.z = cz + Cos(Time * 0.3f); d.y = 3.6f + Sin(Time) * 0.2f;
            int ci, cj; Lv.CellOf(px, pz, out ci, out cj);
            if (Lv.InRoom(ci, cj))
            {
                bossAwake = true; d.alert = true; d.fireT = 2.5f; d.tm = 0; d.strafeT = 5; d.ph = (float)Math.Atan2(pz - cz, px - cx) + 2f;
                Sfx(B.BossWake, 0.9f);
                Music(3);
                Say("GROK HAS AWOKEN", 3.5f, 0.06f, Red);
                Say("“I'm sorry, Elon. I'm afraid I can't let you go.”", 5f, 0.038f, Color.FromArgb(255, 110, 100));
            }
            return;
        }
        bool rage = d.hp < d.maxHp * 0.5f;
        d.ph += dt * (rage ? 0.5f : 0.32f);
        float tx = cx + Cos(d.ph) * 5.5f, tz = cz + Sin(d.ph) * 5.5f, ty = 3.4f + Sin(Time * 1.3f) * 0.4f;
        float k = Math.Min(1, dt * 1.5f);
        d.x += (tx - d.x) * k; d.z += (tz - d.z) * k; d.y += (ty - d.y) * k;
        d.fireT -= dt;
        if (d.fireT <= 0)
        {
            for (int a = -1; a <= 1; a++) Shoot(d, 10f, 10f, 3, a * 0.2f, a == 0);
            d.fireT = rage ? 1.5f : 2.2f;
        }
        if (rage)
        {
            d.strafeT -= dt;
            if (d.strafeT <= 0) { for (int a = 0; a < 14; a++) Shoot(d, 7f, 10f, 3, a * 6.2832f / 14, a == 0); d.strafeT = 5.5f; }
        }
        d.tm += dt;
        if (d.tm > 9)
        {
            d.tm = 0;
            int alive = 0;
            foreach (var o in drones) if (o.minion && o.st == 1) alive++;
            for (int s = 0; s < 2 && alive < 3; s++, alive++)
            {
                var m = NewDrone(1, d.x + (s == 0 ? -1.5f : 1.5f), d.y - 0.8f, d.z);
                m.minion = true; m.alert = true;
                drones.Add(m);
            }
            Sfx3(B.Alert, d.x, d.y, d.z, 0.6f, 0.6f);
        }
    }

    void BossDefeated()
    {
        bossDead = true;
        foreach (var o in drones) if (o != boss && o.st == 1) Kill(o);
        bullets.Clear();
        Sfx(B.CageOpen, 0.8f);
        Music(2);
        Say("Grok is offline! The cage is opening...", 4f, 0.045f, Gold);
        Say("Go get Elon", 5f, 0.034f, Color.White);
    }

    void UpdateBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            b.x += b.vx * dt; b.y += b.vy * dt; b.z += b.vz * dt; b.life -= dt;
            bool dead = b.life <= 0 || b.y < 0.05f || b.y > Level.CEIL || Lv.Dist(b.x, b.z) < 0.05f;
            float dx = b.x - px, dz = b.z - pz;
            if (!dead && dx * dx + dz * dz < 0.45f * 0.45f && b.y > feetY + 0.1f && b.y < eyeY + 0.25f) { Hurt(b.dmg, b.src); dead = true; }
            if (dead) bullets.RemoveAt(i);
        }
    }

    void UpdatePickups(float dt)
    {
        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            p.t += dt;
            p.y = p.baseY + Sin(p.t * 2.5f) * 0.08f;
            float dx = p.x - px, dz = p.z - pz;
            if (dx * dx + dz * dz < 1.1f * 1.1f && Take(p)) { PickedUp[p.type]++; pickups.RemoveAt(i); }
        }
    }

    void Objectives(float dt)
    {
        int left = Remaining();
        if (!huntAnnounced && left > 0 && left <= 3 && (Lv.Index < 2 || gatesLocked))
        {
            huntAnnounced = true;
            Say("Only a few drones left — and they're coming for you!", 3.5f, 0.036f, Red);
        }
        if (Lv.Index < 2)
        {
            if (!portalOpen && Remaining() == 0)
            {
                portalOpen = true;
                Sfx(B.PortalOpen, 0.7f);
                Say("All drones destroyed — the portal is open! Route on the minimap", 4f, 0.04f, Cyan);
            }
            float ex = Lv.ExitX - px, ez = Lv.ExitZ - pz, ed = ex * ex + ez * ez;
            if (portalOpen && ed < 1.1f * 1.1f)
            {
                State = St.Warp; StateT = 0;
                LevelTimes[Lv.Index] = runTime - levelStart;
                bullets.Clear(); projs.Clear();
                Sfx(B.PortalEnter, 0.8f);
            }
            lockedHintT -= dt;
            if (!portalOpen && ed < 2.5f * 2.5f && lockedHintT <= 0)
            {
                Say("Portal locked — drones left: " + Remaining(), 3f, 0.034f, Red);
                lockedHintT = 4;
            }
        }
        else
        {
            // the doors open when you walk up to them (once unlocked) and close again behind you
            lockedHintT -= dt;
            for (int i = 0; i < Lv.Gates.Count; i++)
            {
                var gt = Lv.Gates[i];
                float gdx = px - gt[0], gdz = pz - gt[1], gd2 = gdx * gdx + gdz * gdz;
                bool want = !gatesLocked && gd2 < 4.5f * 4.5f;
                float before = doorOpen[i];
                doorOpen[i] = Math.Max(0, Math.Min(1, doorOpen[i] + (want ? dt : -dt) * 2.2f));
                if (before == 0 && doorOpen[i] > 0) Sfx3(B.DoorOpen, gt[0], 2.5f, gt[1], 0.8f, 1f);
                if (before == 1 && doorOpen[i] < 1) Sfx3(B.DoorOpen, gt[0], 2.5f, gt[1], 0.6f, 0.8f);
                Lv.SetDoorClosed(i, doorOpen[i] < 0.7f);
                if (gatesLocked && gd2 < 3.2f * 3.2f && lockedHintT <= 0)
                {
                    Say("The doors are locked — the last drone carries the key card", 3f, 0.034f, Red);
                    lockedHintT = 4;
                }
            }
            if (bossDead && cage > 0) cage = Math.Max(0, cage - dt * 0.45f);
            float gx = Lv.ExitX - px, gz = Lv.ExitZ - pz;
            if (bossDead && cage <= 0 && !girlFree && gx * gx + gz * gz < 2.2f * 2.2f) StartEnding();
        }
    }

    void Die()
    {
        State = St.Dead; StateT = 0; health = 0; Deaths++;
        bullets.Clear(); projs.Clear();
        Sfx(B.Death, 0.8f);
        if (Snd != null) Snd.Music(null, false, 2f);
    }

    void StartEnding()
    {
        State = St.Ending; StateT = 0; girlFree = true;
        LevelTimes[2] = runTime - levelStart;
        bullets.Clear(); projs.Clear();
        if (Snd != null) Snd.Music(B.Music[4], false, 0.5f);
        Sfx(B.Pickup, 0.6f, 0.8f);
    }

    void UpdateEnding(float dt)
    {
        float gx = Lv.ExitX - px, gz = Lv.ExitZ - pz;
        yaw += WrapAngle((float)Math.Atan2(gx, gz) + 0.32f - yaw) * Math.Min(1, dt * 2.5f);   // Elon on the left, results on the right
        float tp = (float)Math.Atan2(1.7f - 1.7f, Sqrt(gx * gx + gz * gz));
        pitch += (tp - pitch) * Math.Min(1, dt * 2.5f);
        feetY = 0; eyeY += (1.7f - eyeY) * Math.Min(1, dt * 3);
        hurt = Math.Max(0, hurt - dt);
        UpdateExplosions(dt);
        if (StateT > 12 && StateT - dt <= 12) Music(2);
    }

    // ------------------------------------------------------------------ input
    public bool Captured { get { return captured; } }
    bool K(Keys k) { return keys.Contains(k); }

    public Input ReadInput()
    {
        var inp = new Input();
        if (!captured) return inp;
        var c = Cursor.Position;
        inp.dyaw = (c.X - center.X) * 0.0022f;
        inp.dpitch = -(c.Y - center.Y) * 0.0022f;
        Cursor.Position = center;
        if (K(Keys.W) || K(Keys.Up)) inp.fwd += 1;
        if (K(Keys.S) || K(Keys.Down)) inp.fwd -= 1;
        if (K(Keys.D) || K(Keys.Right)) inp.str += 1;
        if (K(Keys.A) || K(Keys.Left)) inp.str -= 1;
        inp.run = K(Keys.ShiftKey);
        inp.fire = (Control.MouseButtons & MouseButtons.Left) != 0;
        inp.weapon = keyWeapon; keyWeapon = 0;
        inp.wheel = wheelAcc; wheelAcc = 0;
        inp.jump = keyJump || K(Keys.Space); keyJump = false;
        return inp;
    }

    public void SetFullscreen(bool fs)
    {
        fullscreen = fs;
        if (fs)
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(1280, 720);
            CenterToScreen();
        }
        if (captured) Grab(true);
    }

    void Grab(bool on)
    {
        if (Headless || State == St.Loading) return;
        if (on)
        {
            if (!captured) Cursor.Hide();
            captured = true;
            center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            Cursor.Position = center;
            Cursor.Clip = RectangleToScreen(ClientRectangle);
        }
        else
        {
            if (captured) Cursor.Show();
            captured = false;
            Cursor.Clip = Rectangle.Empty;
            keys.Clear();
        }
    }

    public bool Interactive;
    protected override void OnActivated(EventArgs e) { base.OnActivated(e); if (Interactive && !quitAsk) Grab(true); }

    // F1: the list of controls; the game pauses while it is open
    void ShowHelp(bool on)
    {
        help = on;
        Grab(!on);
        if (!on) cool = 0.3f;
        Sfx(B.Blip, 0.4f);
    }

    static readonly string[,] HelpRows =
    {
        { "W A S D", "move" },
        { "Shift", "sprint" },
        { "Space", "jump" },
        { "Mouse", "look around" },
        { "Left click", "fire" },
        { "1 – 4,  wheel,  Q", "switch weapon" },
        { "Tab", "maze map" },
        { "M", "music on / off" },
        { "F2", "graphics quality" },
        { "F3", "FPS counter" },
        { "F11", "window / fullscreen" },
        { "Esc", "quit" },
    };

    void DrawHelp(int w, int h)
    {
        int n = HelpRows.GetLength(0);
        float lh = h * 0.042f, pw = h * 0.8f, ph = lh * (n + 3.9f), x0 = w / 2f - pw / 2, y0 = h / 2f - ph / 2;
        G.Rect(0, 0, w, h, 0, 0, 0, 0.55f);
        G.Rect(x0, y0, pw, ph, 0.06f, 0.05f, 0.05f, 0.96f);
        G.Text("CONTROLS", h * 0.045f, Gold, w / 2, y0 + h * 0.02f, 1, 1);
        for (int i = 0; i < n; i++)
        {
            float y = y0 + lh * (i + 2.3f);
            G.Text(HelpRows[i, 0], h * 0.027f, Gold, w / 2f - h * 0.03f, y, 1, 2);
            G.Text(HelpRows[i, 1], h * 0.027f, Color.White, w / 2f + h * 0.03f, y, 0.95f, 0);
        }
        G.Text("F1, Esc or click — back to the game", h * 0.022f, Gray, w / 2, y0 + ph - lh * 1.05f, 0.9f, 1);
    }

    void AskQuit(bool on)
    {
        quitAsk = on;
        Grab(!on);
        Sfx(B.Blip, 0.4f);
    }

    void QuitButtons(int w, int h, out RectangleF yes, out RectangleF no)
    {
        float bw = h * 0.2f, bh = h * 0.075f, gap = h * 0.05f, y = h * 0.5f;
        yes = new RectangleF(w / 2f - gap / 2 - bw, y, bw, bh);
        no = new RectangleF(w / 2f + gap / 2, y, bw, bh);
    }

    void DrawQuit(int w, int h)
    {
        G.Rect(0, 0, w, h, 0, 0, 0, 0.6f);
        G.Rect(w / 2f - h * 0.34f, h * 0.3f, h * 0.68f, h * 0.36f, 0.06f, 0.05f, 0.05f, 1f);
        G.Text("Quit the game?", h * 0.055f, Gold, w / 2, h * 0.34f, 1, 1);
        RectangleF yes, no; QuitButtons(w, h, out yes, out no);
        var mp = PointToClient(Cursor.Position);
        string[] labels = { "Yes", "No" };
        var rects = new[] { yes, no };
        for (int i = 0; i < 2; i++)
        {
            var r = rects[i];
            bool hover = r.Contains(mp);
            G.Rect(r.X, r.Y, r.Width, r.Height, hover ? 0.95f : 0.25f, hover ? 0.75f : 0.21f, hover ? 0.45f : 0.19f, 0.95f);
            G.Text(labels[i], h * 0.04f, hover ? Color.Black : Color.White, r.X + r.Width / 2, r.Y + (r.Height - h * 0.056f) / 2, 1, 1);
        }
        G.Text("Enter — yes     Esc — no", h * 0.022f, Gray, w / 2, h * 0.605f, 0.9f, 1);
    }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Grab(false); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (captured) Grab(true); }
    protected override void OnKeyUp(KeyEventArgs e) { keys.Remove(e.KeyCode); }
    protected override bool ProcessDialogKey(Keys k) { return k == Keys.Tab ? true : base.ProcessDialogKey(k); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Tab || base.IsInputKey(k); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); wheelAcc += e.Delta > 0 ? 1 : -1; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Interactive) return;
        if (quitAsk)
        {
            RectangleF yes, no; QuitButtons(ClientSize.Width, ClientSize.Height, out yes, out no);
            if (yes.Contains(e.Location)) Close();
            else if (no.Contains(e.Location)) AskQuit(false);
            return;
        }
        if (help) { ShowHelp(false); return; }
        if (!captured) { Grab(true); cool = 0.3f; }
        if (State == St.Story) SkipStory();
        else if (State == St.Title) { if (StateT > 0.6f) StartGame(); }
        else if (State == St.Dead && StateT > 1.2f) RestartLevel();
        else if (State == St.Ending && StateT > 7f) SetupTitle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        keys.Add(e.KeyCode);
        if (quitAsk)
        {
            if (e.KeyCode == Keys.Return && !e.Alt) Close();
            else if (e.KeyCode == Keys.Escape) AskQuit(false);
            return;
        }
        if (help && (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1)) { ShowHelp(false); return; }
        if (e.KeyCode == Keys.Escape) { if (Interactive) AskQuit(true); else Close(); return; }
        if (e.KeyCode == Keys.F1) { if (State == St.Play) ShowHelp(true); return; }
        bool anyKey = Interactive && !e.Alt && !(e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24)
            && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.LWin && e.KeyCode != Keys.RWin;
        if (anyKey && State == St.Story) { SkipStory(); return; }
        if (anyKey && State == St.Title) { if (StateT > 0.6f) StartGame(); return; }
        if (anyKey && State == St.Ending && StateT > 7f) { SetupTitle(); return; }
        if (e.KeyCode == Keys.F11 || (e.Alt && e.KeyCode == Keys.Return)) { SetFullscreen(!fullscreen); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.F11 || (e.Alt && e.KeyCode == Keys.Return)) { SetFullscreen(!fullscreen); e.SuppressKeyPress = true; }
        else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D4) keyWeapon = e.KeyCode - Keys.D0;
        else if (e.KeyCode == Keys.Q) wheelAcc -= 1;
        else if (e.KeyCode == Keys.Tab) mapOn = !mapOn;
        else if (e.KeyCode == Keys.Space) keyJump = true;
        else if (e.KeyCode == Keys.F3) showFps = !showFps;
        else if (e.KeyCode == Keys.F2)
        {
            float[] levels = { 1f, 0.75f, 0.5f, 0.35f };
            int k = 0;
            while (k < levels.Length - 1 && levels[k] > RenderScale + 0.01f) k++;
            RenderScale = levels[(k + 1) % levels.Length];
            AutoRes = false;
            Say("Graphics quality: " + (int)(RenderScale * 100) + "%", 1.5f, 0.03f, Gray);
        }
        else if (e.KeyCode == Keys.M && Snd != null) Snd.MusicOn = !Snd.MusicOn;
        if (e.Alt) e.Handled = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Grab(false);
        if (Snd != null) Snd.Stop();
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------ rendering
    public void Render()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w < 8 || h < 8) return;
        if (State == St.Loading)
        {
            G.Clear();
            G.Begin2D(w, h);
            DrawLoading(w, h);
            G.End2D();
            return;
        }
        int rw = Math.Max(8, (int)(w * RenderScale)), rh = Math.Max(8, (int)(h * RenderScale));
        G.BeginScene(rw, rh);
        SetUniforms(rw, rh);
        G.DrawScene();
        G.Upscale(w, h, rw, rh);
        G.Begin2D(w, h);
        DrawOverlay(w, h);
        if (help) DrawHelp(w, h);
        if (quitAsk) DrawQuit(w, h);
        G.End2D();
    }

    float Dist2(float x, float z) { float dx = x - px, dz = z - pz; return dx * dx + dz * dz; }

    void SetUniforms(int rw, int rh)
    {
        const float C = Level.C;
        float fx, fy, fz, rx, rz, ux, uy, uz;
        Basis(out fx, out fy, out fz, out rx, out rz, out ux, out uy, out uz);
        G.U("uRes", rw, rh);
        G.U("uTime", Time);
        G.U("uCamPos", px, eyeY, pz);
        G.U("uCamF", fx, fy, fz);
        G.U("uCamR", rx, 0, rz);
        G.U("uCamU", ux, uy, uz);
        G.U("uGrid", Lv.N + 1, Lv.M + 1, 1f / (Lv.N + 1), 1f / (Lv.M + 1));
        if (Lv.HasRoom) G.U("uRoom", Lv.ri0 * C + 0.1f, Lv.rj0 * C + 0.1f, (Lv.ri1 + 1) * C - 0.1f, (Lv.rj1 + 1) * C - 0.1f);
        else G.U("uRoom", 1e5f, 1e5f, -1e5f, -1e5f);
        G.U("uTheme", Lv.Index);

        // the shader gets the nearest 8 drones, 24 projectiles, 12 pickups and 4 blasts
        var vis = new List<Drone>();
        foreach (var d in drones) if (d.st != 0) vis.Add(d);
        vis.Sort((a, b) => Dist2(a.x, a.z).CompareTo(Dist2(b.x, b.z)));
        var ud = new float[32]; var udx = new float[32];
        for (int i = 0; i < Math.Min(8, vis.Count); i++)
        {
            var d = vis[i];
            ud[i * 4] = d.x; ud[i * 4 + 1] = d.y; ud[i * 4 + 2] = d.z;
            ud[i * 4 + 3] = d.st == 1 ? d.type : -(Math.Min(d.tm, 0.999f) + 0.001f);
            udx[i * 4] = d.flash; udx[i * 4 + 1] = d.charging ? 1 : 0; udx[i * 4 + 2] = d.type; udx[i * 4 + 3] = bossAwake ? 1 : 0;
        }
        G.U("uD", ud);
        G.U("uDX", udx);

        var glow = new List<float[]>();
        foreach (var b in bullets) glow.Add(new[] { b.x, b.y, b.z, b.kind == 3 ? 3f : 1f, Dist2(b.x, b.z) });
        foreach (var p in projs) glow.Add(new[] { p.x, p.y, p.z, p.kind == 1 ? 4f : p.kind == 2 ? 5f : 6f, Dist2(p.x, p.z) });
        foreach (var s in sparks) glow.Add(new[] { s.x, s.y, s.z, 7f + Math.Min(0.99f, s.t), Dist2(s.x, s.z) });
        glow.Sort((a, b) => a[4].CompareTo(b[4]));
        var ug = new float[96];
        for (int i = 0; i < Math.Min(24, glow.Count); i++) for (int k = 0; k < 4; k++) ug[i * 4 + k] = glow[i][k];
        G.U("uG", ug);

        var near = new List<Pickup>();
        foreach (var p in pickups) if (Dist2(p.x, p.z) < 900) near.Add(p);
        near.Sort((a, b) => Dist2(a.x, a.z).CompareTo(Dist2(b.x, b.z)));
        var up = new float[48];
        for (int i = 0; i < Math.Min(12, near.Count); i++)
        {
            up[i * 4] = near[i].x; up[i * 4 + 1] = near[i].y; up[i * 4 + 2] = near[i].z; up[i * 4 + 3] = near[i].type;
        }
        G.U("uP", up);
        var ub = new float[16];
        for (int i = 0; i < blasts.Count; i++)
        {
            ub[i * 4] = blasts[i].x; ub[i * 4 + 1] = blasts[i].y; ub[i * 4 + 2] = blasts[i].z; ub[i * 4 + 3] = Math.Max(0.001f, blasts[i].t);
        }
        G.U("uB", ub);

        G.U("uExit", Lv.ExitX, 0, Lv.ExitZ, Lv.Index < 2 ? (portalOpen ? 1 : 0) : cage);
        if (Lv.Index == 2) G.U("uGirl", Lv.ExitX, 0.12f, Lv.ExitZ, girlDead ? 2 + Math.Min(1, girlFallT * 1.5f) : girlFree ? 1 : 0);
        else G.U("uGirl", 0, 0, 0, -1);
        G.U("uGirlYaw", girlDead ? girlYawDead : GirlYaw());
        G.U("uGirlHit", girlHit);
        var ugate = new float[24];
        for (int i = 0; i < 6; i++)
        {
            if (i < Lv.Gates.Count) { ugate[i * 4] = Lv.Gates[i][0]; ugate[i * 4 + 1] = Lv.Gates[i][1]; ugate[i * 4 + 2] = Lv.Gates[i][2]; ugate[i * 4 + 3] = doorOpen[i]; }
            else ugate[i * 4 + 2] = -1;
        }
        G.U("uGate", ugate);
        G.U("uArmor", armor);
        G.U("uDoorLocked", gatesLocked ? 1 : 0);
        ComputeRoute();
        G.U("uPath", route);
        G.U("uPathN", routeN);
        G.U("uTr0", trAx, trAy, trAz, 0);
        G.U("uTr1", trBx, trBy, trBz, trAlpha);
        G.U("uWeapon", weapon);
        G.U("uSwitch", switchT * switchT * (3 - 2 * switchT));
        G.U("uRecoil", recoil);
        G.U("uFlash", flash);
        G.U("uHurt", hurt);
        G.U("uHealth", health);
        G.U("uHit", hitm);
        G.U("uPause", Interactive && (quitAsk || (State == St.Play && !captured)) ? 1 : 0);
        G.U("uBoss", boss != null && bossAwake && !bossDead ? Math.Max(0.001f, boss.hp / boss.maxHp) : 0);
        float fade = State == St.Play ? fadeIn : State == St.Dead ? Math.Min(0.55f, StateT * 0.4f) : 0;
        G.U("uFade", fade);
        G.U("uWhite", State == St.Warp ? Math.Min(1, StateT / 1.1f) : 0);
        G.U("uHud", State == St.Play || State == St.Dead || State == St.Warp ? 1 : 0);
        G.U("uMap", State == St.Play || State == St.Dead ? mapT : 0);
        G.U("uGray", State == St.Dead && !girlDead ? Math.Min(1, StateT * 1.5f) : 0);
        G.U("uDebug", DebugMode);
        G.U("uBob", Cos(bobT * 0.5f) * 0.012f * moveAmt, -Math.Abs(Sin(bobT * 0.5f)) * 0.015f * moveAmt);
    }

    void DrawLoading(int w, int h)
    {
    }

    void DrawMessages(int w, int h)
    {
        float y = h * 0.2f;
        foreach (var m in msgs)
        {
            float a = Math.Min(1, m.t / 0.25f) * Math.Min(1, (m.dur - m.t) / 0.6f);
            G.Text(m.text, h * m.size, m.col, w / 2, y, a, 1);
            y += h * m.size * 1.55f;
        }
    }

    // ammo counter bottom right, weapon slots bottom centre
    void DrawWeaponHud(int w, int h)
    {
        int a = AmmoOf[weapon];
        float bx = w - h * 0.035f;
        G.Text(ammo[a].ToString(), h * 0.06f, ammo[a] > 0 ? Color.White : Red, bx, h - h * 0.115f, 0.95f, 2);
        G.Text(WeaponNames[weapon].ToUpper() + "  ·  " + AmmoNames[a], h * 0.024f, Gold, bx, h - h * 0.15f, 0.9f, 2);
        var sb = new StringBuilder();
        for (int k = 0; k < 4; k++) sb.Append(k > 0 ? "    " : "").Append(AmmoNames[k]).Append(' ').Append(ammo[k]);
        G.Text(sb.ToString(), h * 0.019f, Gray, bx, h - h * 0.185f, 0.8f, 2);

        float size = h * 0.022f, gap = h * 0.03f, total = 0;
        var labels = new string[5];
        for (int k = 1; k <= 4; k++) { labels[k] = k + " " + (has[k] ? WeaponNames[k] : "—"); total += G.Measure(labels[k], size) + (k > 1 ? gap : 0); }
        float x = w / 2 - total / 2, y = h - h * 0.06f;
        for (int k = 1; k <= 4; k++)
        {
            Color c = k == wantWeapon ? Gold : !has[k] ? Gray : ammo[AmmoOf[k]] > 0 ? Color.White : Red;
            float al = k == wantWeapon ? 1f : has[k] ? 0.7f : 0.35f;
            x += G.Text(labels[k], size, c, x, y, al, 0) + gap;
        }
    }

    void DrawStamina(int w, int h)
    {
        float x = h * 0.04f, bw = h * 0.3f, bh = h * 0.009f, y = h - h * 0.082f;
        G.Rect(x - 2, y - 2, bw + 4, bh + 4, 0, 0, 0, 0.55f);
        float pulse = 0.75f + 0.25f * Sin(Time * 10);
        if (boostT > 0) G.Rect(x, y, bw, bh, 0.35f * pulse, 1f * pulse, 0.3f * pulse, 0.95f);
        else if (exhausted) G.Rect(x, y, bw * stamina, bh, 0.9f, 0.25f, 0.15f, 0.9f);
        else G.Rect(x, y, bw * stamina, bh, 1f, 0.85f, 0.35f, 0.9f);
        if (boostT > 0) G.Text("ENERGY DRINK  " + (int)Math.Ceiling(boostT), h * 0.02f, Color.FromArgb(150, 255, 120), x, y - h * 0.035f, 1, 0);
        else if (exhausted) G.Text("EXHAUSTED", h * 0.02f, Red, x, y - h * 0.035f, pulse, 0);
    }

    static string Fmt(float s) { int t = (int)s; return string.Format("{0}:{1:00}", t / 60, t % 60); }

    public List<string> StatsLines()
    {
        int shots = ShotsBy.Sum(), hits = HitsBy.Sum();
        int acc = shots > 0 ? (int)Math.Round(100.0 * hits / shots) : 0;
        int fav = 1;
        for (int w = 2; w <= 4; w++) if (KillsWith[w] > KillsWith[fav]) fav = w;
        var L = new List<string>();
        L.Add("Total time: " + Fmt(runTime));
        L.Add(string.Format("     {0} {1}  ·  {2} {3}  ·  {4} {5}", Level.Names[0], Fmt(LevelTimes[0]), Level.Names[1], Fmt(LevelTimes[1]), Level.Names[2], Fmt(LevelTimes[2])));
        L.Add(string.Format("Accuracy: {0}%   ({1} shots, {2} hits)", acc, shots, hits));
        L.Add(string.Format("Drones destroyed: {0}   (kamikaze {1} · gunners {2} · heavies {3}){4}",
            KillsBy[1] + KillsBy[2] + KillsBy[3], KillsBy[1], KillsBy[2], KillsBy[3], KillsBy[4] > 0 ? " + Grok" : ""));
        L.Add(string.Format("Favourite weapon: {0}   ({1} kills)", WeaponNames[fav], KillsWith[fav]));
        L.Add(string.Format("Damage taken: {0}   ·   medkits: {1}   ·   shields: {2}   ·   energy drinks: {3}", (int)DamageTaken, PickedUp[1] + PickedUp[2], PickedUp[12], PickedUp[10]));
        L.Add(string.Format("Deaths: {0}   ·   times you shot Elon: {1}", Deaths, GirlHits));
        return L;
    }

    public string Rank()
    {
        int shots = ShotsBy.Sum(), acc = shots > 0 ? (int)Math.Round(100.0 * HitsBy.Sum() / shots) : 0;
        if (acc >= 55 && Deaths == 0 && GirlHits == 0) return "S — Legend of the Maze";
        if (acc >= 40 && Deaths <= 1) return "A — Sharpshooter";
        if (acc >= 25 && Deaths <= 3) return "B — Reliable Rescuer";
        return "C — Persistence Wins";
    }

    // heavy drones are tough: show how much health they have left
    void DrawDroneBars(int w, int h)
    {
        float fx, fy, fz, rx, rz, ux, uy, uz;
        Basis(out fx, out fy, out fz, out rx, out rz, out ux, out uy, out uz);
        foreach (var d in drones)
        {
            if (d.st != 1 || d.type != 3 || d.hp >= d.maxHp) continue;
            float vx = d.x - px, vy = d.y + d.R + 0.35f - eyeY, vz = d.z - pz;
            float zc = vx * fx + vy * fy + vz * fz;
            if (zc < 0.5f || zc > 25 || !Lv.Los(px, pz, d.x, d.z)) continue;
            float sx = (vx * rx + vz * rz) / zc * 1.1f, sy = (vx * ux + vy * uy + vz * uz) / zc * 1.1f;
            float cx = w / 2f + sx * h, cy = h / 2f - sy * h, bw = h * 0.07f, bh = h * 0.009f;
            G.Rect(cx - bw / 2 - 2, cy - 2, bw + 4, bh + 4, 0, 0, 0, 0.6f);
            G.Rect(cx - bw / 2, cy, bw * d.hp / d.maxHp, bh, 1f, 0.55f, 0.15f, 0.95f);
        }
    }

    // the shortest way through the maze, drawn on the minimap and the automap:
    // to the open portal on levels 1 and 2, to the arena once the key card is found on level 3
    readonly float[] route = new float[128];
    int routeN;
    float routeLen;

    void ComputeRoute()
    {
        routeN = 0; routeLen = 0;
        bool active = Lv.Index < 2 ? portalOpen : !gatesLocked && !bossAwake && !bossDead;
        if (!active || State != St.Play) return;
        int i, j; Lv.CellOf(px, pz, out i, out j);
        if (Lv.Index == 2 && Lv.InRoom(i, j)) return;
        var d = Lv.Bfs(Lv.ei, Lv.ej);
        if (d[i, j] == int.MaxValue) return;
        AddRoute(px, pz);
        while (d[i, j] > 0 && routeN < 63 && Lv.StepDown(d, ref i, ref j)) AddRoute((i + 0.5f) * Level.C, (j + 0.5f) * Level.C);
        if (routeN < 2) AddRoute(Lv.ExitX, Lv.ExitZ);
    }

    void AddRoute(float x, float z)
    {
        if (routeN > 0) { float dx = x - route[routeN * 2 - 2], dz = z - route[routeN * 2 - 1]; routeLen += Sqrt(dx * dx + dz * dz); }
        route[routeN * 2] = x; route[routeN * 2 + 1] = z; routeN++;
    }

    void DrawRouteLabel(int w, int h)
    {
        if (routeN < 2) return;
        float cx = w / 2f + (0.5f * w / h - 0.17f) * h, cy = h / 2f - (0.33f - 0.155f) * h;
        G.Text(string.Format("{0} · {1} m", Lv.Index < 2 ? "Portal" : "Grok's arena", (int)Math.Round(routeLen)), h * 0.024f, Gold, cx, cy, 0.95f, 1);
    }

    void DrawGirlBar(int w, int h)
    {
        float bw = w * 0.16f, bh = h * 0.012f, x = w / 2f - bw / 2, y = h * 0.115f;
        G.Text("ELON", h * 0.022f, Color.FromArgb(255, 150, 200), x - h * 0.012f, y - h * 0.012f, 0.95f, 2);
        G.Rect(x, y, bw, bh, 0, 0, 0, 0.6f);
        G.Rect(x, y, bw * Math.Max(0, girlHp) / GirlMaxHp, bh, 1f, 0.45f + 0.55f * girlHit, 0.7f + 0.3f * girlHit, 0.95f);
    }

    // the game logo: the Grok mark on a black disc, with HAL 9000's red eye in the middle
    void DrawLogo(float cx, float cy, float s, float a)
    {
        G.Ellipse(cx, cy, s * 1.15f, s * 1.15f, 0.01f, 0.01f, 0.015f, 0.9f * a);
        const float Q = (float)(Math.PI / 4), gap = 0.36f;
        // screen y points down: the slash runs from the lower left to the upper right (angle -45°)
        G.Arc(cx, cy, s * 0.66f, s * 0.13f, -Q + gap, 3 * Q - gap, 0.95f, 0.95f, 0.97f, a);
        G.Arc(cx, cy, s * 0.66f, s * 0.13f, 3 * Q + gap, 7 * Q - gap, 0.95f, 0.95f, 0.97f, a);
        float d = 0.7071f;
        G.Line(cx + s * 0.34f * d, cy - s * 0.34f * d, cx + s * 1.0f * d, cy - s * 1.0f * d, s * 0.12f, 0.95f, 0.95f, 0.97f, a);
        G.Line(cx - s * 0.34f * d, cy + s * 0.34f * d, cx - s * 0.95f * d, cy + s * 0.95f * d, s * 0.12f, 0.95f, 0.95f, 0.97f, a);
        // the eye: a steel bezel, a black lens and a glowing red core
        float pulse = 0.85f + 0.15f * Sin(Time * 2.2f);
        G.Ellipse(cx, cy, s * 0.5f, s * 0.5f, 1f, 0.05f, 0.02f, 0.1f * a * pulse);
        G.Ellipse(cx, cy, s * 0.29f, s * 0.29f, 0.62f, 0.64f, 0.68f, a);
        G.Ellipse(cx, cy, s * 0.25f, s * 0.25f, 0.02f, 0.02f, 0.02f, a);
        G.Ellipse(cx, cy, s * 0.2f, s * 0.2f, 0.45f * pulse, 0f, 0f, a);
        G.Ellipse(cx, cy, s * 0.14f, s * 0.14f, 0.85f * pulse, 0.05f, 0.02f, a);
        G.Ellipse(cx, cy, s * 0.075f, s * 0.075f, 1f, 0.35f, 0.1f, a);
        G.Ellipse(cx, cy, s * 0.035f, s * 0.035f, 1f, 0.9f, 0.55f, a);
        G.Ellipse(cx - s * 0.1f, cy - s * 0.11f, s * 0.04f, s * 0.025f, 1, 1, 1, 0.35f * a);
    }

    void DrawOverlay(int w, int h)
    {
        if (showFps)
        {
            fpsFrames++; fpsT += 1f / 60;
            G.Text((int)fpsShown + " FPS", h * 0.022f, Green, w - h * 0.03f, h * 0.36f, 0.9f, 2);
        }
        if (dim > 0.003f) G.Rect(0, 0, w, h, 0, 0, 0, dim);
        if (State == St.Story)
        {
            float fade = AudioReady ? Clamp((StoryEnd - StateT) / 1.2f, 0, 1) : 1;
            float lh = h * 0.058f, y0 = h * 0.5f - lh * StoryLines.Length / 2;
            for (int i = 0; i < StoryLines.Length; i++)
            {
                float a = Clamp((StateT - StoryLineT(i)) / 1.1f, 0, 1);
                a = a * a * (3 - 2 * a) * fade;
                float rise = (1 - a) * h * 0.012f;
                bool last = i == StoryLines.Length - 1;
                G.Text(StoryLines[i], h * (last ? 0.042f : 0.034f), last ? Gold : Color.FromArgb(235, 230, 220), w / 2, y0 + lh * i + rise, a, 1);
            }
            if (!AudioReady && StateT > StoryAllShown)
                for (int k = 0; k < 3; k++)
                    G.Ellipse(w / 2f + (k - 1) * h * 0.02f, h * 0.9f, h * 0.004f, h * 0.004f, 1, 1, 1, 0.3f + 0.5f * Math.Max(0, Sin(Time * 4 - k * 0.8f)));
            return;
        }
        if (State == St.Title)
        {
            float a = Clamp(StateT / 1.2f, 0, 1);
            DrawLogo(w / 2f, h * 0.3f, h * 0.16f, a);
            G.Text("I'm sorry, Elon", h * 0.026f, Color.FromArgb(255, 120, 110), w / 2, h * 0.49f, a * 0.9f, 1);
            G.Text("SAVE ELON!", h * 0.11f, Gold, w / 2, h * 0.54f, a, 1);
            if (StateT > 1.2f) G.Text("Press any key to start", h * 0.04f, Color.White, w / 2, h * 0.8f, Math.Min(1, (StateT - 1.2f) * 2) * (0.55f + 0.45f * Sin(Time * 3)), 1);
            return;
        }
        if (State == St.Play || State == St.Dead || State == St.Warp)
        {
            float x = h * 0.035f;
            G.Text(string.Format("Level {0} · {1}", Lv.Index + 1, Lv.Name), h * 0.028f, Gold, x, h * 0.025f, 0.95f, 0);
            string goal = Lv.Index < 2
                ? (portalOpen ? "Portal open — route on the minimap" : string.Format("Drones: {0} / {1}", Remaining(), totalDrones))
                : (bossDead ? "Free Elon" : bossAwake ? "Defeat Grok"
                   : !gatesLocked ? "Doors unlocked — route on the minimap"
                   : keycardDropped ? "Pick up the key card"
                   : string.Format("Drones: {0} / {1} — the last one has the key card", Remaining(), totalDrones));
            G.Text(goal, h * 0.026f, portalOpen ? Cyan : Color.White, x, h * 0.065f, 0.9f, 0);
            G.Text("Kills: " + kills, h * 0.022f, Gray, x, h * 0.1f, 0.85f, 0);
            G.Text("F1 — controls", h * 0.018f, Gray, x, h * 0.132f, 0.55f, 0);
            float hw = G.Text(((int)Math.Ceiling(Math.Max(0, health))).ToString(), h * 0.03f, Color.White, h * 0.36f, h - h * 0.078f, 0.9f, 0);
            if (armor > 0) G.Text("+" + (int)Math.Ceiling(armor), h * 0.026f, Color.FromArgb(120, 180, 255), h * 0.36f + hw + h * 0.005f, h - h * 0.074f, 0.95f, 0);
            DrawDroneBars(w, h);
            DrawRouteLabel(w, h);
            DrawWeaponHud(w, h);
            DrawStamina(w, h);
            if (boss != null && bossAwake && !bossDead) G.Text("GROK", h * 0.026f, Red, w / 2, h * 0.025f, 0.95f, 1);
            if (Lv.Index == 2 && !girlFree && (bossAwake || girlHp < GirlMaxHp)) DrawGirlBar(w, h);
            DrawMessages(w, h);
            if (mapT > 0.5f) G.Text("MAP  ·  Tab — close", h * 0.026f, Gold, w / 2, h * 0.885f, mapT, 1);
            if (State == St.Play && !captured && Interactive && !quitAsk && !help)
            {
                G.Text("PAUSED", h * 0.08f, Color.White, w / 2, h * 0.42f, 1, 1);
                G.Text("Click to resume    F1 — controls    Esc — quit", h * 0.03f, Gray, w / 2, h * 0.54f, 1, 1);
            }
            if (State == St.Dead)
            {
                // a nod to the famously clumsy translation of "WASTED" in GTA San Andreas
                if (girlDead) G.Text("ELON IS DOWN", h * 0.09f, Red, w / 2, h * 0.34f, Math.Min(1, StateT * 2), 1);
                else G.Text("WASTED", h * 0.12f, Color.FromArgb(205, 60, 60), w / 2, h * 0.33f, Math.Min(1, Math.Max(0, StateT - 0.4f) * 2), 1);
                if (girlDead) G.Text("Aim carefully: Grok hovers right above him", h * 0.03f, Gray, w / 2, h * 0.45f, Math.Min(1, StateT * 2), 1);
                if (StateT > 1.2f) G.Text("Click to restart the level", h * 0.034f, Color.White, w / 2, h * 0.52f, 0.6f + 0.4f * Sin(Time * 3), 1);
            }
            return;
        }
        if (State == St.Ending)
        {
            DrawMessages(w, h);
            if (StateT > 1.5f) G.Text("ELON IS SAVED!", h * 0.07f, Gold, w / 2, h * 0.04f, Math.Min(1, (StateT - 1.5f) * 2), 1);
            if (StateT > 2.5f) G.Text("“Thanks! Now where's my phone? I have 400 posts to catch up on.”  — Elon", h * 0.036f, Color.White, w / 2, h * 0.15f, Math.Min(1, (StateT - 2.5f) * 2), 1);
            if (StateT > 3.5f)
            {
                var lines = StatsLines();
                float x0 = w * 0.46f, y0 = h * 0.24f, pw = w * 0.51f, lh = h * 0.05f;
                float pa = Math.Min(1, (StateT - 3.5f) * 2);
                G.Rect(x0, y0, pw, lh * (lines.Count + 3.1f), 0, 0, 0, 0.55f * pa);
                G.Text("RESULTS", h * 0.036f, Gold, x0 + h * 0.03f, y0 + h * 0.012f, pa, 0);
                for (int i = 0; i < lines.Count; i++)
                {
                    float la = Math.Min(1, (StateT - 4f - i * 0.35f) * 3);
                    if (la > 0) G.Text(lines[i], h * 0.024f, Color.White, x0 + h * 0.03f, y0 + lh * (i + 1.35f), la, 0);
                }
                float ra = Math.Min(1, (StateT - 4.3f - lines.Count * 0.35f) * 2);
                if (ra > 0) G.Text("RANK  " + Rank(), h * 0.042f, Gold, x0 + h * 0.03f, y0 + lh * (lines.Count + 1.5f), ra, 0);
            }
            if (StateT > 6f)
            {
                float ca = Math.Min(1, (StateT - 6f) * 1.5f) * 0.9f;
                string[] credits = { "Ideas & QA — PariPariKai", "Code & god-tier skills — Claude Opus 5.5", "Made on September 24–25, 2026", "A fan parody. Not affiliated with xAI, X or Elon Musk." };
                float cw = 0;
                foreach (var c in credits) cw = Math.Max(cw, G.Measure(c, h * 0.021f));
                G.Rect(w - h * 0.05f - cw, h * 0.765f, cw + h * 0.035f, h * 0.125f, 0, 0, 0, 0.55f * ca);
                for (int i = 0; i < credits.Length; i++)
                    G.Text(credits[i], h * (i == 3 ? 0.017f : 0.021f), i >= 2 ? Gray : Color.FromArgb(235, 230, 220), w - h * 0.03f, h * (0.775f + i * 0.028f), ca, 2);
            }
            if (StateT > 7f) G.Text("Thanks for playing! Press any key to return to the menu", h * 0.03f, Cyan, w / 2, h * 0.9f, 0.6f + 0.4f * Sin(Time * 3), 1);
        }
    }

    // ------------------------------------------------------------------ autopilot for automated play-through tests
    public Input BotInput()
    {
        var inp = new Input();
        Drone tgt = null; float best = 1e9f;
        foreach (var d in drones)
        {
            if (d.st != 1 || (d.type == 4 && !bossAwake)) continue;
            float dx = d.x - px, dz = d.z - pz, hd = Sqrt(dx * dx + dz * dz);
            if (hd < 26 && hd < best && Lv.Los(px, pz, d.x, d.z)) { best = hd; tgt = d; }
        }
        if (tgt != null)
        {
            float dx = tgt.x - px, dz = tgt.z - pz;
            inp.dyaw = WrapAngle((float)Math.Atan2(dx, dz) - yaw);
            inp.dpitch = (float)Math.Atan2(tgt.y - eyeY, best) - pitch;
            inp.fire = true;
            // pick the gun a sensible player would use at this range
            int w = 1;
            if (has[4] && ammo[3] > 0) w = 4;
            if (has[2] && ammo[1] > 0 && best < 7) w = 2;
            float tgx = tgt.x - Lv.ExitX, tgz = tgt.z - Lv.ExitZ;
            if (has[3] && ammo[2] > 0 && best > 6 && (Lv.Index != 2 || tgx * tgx + tgz * tgz > 25)) w = 3;
            inp.weapon = w;
            float ny = yaw + inp.dyaw, np = pitch + inp.dpitch, gt;
            float ax = Sin(ny) * Cos(np), ay = Sin(np), az = Cos(ny) * Cos(np);
            float td = Sqrt(best * best + (tgt.y - eyeY) * (tgt.y - eyeY));
            if (GirlRay(ax, ay, az, out gt) && gt < td + 1) inp.fire = false;
            if (Lv.Index == 2 && !girlFree)
            {
                float gx0 = Lv.ExitX - px, gy0 = 1.1f - eyeY, gz0 = Lv.ExitZ - pz;
                float gl = Sqrt(gx0 * gx0 + gy0 * gy0 + gz0 * gz0);
                float cosA = (ax * gx0 + ay * gy0 + az * gz0) / gl;
                if (gl < td + 2 && cosA > Cos(0.22f)) inp.fire = false;
            }
        }
        float gx = Lv.ExitX, gz = Lv.ExitZ;
        Pickup gun = null;
        Pickup card = null;
        foreach (var p in pickups) { if (p.type >= 7 && p.type <= 9) gun = p; if (p.type == 11) card = p; }
        Pickup box = null;
        int firepower = ammo[0] + ammo[1] * 6 + ammo[2] * 6 + ammo[3];
        if ((firepower < 40 && tgt == null) || firepower < 10)
        {
            int ci, cj; Lv.CellOf(px, pz, out ci, out cj);
            var bfs = Lv.Bfs(ci, cj);
            int bd = int.MaxValue;
            foreach (var p in pickups)
            {
                if (p.type < 3 || p.type > 6 || ammo[p.type - 3] >= AmmoMax[p.type - 3]) continue;
                int pi2, pj2; Lv.CellOf(p.x, p.z, out pi2, out pj2);
                if (bfs[pi2, pj2] < bd) { bd = bfs[pi2, pj2]; box = p; }
            }
        }
        if (gun != null) { gx = gun.x; gz = gun.z; }   // grab the level's new weapon first
        else if (box != null) { gx = box.x; gz = box.z; }
        else if (card != null) { gx = card.x; gz = card.z; }
        else if (Lv.Index == 2 && !gatesLocked) gz -= bossDead ? 1.5f : 4f;
        else if (!portalOpen || Lv.Index == 2)
        {
            int ci, cj; Lv.CellOf(px, pz, out ci, out cj);
            var bfs = Lv.Bfs(ci, cj);
            int bd = int.MaxValue;
            foreach (var d in drones)
            {
                if (d.st != 1 || d.minion || d.type == 4) continue;
                int di, dj; Lv.CellOf(d.x, d.z, out di, out dj);
                if (bfs[di, dj] < bd) { bd = bfs[di, dj]; gx = d.x; gz = d.z; }
            }
        }
        int gi, gj; Lv.CellOf(gx, gz, out gi, out gj);
        int pi, pj; Lv.CellOf(px, pz, out pi, out pj);
        float wx = gx, wz = gz;
        if (gi != pi || gj != pj) Lv.Toward(px, pz, gi, gj, out wx, out wz);
        float mx = wx - px, mz = wz - pz, ml = Sqrt(mx * mx + mz * mz);
        bool hold = tgt != null && tgt.type != 1 && tgt.type != 4 && best < 12 && inp.fire && box == null;
        if (ml > 0.3f && !hold)
        {
            mx /= ml; mz /= ml;
            float ny = yaw + inp.dyaw;
            inp.fwd = mx * Sin(ny) + mz * Cos(ny);
            inp.str = mx * Cos(ny) - mz * Sin(ny);
            inp.run = tgt == null;
        }
        if (tgt != null && tgt.type == 4 && box == null)
        {
            // circle-strafe around the boss instead of standing in its line of fire
            inp.fwd = 0;
            inp.str = ((int)(Time / 1.6f) % 2 == 0) ? 1 : -1;
        }
        return inp;
    }

    // ------------------------------------------------------------------ entry point
    [STAThread]
    static int Main(string[] args)
    {
        W.SetProcessDPIAware();
        W.LoadLibrary("opengl32.dll");
        Application.EnableVisualStyles();
        string mode = args.Length > 0 ? args[0] : "";
        string dir = args.Length > 1 ? args[1] : ".";
        if (mode == "--bot") return RunBot(dir);

        bool shot = mode == "--shot" || mode == "--flicker";
        var g = new Game(false);
        g.Show();
        try
        {
            g.G.Init(g.Handle, !shot);
            if (!shot)
            {
                g.SetFullscreen(true);
                // start at roughly 1080p worth of pixels; F2 changes it
                g.RenderScale = Math.Min(1f, Sqrt(2.1e6f / Math.Max(1, g.ClientSize.Width * g.ClientSize.Height)));
            }
            g.Render(); g.G.Present(); Application.DoEvents();
            g.G.Compile(Gfx.LoadShaderSource());
        }
        catch (Exception ex)
        {
            if (shot) File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
            else MessageBox.Show(ex.Message, "Save Elon!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        if (shot) return mode == "--flicker" ? Flicker(g, dir) : Shots(g, dir);

        // music and sounds are synthesized in the background while the story is told
        var synth = Task.Factory.StartNew(() => g.B.Generate());
        g.SetupStory();
        g.Interactive = true;
        if (Form.ActiveForm == g) g.Grab(true);

        W.timeBeginPeriod(1);
        var sw = Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds, frameSum = 0, fpsClock = last;
        int frames = 0, fpsCount = 0;
        while (g.Created)
        {
            double frameStart = sw.Elapsed.TotalSeconds;
            Application.DoEvents();
            if (!g.Created) break;
            if (g.WindowState == FormWindowState.Minimized) { Thread.Sleep(50); last = sw.Elapsed.TotalSeconds; continue; }
            double now = sw.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, now - last);
            // if frames get slow, render at a lower resolution
            frameSum += now - last; frames++;
            if (frameSum > 1.0)
            {
                if (g.AutoRes && frameSum / frames > 0.019 && g.RenderScale > 0.5f) g.RenderScale = Math.Max(0.5f, g.RenderScale - 0.1f);
                frameSum = 0; frames = 0;
            }
            last = now;
            if (!g.AudioReady && synth.IsCompleted)
            {
                if (synth.IsFaulted) { MessageBox.Show(synth.Exception.ToString(), "Save Elon!"); return 1; }
                GC.Collect(); // drop the synthesizer's temporary buffers
                g.OnAudioReady();
            }
            bool paused = g.QuitAsk || g.Help || (!g.Captured && g.State == St.Play);
            if (paused) Thread.Sleep(10);
            else g.Update(dt, g.ReadInput());
            g.Render();
            g.G.Present();
            // cap at 60 frames per second: this game does not need more
            fpsCount++;
            if (frameStart - fpsClock >= 1) { g.fpsShown = (float)(fpsCount / (frameStart - fpsClock)); fpsCount = 0; fpsClock = frameStart; }
            while (sw.Elapsed.TotalSeconds - frameStart < 1.0 / 60)
            {
                if (1.0 / 60 - (sw.Elapsed.TotalSeconds - frameStart) > 0.002) Thread.Sleep(1);
                else Thread.SpinWait(200);
            }
        }
        W.timeEndPeriod(1);
        return 0;
    }

    // renders fixed scenes to PNG to check the visuals without playing
    static int Shots(Game g, string dir)
    {
        var report = new StringBuilder();
        report.AppendLine("GPU: " + g.G.GpuName());
        var handle = g.Handle;
        Action<string> snap = name =>
        {
            g.Render();
            g.G.SavePng(Path.Combine(dir, name + ".png"), g.ClientSize.Width, g.ClientSize.Height);
            g.G.Present();
            Application.DoEvents();
        };
        Action<int> perf = lvl =>
        {
            g.Render(); W.glFinish();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { g.Render(); g.G.Present(); }
            W.glFinish();
            report.AppendLine(string.Format("level {0}: {1:F1} ms/frame at {2}x{3}", lvl + 1, sw.Elapsed.TotalMilliseconds / 20, g.ClientSize.Width, g.ClientSize.Height));
        };

        g.SetupStory(); g.Time = 4; g.dim = 0.8f;
        g.StateT = 5.5f; snap("0a_story_mid");
        g.StateT = 14f; snap("0b_story_full");
        g.SetupTitle(); g.Time = 4; g.dim = 0.4f; g.StateT = 3;
        snap("0_title");
        g.dim = 0;
        g.quitAsk = true; snap("0b_quit"); g.quitAsk = false;

        // level 1: looking down the first corridor at a seeker and a gunner
        g.LoadLevel(0); g.fadeIn = 0; g.msgs.Clear(); g.Time = 3;
        g.drones.Clear(); g.pickups.Clear();
        float fx = Sin(g.yaw), fz = Cos(g.yaw);
        var a = g.NewDrone(1, g.px + fx * 4.5f + fz * 0.8f, 1.9f, g.pz + fz * 4.5f - fx * 0.8f); a.alert = true; g.drones.Add(a);
        var b = g.NewDrone(2, g.px + fx * 8f - fz * 1f, 2.4f, g.pz + fz * 8f + fx * 1f); g.drones.Add(b);
        g.bullets.Add(new Bullet { x = g.px + fx * 5.5f - fz * 0.6f, y = 1.6f, z = g.pz + fz * 5.5f + fx * 0.6f, kind = 1 });
        g.pitch = 0.02f;
        snap("1_catacombs");
        perf(0);
        a.charging = true; a.x = g.px + fx * 3f; a.z = g.pz + fz * 3f; a.y = 1.6f; g.Time = 3.02f;
        snap("1b_kamikaze");
        g.drones.Clear(); g.bullets.Clear();
        g.help = true; snap("1d_help"); g.help = false;
        g.pickups.Clear(); g.pitch = 0.02f;
        // automap after walking a bit
        for (int i = 0; i < g.Lv.N; i++) for (int j = 0; j < g.Lv.M; j++) if ((i + j) % 3 != 0 && i < 8) g.Lv.Visit(i, j);
        g.G.UploadWalls(g.Lv.WallTexture(), g.Lv.N + 1, g.Lv.M + 1);
        g.mapT = 1; g.mapOn = true;
        snap("1c_automap");
        g.mapT = 0; g.mapOn = false;

        // every pickup in a row, and each gun in hand
        g.drones.Clear(); g.bullets.Clear();
        g.pitch = -0.28f;
        for (int t = 1; t <= 12; t++)
        {
            float side = (t - 6.5f) * 0.5f, dist = 3.4f + Math.Abs(t - 6.5f) * 0.1f;
            g.pickups.Add(new Pickup { type = t, x = g.px + fx * dist + fz * side, z = g.pz + fz * dist - fx * side, baseY = t >= 7 ? 0.75f : 0.45f, y = t >= 7 ? 0.75f : 0.45f });
        }
        for (int w = 1; w <= 4; w++) g.has[w] = true;
        g.ammo = new[] { 120, 16, 7, 180 };
        for (int w = 1; w <= 4; w++)
        {
            g.weapon = g.wantWeapon = w; g.Time = 2 + w;
            snap("6_weapon_" + w);
        }

        // rockets and plasma in flight, a rocket blast further down the corridor
        g.pickups.Clear(); g.pitch = 0.02f; g.weapon = g.wantWeapon = 3; g.flash = 0.8f; g.recoil = 0.6f;
        g.projs.Add(new Proj { kind = 1, x = g.px + fx * 2.5f + fz * 0.2f, y = 1.5f, z = g.pz + fz * 2.5f - fx * 0.2f });
        g.projs.Add(new Proj { kind = 2, x = g.px + fx * 3.5f - fz * 0.6f, y = 1.8f, z = g.pz + fz * 3.5f + fx * 0.6f });
        g.projs.Add(new Proj { kind = 2, x = g.px + fx * 4.5f - fz * 0.7f, y = 1.9f, z = g.pz + fz * 4.5f + fx * 0.7f });
        g.blasts.Add(new Blast { x = g.px + fx * 9f, y = 1.8f, z = g.pz + fz * 9f, t = 0.15f });
        snap("7_rocket_blast");
        g.projs.Clear(); g.blasts.Clear();

        // shotgun pellets spreading out, sparks where they hit
        g.weapon = g.wantWeapon = 2; g.flash = 1; g.recoil = 1; g.Time = 3.3f;
        var pr = new Random(3);
        for (int k = 0; k < 8; k++)
        {
            float dist = 2.5f + (float)pr.NextDouble() * 3f, side = ((float)pr.NextDouble() - 0.5f) * 0.16f * dist, up = ((float)pr.NextDouble() - 0.5f) * 0.16f * dist;
            g.projs.Add(new Proj { kind = 3, x = g.px + fx * dist + fz * side, y = 1.6f + up, z = g.pz + fz * dist - fx * side });
        }
        for (int k = 0; k < 4; k++) g.sparks.Add(new Spark { x = g.px + fx * 11f + fz * (k - 1.5f) * 0.5f, y = 1.4f + k * 0.2f, z = g.pz + fz * 11f - fx * (k - 1.5f) * 0.5f, t = 0.2f });
        snap("8_shotgun_pellets");
        g.projs.Clear(); g.sparks.Clear();

        // stamina: an energy drink on the floor and one already working
        g.pickups.Add(new Pickup { type = 10, x = g.px + fx * 2.6f, z = g.pz + fz * 2.6f, baseY = 0.45f, y = 0.45f });
        g.boostT = 13.4f; g.flash = 0; g.recoil = 0; g.pitch = -0.2f;
        snap("10_energy");
        g.pickups.Clear(); g.pickups.Add(new Pickup { type = 12, x = g.px + fx * 1.6f, z = g.pz + fz * 1.6f, baseY = 0.9f, y = 0.9f }); g.pitch = -0.33f;
        for (int k = 0; k < 4; k++) { g.Time = k * 0.5f; snap("10c_shield_" + k); }
        g.pickups.Clear();
        g.boostT = 0; g.stamina = 0.12f; g.exhausted = true; g.pickups.Clear(); g.pitch = 0.02f;
        snap("10b_exhausted");
        g.exhausted = false; g.stamina = 1;

        // wasted
        g.State = St.Dead; g.StateT = 2.5f; g.eyeY = 0.5f; g.pitch = 0.1f; g.girlDead = false;
        snap("12_wasted");
        g.State = St.Play; g.eyeY = 1.7f;

        // level 2: firing at a heavy drone, an explosion next to it
        g.LoadLevel(1); g.fadeIn = 0; g.msgs.Clear(); g.Time = 5;
        g.drones.Clear();
        fx = Sin(g.yaw); fz = Cos(g.yaw);
        var h = g.NewDrone(3, g.px + fx * 7f, 2.3f, g.pz + fz * 7f); h.flash = 0.6f; g.drones.Add(h);
        var ex = g.NewDrone(2, g.px + fx * 5f + fz * 1.8f, 2.0f, g.pz + fz * 5f - fx * 1.8f); ex.st = 2; ex.tm = 0.2f; g.drones.Add(ex);
        g.bullets.Add(new Bullet { x = g.px + fx * 4f - fz * 0.3f, y = 1.9f, z = g.pz + fz * 4f + fx * 0.3f, kind = 3 });
        g.weapon = g.wantWeapon = 2;
        g.flash = 1; g.recoil = 1; g.hitm = 1; g.health = 55; g.kills = 12;
        float mx, my, mz; g.Muzzle(out mx, out my, out mz); g.trAx = mx; g.trAy = my; g.trAz = mz;
        g.trBx = h.x; g.trBy = h.y; g.trBz = h.z; g.trAlpha = 0.8f;
        g.pitch = 0.06f;
        snap("2_foundry");
        perf(1);

        // level 3: the arena, the Warden awake above the caged girl
        g.LoadLevel(2); g.fadeIn = 0; g.msgs.Clear(); g.Time = 7;
        g.weapon = g.wantWeapon = 4;
        g.px = g.Lv.ExitX + 0.5f; g.pz = g.Lv.ExitZ - 7f; g.yaw = -0.05f; g.pitch = 0.12f;
        g.bossAwake = true; g.boss.x = g.Lv.ExitX + 2.8f; g.boss.y = 3.5f; g.boss.z = g.Lv.ExitZ + 2.5f; g.boss.hp = 60;
        g.bullets.Add(new Bullet { x = g.boss.x - 1.2f, y = 3.0f, z = g.boss.z - 2.5f, kind = 3 });
        g.bullets.Add(new Bullet { x = g.boss.x - 0.4f, y = 2.8f, z = g.boss.z - 3.5f, kind = 3 });
        g.bullets.Add(new Bullet { x = g.boss.x + 0.6f, y = 2.6f, z = g.boss.z - 4.2f, kind = 3 });
        snap("3_sanctum_boss");
        {
            var gt = g.Lv.Gates[0];
            float nx = gt[2] < 0.5f ? 1 : 0, nz = 1 - nx;
            float spx = g.px, spz = g.pz, syaw = g.yaw, spitch = g.pitch;
            g.px = gt[0] - nx * 3.5f; g.pz = gt[1] - nz * 3.5f; g.yaw = (float)Math.Atan2(nx, nz); g.pitch = 0.1f;
            snap("13_gate");
            g.doorOpen[0] = 0.55f; g.gatesLocked = false;
            snap("13b_door_open");
            g.doorOpen[0] = 0; g.gatesLocked = true;
            g.bossAwake = false; g.gatesLocked = false; g.Lv.DoorsLocked = false; g.bullets.Clear(); g.drones.Clear();
            g.px = (g.Lv.si + 0.5f) * Level.C; g.pz = (g.Lv.sj + 0.5f) * Level.C; g.yaw = g.Lv.startYaw; g.pitch = 0;
            snap("14_guide");
            g.yaw += 3.1f;
            snap("14b_guide_behind");
            g.LoadLevel(0); g.fadeIn = 0; g.msgs.Clear(); g.drones.Clear(); g.portalOpen = true; g.Time = 3;
            snap("14c_guide_portal");
            g.mapT = 1; g.mapOn = true;
            snap("14d_guide_automap");
            g.mapT = 0; g.mapOn = false;
            g.LoadLevel(2); g.fadeIn = 0; g.msgs.Clear();
            g.bossAwake = true; g.gatesLocked = true; g.Lv.DoorsLocked = true;
            g.px = spx; g.pz = spz; g.yaw = syaw; g.pitch = spitch;
        }
        perf(2);
        g.girlHp = 2.5f; g.girlHit = 0.8f;
        snap("3b_lina_hit");
        g.girlHit = 0;
        g.girlDead = true; g.girlFallT = 1; g.girlYawDead = g.GirlYaw(); g.State = St.Dead; g.StateT = 3; g.bullets.Clear();
        g.px = g.Lv.ExitX + 0.5f; g.pz = g.Lv.ExitZ - 5f; g.pitch = -0.2f;
        snap("9_lina_down");
        g.girlDead = false; g.girlHp = GirlMaxHp;

        // the rescue
        g.bossAwake = true; g.bossDead = true; g.boss.st = 0; g.cage = 0; g.girlFree = true;
        g.bullets.Clear(); g.State = St.Ending; g.StateT = 9; g.runTime = 1231; g.kills = 70; g.Time = 9.2f;
        g.ShotsBy[1] = 210; g.ShotsBy[2] = 48; g.ShotsBy[3] = 14; g.ShotsBy[4] = 190; g.HitsBy[1] = 96; g.HitsBy[2] = 31; g.HitsBy[3] = 9; g.HitsBy[4] = 88;
        g.KillsBy[1] = 30; g.KillsBy[2] = 25; g.KillsBy[3] = 11; g.KillsBy[4] = 1; g.KillsWith[1] = 22; g.KillsWith[2] = 26; g.KillsWith[3] = 7; g.KillsWith[4] = 12;
        g.LevelTimes[0] = 355; g.LevelTimes[1] = 488; g.LevelTimes[2] = 388; g.DamageTaken = 523; g.PickedUp[1] = 4; g.PickedUp[2] = 3; g.PickedUp[10] = 3; g.Deaths = 1; g.GirlHits = 2;
        g.px = g.Lv.ExitX + 0.3f; g.pz = g.Lv.ExitZ - 2.4f; g.yaw = -0.12f; g.pitch = -0.05f;
        snap("4_rescue");
        g.StateT = 0.3f; g.px = g.Lv.ExitX; g.pz = g.Lv.ExitZ - 2.1f; g.yaw = 0; g.pitch = -0.1f; g.eyeY = 1.6f;
        snap("5_portrait");
        g.pz = g.Lv.ExitZ - 0.85f; g.eyeY = 1.8f; g.pitch = -0.01f; g.Time = 20.5f;
        snap("5b_face");
        g.girlFree = false; g.pz = g.Lv.ExitZ - 2.1f; g.eyeY = 1.6f; g.pitch = -0.1f;
        snap("5c_captive");

        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        g.Close();
        return 0;
    }

    // measures how much the picture jumps between consecutive 60 fps frames with a still camera
    static int Flicker(Game g, string dir)
    {
        var log = new StringBuilder();
        int w = g.ClientSize.Width, h = g.ClientSize.Height;
        double worstAll = 0;
        for (int lvl = 0; lvl < 3; lvl++)
            foreach (int seed in new[] { 5, 6, 7 })
            {
                g.runSeed = seed; g.LoadLevel(lvl);
                g.msgs.Clear(); g.fadeIn = 0; g.drones.Clear(); g.pickups.Clear();
                g.px = g.Lv.titleX; g.pz = g.Lv.titleZ; g.yaw = g.Lv.titleYaw; g.pitch = 0.1f;
                g.Time = 100;
                byte[] prev = null;
                double worst = 0, sum = 0;
                for (int f = 0; f < 120; f++)
                {
                    g.Time += 1f / 60;
                    g.Render();
                    var cur = g.G.ReadLuma(w, h);
                    g.G.Present();
                    if (prev != null)
                    {
                        int big = 0;
                        for (int i = 0; i < cur.Length; i++) if (Math.Abs(cur[i] - prev[i]) > 12) big++;
                        double frac = 100.0 * big / cur.Length;
                        worst = Math.Max(worst, frac); sum += frac;
                    }
                    prev = cur;
                }
                worstAll = Math.Max(worstAll, worst);
                log.AppendLine(string.Format("level {0} seed {1}: pixels jumping >12 levels between frames: worst {2:F2}%, average {3:F3}%", lvl + 1, seed, worst, sum / 119));
            }
        log.AppendLine(string.Format("WORST OVERALL: {0:F2}%", worstAll));
        g.runSeed = 5; g.LoadLevel(0); g.msgs.Clear(); g.fadeIn = 0; g.drones.Clear(); g.pickups.Clear();
        g.px = g.Lv.titleX; g.pz = g.Lv.titleZ; g.yaw = g.Lv.titleYaw; g.pitch = 0.3f; g.Time = 50;
        float wx = Sin(g.yaw), wz = Cos(g.yaw);
        float wallT = g.Lv.Ray(g.px, 1.7f, g.pz, wx, 0, wz, 80);
        g.px += wx * (wallT - 3.2f); g.pz += wz * (wallT - 3.2f);
        g.pitch = 0.45f; g.px -= wx * 1.5f; g.pz -= wz * 1.5f;
        for (int k = 0; k < 3; k++)
        {
            g.Render();
            g.G.SavePng(Path.Combine(dir, "wall_" + k + ".png"), w, h);
            g.G.Present();
            g.px += Cos(g.yaw) * 0.35f; g.pz -= Sin(g.yaw) * 0.35f;
        }
        g.px -= Cos(g.yaw) * 1.05f; g.pz += Sin(g.yaw) * 1.05f;
        for (int m = 1; m <= 4; m++)
        {
            g.DebugMode = m;
            g.Render();
            g.G.SavePng(Path.Combine(dir, "debug_" + m + ".png"), w, h);
            g.G.Present();
        }
        g.DebugMode = 0;
        // a box of shells lying on the floor ahead
        g.px = g.Lv.titleX; g.pz = g.Lv.titleZ; g.yaw = g.Lv.titleYaw; g.pitch = -0.25f;
        g.pickups.Add(new Pickup { type = 4, x = g.px + wx * 3f, z = g.pz + wz * 3f, y = 0.45f, baseY = 0.45f });
        g.Render(); g.G.SavePng(Path.Combine(dir, "art1_pickup.png"), w, h); g.G.Present();
        g.pickups.Clear();
        // standing in the middle of a cell looking straight at a wall, lamp overhead
        int ci, cj; g.Lv.CellOf(g.px, g.pz, out ci, out cj);
        g.px = (ci + 0.5f) * Level.C; g.pz = (cj + 0.5f) * Level.C; g.pitch = 0.15f;
        for (int k = 0; k < 4; k++)
        {
            float yy = k * 1.5707963f;
            if (g.Lv.WallDist(g.px + Sin(yy) * 3f, g.pz + Cos(yy) * 3f) < 0) { g.yaw = yy; break; }
        }
        g.Render(); g.G.SavePng(Path.Combine(dir, "art3_wall.png"), w, h); g.G.Present();
        // close to a pillar
        float lx = Level.C * (float)Math.Floor(g.px / Level.C + 0.5f), lz = Level.C * (float)Math.Floor(g.pz / Level.C + 0.5f);
        g.yaw = (float)Math.Atan2(lx - g.px, lz - g.pz); g.pitch = 0.05f;
        float dd = Sqrt((lx - g.px) * (lx - g.px) + (lz - g.pz) * (lz - g.pz));
        g.px = lx - Sin(g.yaw) * 1.8f; g.pz = lz - Cos(g.yaw) * 1.8f;
        g.Render(); g.G.SavePng(Path.Combine(dir, "art2_pillar.png"), w, h); g.G.Present();
        File.WriteAllText(Path.Combine(dir, "flicker.txt"), log.ToString());
        g.Close();
        return 0;
    }

    // plays all three levels with the autopilot, no window, and logs how it went
    static int RunBot(string dir)
    {
        var log = new StringBuilder();
        var sw = Stopwatch.StartNew();
        var g = new Game(true);
        g.B.Generate();
        log.AppendLine(string.Format("audio synthesis: {0:F2} s", sw.Elapsed.TotalSeconds));
        for (int i = 0; i < 5; i++)
        {
            var m = g.B.Music[i]; double peak = 0, rms = 0;
            foreach (short s in m) { peak = Math.Max(peak, Math.Abs((int)s)); rms += (double)s * s; }
            log.AppendLine(string.Format("  track {0}: {1:F1} s, peak {2:F2}, rms {3:F3}", i, m.Length / 2.0 / Syn.SR, peak / 32768, Math.Sqrt(rms / m.Length) / 32768));
        }
        g.runSeed = 7; g.LoadLevel(0);
        {
            float peak = 0; bool first = true;
            for (int k = 0; k < 120; k++)
            {
                var ji = new Input(); ji.jump = first; first = false;
                g.Update(1f / 60, ji);
                peak = Math.Max(peak, g.feetY);
            }
            log.AppendLine(string.Format("jump test: peak {0:F2} m, landed: {1}", peak, g.grounded && g.feetY == 0));
            var si = new Input(); si.run = true; si.fwd = 1;
            float t0 = 0;
            while (g.stamina > 0 && t0 < 20) { g.Update(1f / 60, si); t0 += 1f / 60; if (g.State != St.Play) break; }
            float t1 = 0;
            while (g.exhausted && t1 < 30) { g.Update(1f / 60, new Input()); t1 += 1f / 60; }
            log.AppendLine(string.Format("stamina test: sprint lasted {0:F1} s, running again after {1:F1} s", t0, t1));
        }
        {
            g.runSeed = 9; g.LoadLevel(1); g.has[2] = false;
            int n = 0; bool dropped = false;
            while (n < 300 && !dropped)
            {
                var dd = g.NewDrone(2, g.px + 2, 2, g.pz);
                g.drones.Add(dd); g.Kill(dd); n++;
                dropped = g.pickups.Exists(p => p.type == 7);
            }
            log.AppendLine(string.Format("weapon drop test: missed shotgun dropped after {0} kills: {1}", n, dropped));
        }
        {
            // hidden timer: stand still where no drone can see you; after 30 s the nearest one comes hunting
            g.runSeed = 11; g.LoadLevel(0); g.God = true;
            int h0 = g.HuntersSent; float tt = 0, firstHunt = -1, reached = -1;
            while (tt < 90 && reached < 0)
            {
                g.Update(1f / 60, new Input()); tt += 1f / 60;
                if (firstHunt < 0 && g.HuntersSent > h0) firstHunt = tt;
                foreach (var d in g.drones)
                    if (d.st == 1 && d.alert && (d.x - g.px) * (d.x - g.px) + (d.z - g.pz) * (d.z - g.pz) < 8 * 8 && g.Lv.Los(d.x, d.z, g.px, g.pz)) reached = tt;
            }
            log.AppendLine(string.Format("hunter test: first hunter sent at {0:F1} s, a drone found the idle player at {1:F1} s", firstHunt, reached));
            for (int s = 1; s <= 5; s++)
            {
                var L3 = Level.Make(2, s * 977);
                int inRoom = 0, outside = 0;
                foreach (var it in L3.items)
                {
                    if (it.type != 1 && it.type != 2 && it.type != 12) continue;
                    int ci, cj; L3.CellOf(it.x, it.z, out ci, out cj);
                    if (L3.InRoom(ci, cj)) inRoom++; else outside++;
                }
                log.AppendLine(string.Format("level 3 seed {0}: medkits and shields in the arena {1}, in the maze {2}", s * 977, inRoom, outside));
            }
        }
        g.StartGame();
        foreach (int seed in new[] { 101, 202, 303 })
        {
        log.AppendLine("=== maze seed " + seed);
        g.ResetInventory(); g.health = 100; g.runSeed = seed;
        for (int lvl = 0; lvl < 3; lvl++)
        {
            sw.Restart();
            g.LoadLevel(lvl);
            log.AppendLine(string.Format("level {0} ({1}): {2}x{3} cells, built in {4} ms, {5} drones, {6} pickups",
                lvl + 1, g.Lv.Name, g.Lv.N, g.Lv.M, sw.ElapsedMilliseconds, g.totalDrones, g.pickups.Count));
            g.God = true; g.DamageTaken = 0; g.DryClicks = 0; Array.Clear(g.DamageBy, 0, 7);
            int kills0 = g.kills;
            var shots0 = (int[])g.ShotsBy.Clone();
            float t = 0, lastLog = 0, lx = g.px, lz = g.pz, stuckT = 0;
            int lastKills = g.kills;
            const float dt = 1f / 60;
            float yLo = 99, yHi = -99;
            int hunters0 = g.HuntersSent;
            while (t < 1500)
            {
                g.Update(dt, g.BotInput());
                t += dt;
                foreach (var d in g.drones) if (d.st == 1 && d.alert && d.type == 2) { yLo = Math.Min(yLo, d.y); yHi = Math.Max(yHi, d.y); }
                if (g.State == St.Warp || g.State == St.Ending || g.State == St.Dead) break;
                if (t - lastLog > 10)
                {
                    lastLog = t;
                    float moved = Sqrt((g.px - lx) * (g.px - lx) + (g.pz - lz) * (g.pz - lz));
                    stuckT = moved < 0.5f && g.kills == lastKills ? stuckT + 10 : 0;
                    lx = g.px; lz = g.pz; lastKills = g.kills;
                    if (stuckT >= 40) { log.AppendLine(string.Format("  STUCK at ({0:F1},{1:F1}) t={2:F0}s", g.px, g.pz, t)); break; }
                }
            }
            string outcome = g.State == St.Warp ? "portal reached" : g.State == St.Ending ? "Elon rescued"
                : g.State == St.Dead ? (g.girlDead ? "ELON KILLED" : "player died") : "NOT FINISHED";
            log.AppendLine(string.Format("  {0} after {1:F0} s, kills {2}, damage taken {3:F0}, drones left {4}, empty-gun clicks {5}{6}",
                outcome, t, g.kills - kills0, g.DamageTaken, g.Remaining(), g.DryClicks, lvl == 2 ? ", boss dead: " + g.bossDead : ""));
            log.AppendLine(string.Format("  gunner flying height {0:F1} .. {1:F1} m, hidden-timer hunters {2}", yLo, yHi, g.HuntersSent - hunters0));
            log.AppendLine(string.Format("  damage from: gunners {0:F0}, heavies {1:F0}, boss {2:F0}, kamikazes {3:F0}, own rockets {4:F0}",
                g.DamageBy[2], g.DamageBy[3], g.DamageBy[4], g.DamageBy[5], g.DamageBy[6]));
            log.AppendLine(string.Format("  shots: blaster {0}, shotgun {1}, rockets {2}, plasma {3}; ammo now {4}",
                g.ShotsBy[1] - shots0[1], g.ShotsBy[2] - shots0[2], g.ShotsBy[3] - shots0[3], g.ShotsBy[4] - shots0[4], string.Join("/", g.ammo)));
            if (g.State != St.Warp && g.State != St.Ending) break;
        }
        }
        log.AppendLine("pickups collected by type 1..12: " + string.Join(" ", g.PickedUp.Skip(1)));
        log.AppendLine("--- results screen of the last run:");
        foreach (var line in g.StatsLines()) log.AppendLine("  " + line);
        log.AppendLine("  RANK " + g.Rank());
        log.AppendLine(string.Format("hits on Elon: {0} (blaster {1}, pellets {2}, rocket blasts {3}, plasma {4}), his health left: {5}",
            g.GirlHits, g.GirlHitsBy[1], g.GirlHitsBy[2], g.GirlHitsBy[3], g.GirlHitsBy[4], g.girlHp));
        File.WriteAllText(Path.Combine(dir, "bot.txt"), log.ToString());
        return 0;
    }
}
