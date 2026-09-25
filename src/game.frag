#version 120
// Save Elon! The whole world is one formula plus a tiny texture saying where the maze walls are.
uniform vec2  uRes;
uniform float uTime;
uniform vec3  uCamPos;
uniform vec3  uCamF;
uniform vec3  uCamR;
uniform vec3  uCamU;
uniform sampler2D uWalls;  // one texel per lattice point: r = wall towards +z, g = wall towards +x
uniform vec4  uGrid;      // lattice size (N+1, M+1) and its reciprocal
uniform vec4  uRoom;      // arena without pillars: x0 z0 x1 z1
uniform float uTheme;     // 0 catacombs, 1 foundry, 2 sanctum
uniform vec4  uD[8];      // drones: xyz, w: type 1..4 alive, <0 exploding (-progress), 0 none
uniform vec4  uDX[8];     // x: hit flash, y: kamikaze charging, z: type, w: boss awake
uniform vec4  uG[24];     // glowing projectiles: xyz, w: 1 enemy bullet, 3 heavy bullet, 4 rocket, 5 plasma, 6 pellet, 7..8 spark (fraction = age)
uniform vec4  uP[12];     // pickups, nearest first: xyz, w: item type (0 = end of list)
uniform vec4  uB[4];      // rocket blasts: xyz, w: progress (0 none)
uniform float uWeapon;    // 1 blaster, 2 shotgun, 3 rocket launcher, 4 plasma gun
uniform float uSwitch;    // 0 raised .. 1 lowered while switching weapons
uniform vec4  uExit;      // portal / cage position, w: portal open (0/1) or cage bar height (1..0); -1 none
uniform vec4  uGirl;      // xyz, w: -1 absent, 0 captive, 1 free
uniform float uGirlYaw;
uniform float uGirlHit;   // red flash when the player hits him
uniform vec4  uTr0;
uniform vec4  uTr1;       // tracer end, w: alpha
uniform float uRecoil;
uniform float uFlash;
uniform float uHurt;
uniform float uHealth;
uniform float uHit;
uniform float uPause;
uniform float uBoss;
uniform float uFade;
uniform float uWhite;
uniform float uHud;
uniform float uMap;       // automap fade in (Tab)
uniform float uGray;      // black-and-white death screen
uniform vec4  uGate[6];   // arena gates: x, z, orientation (-1 = none), how far raised
uniform float uArmor;
uniform float uDoorLocked;
uniform vec4  uPath[64];  // route to the goal: two points (x, z) per entry
uniform float uPathN;     // number of route points
uniform float uDebug;     // test switch: 1 no shadows, 2 no neighbour lamps, 3 no AO, 4 no scattering, 5 lamps only
uniform vec2  uBob;

const float CELL = 6.0;
const float CEIL = 5.4;

// ---------------------------------------------------------------- noise & helpers
float hash12(vec2 p){ vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float noise(vec2 p){
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
               mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p){ float a = 0.5, s = 0.0; for (int i = 0; i < 4; i++){ s += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; } return s; }

float sdBox(vec3 p, vec3 b){ vec3 q = abs(p) - b; return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0); }
vec2  opU(vec2 a, vec2 b){ return (a.x < b.x) ? a : b; }
mat2  rot(float a){ float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
float smin(float a, float b, float k){ float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0); return mix(b, a, h) - k * h * (1.0 - h); }
float sdCapsule(vec3 p, vec3 a, vec3 b, float r){ vec3 pa = p - a, ba = b - a; float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0); return length(pa - ba * h) - r; }
float sdEllipsoid(vec3 p, vec3 r){ float k0 = length(p / r); float k1 = length(p / (r * r)); return k0 * (k0 - 1.0) / k1; }
float sdCappedCone(vec3 p, float h, float r1, float r2){
    vec2 q = vec2(length(p.xz), p.y);
    vec2 k1 = vec2(r2, h);
    vec2 k2 = vec2(r2 - r1, 2.0 * h);
    vec2 ca = vec2(q.x - min(q.x, (q.y < 0.0) ? r1 : r2), abs(q.y) - h);
    vec2 cb = q - k1 + k2 * clamp(dot(k1 - q, k2) / dot(k2, k2), 0.0, 1.0);
    float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
    return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
}

vec3 themeLamp(){   return uTheme < 0.5 ? vec3(1.0, 0.62, 0.30) : (uTheme < 1.5 ? vec3(0.50, 0.70, 0.92) : vec3(0.95, 0.52, 0.60)); }
vec3 themeAccent(){ return uTheme < 0.5 ? vec3(1.0, 0.70, 0.35) : (uTheme < 1.5 ? vec3(1.00, 0.45, 0.10) : vec3(0.75, 0.25, 1.00)); }
vec3 themeFog(){    return uTheme < 0.5 ? vec3(0.045, 0.032, 0.024) : (uTheme < 1.5 ? vec3(0.018, 0.026, 0.034) : vec3(0.030, 0.012, 0.028)); }

vec2 edgeAt(vec2 ij){
    if (ij.x < 0.0 || ij.y < 0.0 || ij.x > uGrid.x - 0.5 || ij.y > uGrid.y - 0.5) return vec2(0.0);
    return texture2D(uWalls, (ij + 0.5) * uGrid.zw).rg;
}
float sdRect(vec2 p, vec2 b){ vec2 q = abs(p) - b; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0); }

// exact distance to the maze walls: only the four wall slots touching the nearest lattice point can be close
float wallDist(vec2 xz){
    vec2 li = floor(xz / CELL + 0.5);
    vec2 q = xz - li * CELL;
    vec2 e0 = edgeAt(li), ed = edgeAt(li - vec2(0.0, 1.0)), el = edgeAt(li - vec2(1.0, 0.0));
    float d = 2.6;
    if (e0.x > 0.5) d = min(d, sdRect(q - vec2(0.0, 3.0), vec2(0.4, 3.0)));
    if (ed.x > 0.5) d = min(d, sdRect(q + vec2(0.0, 3.0), vec2(0.4, 3.0)));
    if (e0.y > 0.5) d = min(d, sdRect(q - vec2(3.0, 0.0), vec2(3.0, 0.4)));
    if (el.y > 0.5) d = min(d, sdRect(q + vec2(3.0, 0.0), vec2(3.0, 0.4)));
    vec2 hs = (uGrid.xy - 1.0) * CELL * 0.5;
    return min(d, -sdRect(xz - hs, hs));
}
bool  inRoom(vec2 q){ return q.x > uRoom.x && q.x < uRoom.z && q.y > uRoom.y && q.y < uRoom.w; }

// lintel with an arch cut out of it, running along one lattice line
float archLine(float across, float along, float y){
    float a = max(abs(across) - 0.3, 2.9 - y);
    return max(a, -(length(vec2(along, max(y - 2.9, 0.0))) - 2.3));
}

// ---------------------------------------------------------------- drones
float droneSDF(vec3 d, float typ, float fi, float ch, vec3 toCam){
    float spin = uTime * (3.0 + 9.0 * ch) + fi * 1.7;
    if (typ < 1.5){                       // kamikaze: orb with a spinning circular saw
        float s = length(d) - 0.36;
        // the blade always turns its face towards you, like a buzz saw flying at your head
        vec3 bu = normalize(cross(toCam, vec3(0.0, 1.0, 0.001))), bv = cross(bu, toCam);
        vec3 q = vec3(dot(d, bu), dot(d, toCam), dot(d, bv));
        q.xz = rot(spin) * q.xz;
        float tooth = fract(atan(q.z, q.x) * 2.5465);          // 16 teeth
        float blade = max(length(q.xz) - (0.55 + 0.11 * (1.0 - tooth)), abs(q.y) - 0.014) * 0.8;
        return min(s, blade);
    } else if (typ < 2.5){                // gunner: faceted core with twin cannons
        vec3 q = d; q.xz = rot(spin * 0.4) * q.xz;
        float core = max((abs(q.x) + abs(q.y) + abs(q.z) - 0.6) * 0.57735, length(q) - 0.44);
        vec3 c = vec3(abs(q.x) - 0.42, q.y, q.z);
        float pod = max(length(c.yz) - 0.09, abs(c.x) - 0.14);
        float barrel = max(length(c.xy - vec2(0.0, 0.0)) - 0.035, abs(q.z - 0.25) - 0.25);
        return min(core, min(pod, barrel));
    } else if (typ < 3.5){                // heavy: big armoured sphere with gyroscope bands
        d /= 1.3;
        float s = length(d) - 0.55;
        vec3 a = d; a.xy = rot(spin * 0.6) * a.xy;
        float b1 = length(vec2(length(a.xz) - 0.72, a.y)) - 0.07;
        vec3 b = d; b.yz = rot(spin * 0.45 + 1.0) * b.yz;
        float b2 = length(vec2(length(b.xy) - 0.72, b.z)) - 0.07;
        vec3 sp = abs(d);
        float spikes = length(sp - vec3(0.0, 0.62, 0.0)) - 0.09;
        return min(s, min(min(b1, b2), spikes)) * 1.3;
    }
    // boss: core, two counter-rotating slotted caps and a bladed halo
    float core = length(d) - 0.95;
    vec3 q = d; q.xz = rot(uTime * (d.y > 0.0 ? 0.8 : -0.8)) * q.xz;
    float shell = abs(length(q) - 1.3) - 0.07;
    shell = max(shell, 0.55 - abs(q.y));
    float slot = abs(fract(atan(q.z, q.x) * 0.95493) - 0.5) - 0.07;   // 6 slots
    shell = max(shell, -max(slot * 1.2, 0.8 - abs(q.y)));
    vec3 h = d; h.xz = rot(-uTime * 1.6) * h.xz;
    float halo = length(vec2(length(h.xz) - 1.75, h.y)) - 0.06;
    float sec = 1.5707963;
    float a = atan(h.z, h.x);
    float ai = floor(a / sec + 0.5) * sec;
    vec3 bl = h; bl.xz = rot(ai) * bl.xz;
    float blade = sdBox(bl - vec3(1.75, 0.0, 0.0), vec3(0.35, 0.05, 0.12));
    return min(min(core, shell), min(halo, blade));
}

// ---------------------------------------------------------------- the captive: Elon
// his own frame: facing the player, swaying a little, falling backwards if shot
vec3 girlLocal(vec3 g){
    vec3 p = g;
    p.xz = rot(uGirlYaw) * p.xz;
    if (uGirl.w > 1.5){ float f = clamp(uGirl.w - 2.0, 0.0, 1.0); p.yz = rot(-f * f * 1.45) * p.yz; }
    p.x += sin(uTime * 1.3) * 0.012 * p.y;
    return p;
}

float girlFree(){ return uGirl.w > 0.5 && uGirl.w < 1.5 ? 1.0 : 0.0; }
vec3 girlHead(){ return girlFree() > 0.5 ? vec3(0.0, 1.72, 0.0) : vec3(0.0, 1.7, 0.03); }

// elbows and wrists (+x is his left): hands clasped while captive, waving when free
void girlArms(out vec3 el, out vec3 wl, out vec3 er, out vec3 wr){
    if (girlFree() > 0.5){
        float wv = sin(uTime * 7.0) * 0.08;
        el = vec3(0.3, 1.21, 0.03); wl = vec3(0.3, 0.95, 0.07);
        er = vec3(-0.38, 1.62, 0.05); wr = vec3(-0.37 + wv, 1.88, 0.07);
    } else {
        el = vec3(0.3, 1.21, 0.07); wl = vec3(0.08, 1.1, 0.19);
        er = vec3(-0.3, 1.21, 0.07); wr = vec3(-0.08, 1.1, 0.19);
    }
}

// materials: 10 skin, 11 black T-shirt, 12 hair, 16 jeans, 17 shoes
float girlSDF(vec3 g, out float m){
    vec3 p = girlLocal(g);
    vec3 hc = girlHead();
    vec3 lp = vec3(abs(p.x), p.y, p.z);
    float legs = min(sdCapsule(lp, vec3(0.1, 0.95, 0.0), vec3(0.105, 0.52, 0.01), 0.078),
                     sdCapsule(lp, vec3(0.105, 0.52, 0.01), vec3(0.1, 0.12, 0.0), 0.066));
    float jeans = smin(legs, sdEllipsoid(p - vec3(0.0, 0.97, 0.0), vec3(0.19, 0.11, 0.11)), 0.04);
    float shoe = sdBox(lp - vec3(0.1, 0.05, 0.035), vec3(0.055, 0.04, 0.12)) - 0.02;
    float torso = smin(sdEllipsoid(p - vec3(0.0, 1.34, 0.0), vec3(0.215, 0.24, 0.125)),
                       sdEllipsoid(p - vec3(0.0, 1.1, 0.01), vec3(0.2, 0.15, 0.125)), 0.08);
    vec3 el, wl, er, wr; girlArms(el, wl, er, wr);
    vec3 sl = vec3(0.2, 1.47, 0.0), sr = vec3(-0.2, 1.47, 0.0);
    float sleeves = min(sdCapsule(p, sl, mix(sl, el, 0.45), 0.064), sdCapsule(p, sr, mix(sr, er, 0.45), 0.064));
    float shirt = smin(torso, sleeves, 0.015);
    float arms = min(min(sdCapsule(p, sl, el, 0.056), sdCapsule(p, el, wl, 0.048)),
                     min(sdCapsule(p, sr, er, 0.056), sdCapsule(p, er, wr, 0.048)));
    float hands = min(length(p - (wl + normalize(wl - el) * 0.08)) - 0.048, length(p - (wr + normalize(wr - er) * 0.08)) - 0.048);
    float neck = sdCapsule(p, vec3(0.0, 1.5, 0.0), vec3(0.0, hc.y - 0.08, hc.z * 0.5), 0.058);
    vec3 hp = p - hc;
    float head = smin(sdEllipsoid(hp, vec3(0.097, 0.128, 0.108)),
                      sdEllipsoid(hp - vec3(0.0, -0.065, 0.02), vec3(0.085, 0.07, 0.09)), 0.04);   // skull and a square jaw
    float ears = sdEllipsoid(vec3(abs(hp.x), hp.y, hp.z) - vec3(0.098, -0.005, -0.01), vec3(0.018, 0.034, 0.024));
    float nose = sdCapsule(hp, vec3(0.0, -0.004, 0.1), vec3(0.0, -0.042, 0.117), 0.016);
    head = min(smin(head, nose, 0.015), ears);
    // short dark hair, swept up at the front
    // a layer over the skull that thins out to nothing at the hairline, thickest in the quiff on top
    float line = 0.075 - 0.025 * smoothstep(0.02, 0.07, abs(hp.x)) + (hp.z > 0.0 ? 0.1 : 0.9) * hp.z;
    float th = smoothstep(line - 0.008, line + 0.02, hp.y) * (0.01 + 0.028 * smoothstep(0.04, 0.12, hp.y) * smoothstep(-0.06, 0.05, hp.z));
    float hair = sdEllipsoid(hp, vec3(0.097, 0.128, 0.108)) - th;
    float skin = smin(smin(neck, head, 0.03), min(arms, hands), 0.02);
    float d = skin; m = 10.0;
    if (shirt < d){ d = shirt; m = 11.0; }
    if (hair < d - 0.002){ d = hair; m = 12.0; }   // the thin edge of the hairline stays skin: no speckles
    if (jeans < d){ d = jeans; m = 16.0; }
    if (shoe < d){ d = shoe; m = 17.0; }
    return d;
}

// Elon's colours, with the face painted on: eyes, brows and a big grin
vec3 girlColor(vec3 p, vec3 n, float m, inout vec3 emi, inout float spec){
    vec3 q = girlLocal(p - uGirl.xyz);
    vec3 nl = n; nl.xz = rot(uGirlYaw) * nl.xz;
    vec3 hc = girlHead();
    vec3 skin = vec3(0.93, 0.74, 0.63);
    spec = 0.25;
    if (m < 10.5){
        vec3 c = skin;
        vec3 fc = q - hc;
        if (fc.z > 0.04 && fc.y > -0.12 && fc.y < 0.06){
            vec2 uv = fc.xy;
            float side = uv.x > 0.0 ? 1.0 : -1.0;
            vec2 e = (uv - vec2(0.036 * side, -0.002)) / vec2(0.018, 0.009);
            float er = length(e);
            if (er < 1.0){
                c = vec3(0.95, 0.93, 0.9);
                float il = length(uv - vec2(0.034 * side, -0.002));
                if (il < 0.0078) c = il < 0.0035 ? vec3(0.03) : vec3(0.28, 0.42, 0.48);
                spec = 0.8;
            } else if (er < 1.35 && e.y > 0.0) c = vec3(0.3, 0.18, 0.14);                   // upper lids
            float bx = uv.x - 0.036 * side;
            if (abs(bx) < 0.024 && abs(uv.y - 0.02 + bx * side * 0.2) < 0.0045) c = vec3(0.22, 0.14, 0.09);   // brows
            float my = -0.077 + 16.0 * uv.x * uv.x;
            if (abs(uv.x) < 0.033 && abs(uv.y - my) < 0.0038) c = vec3(0.45, 0.2, 0.2);      // the grin
            if (abs(uv.x) < 0.024 && uv.y < my - 0.0038 && uv.y > -0.087 + 8.0 * uv.x * uv.x) c = vec3(0.95);   // teeth
        }
        return c;
    }
    if (m < 11.5){   // black T-shirt with an X on the chest
        vec2 x = q.xy - vec2(0.0, 1.36);
        spec = 0.2;
        if (nl.z > 0.3 && q.z > 0.0 && abs(x.x) < 0.05 && abs(x.y) < 0.05 && min(abs(x.x - x.y), abs(x.x + x.y)) < 0.011) return vec3(0.85);
        return vec3(0.055, 0.055, 0.065);
    }
    if (m < 12.5){ spec = 0.5; return vec3(0.09, 0.06, 0.04) * (0.85 + 0.3 * noise(vec2(q.x * 300.0, q.y * 60.0))); }
    if (m < 16.5){   // jeans and a belt
        spec = 0.15;
        if (q.y > 0.975 && q.y < 1.01) return abs(q.x) < 0.022 && q.z > 0.0 ? vec3(0.7) : vec3(0.07, 0.045, 0.03);
        return vec3(0.1, 0.14, 0.24) * (0.85 + 0.15 * noise(q.xy * 120.0));
    }
    spec = 0.6;
    return vec3(0.03, 0.03, 0.035);
}

// ---------------------------------------------------------------- weapons
// gun space: +z forward, origin in the middle. y = material: 0 metal, 1 wood, 2 energy, 3 red, 4 olive paint
vec2 weaponSDF(vec3 p, float w){
    if (w < 1.5){                                   // blaster
        float body = sdBox(p, vec3(0.07, 0.06, 0.28)) - 0.02;
        float top  = sdBox(p - vec3(0.0, 0.085, -0.06), vec3(0.035, 0.025, 0.2)) - 0.01;
        float bl   = length(p.xy - vec2(0.0, 0.01));
        float bar  = max(max(bl - 0.042, abs(p.z - 0.36) - 0.12), -(bl - 0.022));
        float grip = sdBox(p - vec3(0.0, -0.13, -0.15), vec3(0.04, 0.09, 0.05)) - 0.015;
        float d = min(min(body, top), min(bar, grip));
        float groove = sdBox(vec3(abs(p.x) - 0.09, p.y, mod(p.z, 0.07) - 0.035), vec3(0.012, 0.045, 0.012));
        return vec2(max(d, -groove), 0.0);
    }
    if (w < 2.5){                                   // double-barrelled shotgun
        float bl = length(vec2(abs(p.x) - 0.036, p.y - 0.02));
        float bar = max(max(bl - 0.032, abs(p.z - 0.2) - 0.32), -(bl - 0.019));
        float recv = sdBox(p - vec3(0.0, 0.0, -0.17), vec3(0.075, 0.065, 0.1)) - 0.01;
        float grip = sdBox(p - vec3(0.0, -0.1, -0.24), vec3(0.035, 0.08, 0.045)) - 0.01;
        float metal = min(min(bar, recv), grip);
        float fore = sdBox(p - vec3(0.0, -0.035, 0.12), vec3(0.068, 0.03, 0.12)) - 0.012;
        float stock = sdBox(p - vec3(0.0, -0.05, -0.42), vec3(0.045, 0.075, 0.16)) - 0.015;
        float wood = min(fore, stock);
        return wood < metal ? vec2(wood, 1.0) : vec2(metal, 0.0);
    }
    if (w < 3.5){                                   // rocket launcher
        float r = length(p.xy);
        float tube = max(r - 0.085, abs(p.z) - 0.46);
        tube = max(tube, -max(r - 0.066, abs(p.z - 0.35) - 0.12));
        float rings = min(length(vec2(r - 0.088, p.z - 0.3)), length(vec2(r - 0.088, p.z + 0.3))) - 0.014;
        float grip = sdBox(p - vec3(0.0, -0.13, -0.05), vec3(0.035, 0.08, 0.045)) - 0.01;
        float sight = sdBox(p - vec3(0.0, 0.1, 0.1), vec3(0.012, 0.025, 0.03));
        float warhead = length(p - vec3(0.0, 0.0, 0.36)) - 0.05;
        float body = min(tube, sight);
        float metal = min(rings, grip);
        vec2 res = body < metal ? vec2(body, 4.0) : vec2(metal, 0.0);
        return warhead < res.x ? vec2(warhead, 3.0) : res;
    }
    // plasma gun
    float body = sdBox(p - vec3(0.0, 0.0, -0.05), vec3(0.085, 0.07, 0.22)) - 0.03;
    float bl = length(p.xy);
    float bar = max(bl - 0.038, abs(p.z - 0.33) - 0.18);
    float grip = sdBox(p - vec3(0.0, -0.13, -0.12), vec3(0.04, 0.09, 0.05)) - 0.015;
    float coils = max(length(vec2(bl - 0.062, mod(p.z + 0.04, 0.08) - 0.04)) - 0.016, abs(p.z - 0.33) - 0.13);
    float strip = sdBox(p - vec3(0.0, 0.1, -0.05), vec3(0.02, 0.012, 0.18));
    float metal = min(min(body, bar), grip);
    float energy = min(coils, strip);
    return energy < metal ? vec2(energy, 2.0) : vec2(metal, 0.0);
}

// heater-shield outline: flat top, curved sides meeting in a point at the bottom
float heater(vec2 q){
    float w = q.y > 0.0 ? 0.14 : 0.14 * sqrt(clamp((q.y + 0.21) / 0.21, 0.0, 1.0));
    return max(max(abs(q.x) - w, q.y - 0.15), -0.21 - q.y) * 0.7;
}

float sdStar5(vec2 p, float r, float rf){
    const vec2 k1 = vec2(0.809016994375, -0.587785252292);
    const vec2 k2 = vec2(-0.809016994375, -0.587785252292);
    p.x = abs(p.x);
    p -= 2.0 * max(dot(k1, p), 0.0) * k1;
    p -= 2.0 * max(dot(k2, p), 0.0) * k2;
    p.x = abs(p.x);
    p.y -= r;
    vec2 ba = rf * vec2(-k1.y, k1.x) - vec2(0.0, 1.0);
    float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
    return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
}

// pickups in their own rotating frame
vec2 pickupSDF(vec3 q, float t){
    if (t > 11.5) return vec2(max(heater(q.xy), abs(q.z) - 0.022 + 0.03 * (q.x * q.x + q.y * q.y)) - 0.008, 0.0);   // shield, slightly domed
    if (t > 10.5) return vec2(sdBox(q, vec3(0.17, 0.11, 0.008)) - 0.006, 0.0);               // key card
    if (t > 9.5) return vec2(max(length(q.xz) - 0.09, abs(q.y) - 0.16) - 0.012, 0.0);   // energy drink
    if (t < 1.5) return vec2(sdBox(q, vec3(0.2, 0.13, 0.14)) - 0.02, 0.0);
    if (t < 2.5) return vec2(sdBox(q, vec3(0.32, 0.2, 0.22)) - 0.03, 0.0);
    if (t < 3.5) return vec2(sdBox(q, vec3(0.17, 0.1, 0.11)) - 0.012, 0.0);
    if (t < 4.5) return vec2(sdBox(q, vec3(0.2, 0.1, 0.11)) - 0.012, 0.0);
    if (t < 5.5) return vec2(sdCapsule(vec3(q.x, q.y, abs(q.z) - 0.08), vec3(-0.26, 0.0, 0.0), vec3(0.26, 0.0, 0.0), 0.065), 0.0);
    if (t < 6.5) return vec2(max(length(q.xz) - 0.13, abs(q.y) - 0.19) - 0.01, 0.0);
    vec2 w = weaponSDF(q / 1.3, t - 5.0);
    return vec2(w.x * 1.3, w.y);
}

// ---------------------------------------------------------------- the world
// materials: 1 floor, 2 stone, 3 accent ring, 4 wall, 5 metal, 6 drone, 7 ceiling, 8 lamp, 9 energy, 10-13 girl
bool gShadowRay = false;   // set while tracing shadow rays towards the lamps

vec2 map(vec3 p){
    vec2 res = vec2(wallDist(p.xz), 4.0);
    res = opU(res, vec2(p.y, 1.0));
    res = opU(res, vec2(CEIL - p.y, 7.0));

    vec2 cellC = CELL * (floor(p.xz / CELL) + 0.5);
    vec2 lat   = CELL * floor(p.xz / CELL + 0.5);
    vec2 lq = p.xz - lat;
    vec2 cq = p.xz - cellC;

    if (!inRoom(lat)){
        float r = length(lq);
        float pil = r - 0.46;
        if (r < 0.9) pil -= 0.025 * smoothstep(0.9, 0.6, r) * cos(atan(lq.y, lq.x) * 12.0);   // flutes, only worth computing up close
        pil = min(pil, sdBox(vec3(lq.x, p.y - 0.3, lq.y), vec3(0.66, 0.3, 0.66)) - 0.03);
        pil = min(pil, sdBox(vec3(lq.x, p.y - 4.9, lq.y), vec3(0.62, 0.5, 0.62)) - 0.03);
        res = opU(res, vec2(pil, 2.0));
        float ring = max(abs(r - 0.47) - 0.05, abs(p.y - 2.3) - 0.09);
        res = opU(res, vec2(ring, 3.0));
    }
    float ar = 1e3;
    if (!inRoom(vec2(lat.x, cellC.y))) ar = archLine(lq.x, cq.y, p.y);
    if (!inRoom(vec2(cellC.x, lat.y))) ar = min(ar, archLine(lq.y, cq.x, p.y));
    res = opU(res, vec2(ar, 2.0));

    // a hanging lamp in the middle of every cell (it does not shadow its own light)
    if (!gShadowRay){
        vec3 lp = vec3(cq.x, p.y - 4.3, cq.y);
        res = opU(res, vec2(length(lp) - 0.15, 8.0));
        float cage = length(vec2(length(lp.xz) - 0.24, lp.y)) - 0.022;
        float chain = max(length(lp.xz) - 0.02, abs(p.y - 4.9) - 0.5);
        res = opU(res, vec2(min(cage, chain), 5.0));
    }

    // portal pad or captive's cage
    if (uExit.w > -0.5){
        vec3 e = p - vec3(uExit.x, 0.0, uExit.z);
        float be = length(e - vec3(0.0, 1.5, 0.0)) - 2.4;
        if (gShadowRay && uTheme < 1.5) {}
        else if (be > 0.3 && !gShadowRay) res = opU(res, vec2(be, 0.0));
        else {
            float rr = length(e.xz);
            float pad = max(rr - 1.45, abs(e.y - 0.06) - 0.06);
            float tor = length(vec2(rr - 1.3, e.y - 0.14)) - 0.07;
            res = opU(res, vec2(min(pad, tor), 5.0));
            res = opU(res, vec2(max(rr - 1.15, abs(e.y - 0.13) - 0.015), 9.0));
            if (uTheme > 1.5 && uExit.w > 0.01){
                float h = 3.2 * uExit.w;
                float sec = 0.5235988;
                float ai = floor(atan(e.z, e.x) / sec + 0.5) * sec;
                float bar = max(length(e.xz - vec2(cos(ai), sin(ai)) * 1.3) - 0.04, abs(e.y - h * 0.5) - h * 0.5);
                float crown = length(vec2(rr - 1.3, e.y - h)) - 0.06;
                res = opU(res, vec2(min(bar, crown), 9.0));
            }
        }
    }

    for (int i = 0; i < 6; i++){
        vec4 G = uGate[i];
        if (G.z < -0.5) break;
        vec2 o = p.xz - G.xy;
        float along = G.z < 0.5 ? o.y : o.x, across = G.z < 0.5 ? o.x : o.y;
        float bb = max(abs(across) - 0.2, abs(along) - 2.8);
        if (bb > 0.3){ res = opU(res, vec2(bb, 0.0)); continue; }
        // two heavy panels: the lower one sinks into the floor, the upper one rises into the ceiling
        float slab = max(abs(across) - 0.14, abs(along) - 2.75);
        float lowTop = 2.6 * (1.0 - G.w), upBot = 2.6 + G.w * (CEIL - 2.6);
        res = opU(res, vec2(min(max(slab, p.y - lowTop), max(slab, upBot - p.y)), 20.0));
    }

    for (int i = 0; i < 8; i++){
        vec4 D = uD[i];
        if (D.w > 0.5){
            vec3 d = p - D.xyz;
            float R = D.w < 2.5 ? 0.75 : (D.w < 3.5 ? 1.35 : 2.1);
            float bd = length(d) - R;
            if (bd > 0.3) res = opU(res, vec2(gShadowRay ? bd + 0.25 : bd, 0.0));
            else res = opU(res, vec2(droneSDF(d, D.w, float(i), uDX[i].y, normalize(uCamPos - D.xyz)), 6.0));
        }
    }

    for (int i = 0; i < 12; i++){
        vec4 P = uP[i];
        if (P.w < 0.5 || gShadowRay) break;
        vec3 q = p - P.xyz;
        float bd = length(q) - 0.75;
        if (bd > 0.3){ res = opU(res, vec2(bd, 0.0)); continue; }
        q.xz = rot(uTime * 1.5 + float(i)) * q.xz;
        res = opU(res, vec2(pickupSDF(q, P.w).x, 14.0));
    }

    if (uGirl.w > -0.5){
        vec3 g = p - uGirl.xyz;
        float bd = uGirl.w > 1.5 ? length(g) - 2.2 : length(g - vec3(0.0, 1.0, 0.0)) - 1.1;
        if (bd > 0.3) res = opU(res, vec2(gShadowRay ? bd + 0.3 : bd, 0.0));
        else { float gm; float gd = girlSDF(g, gm); res = opU(res, vec2(gd, gm)); }
    }
    return res;
}

vec3 calcNormal(vec3 p){
    const vec2 k = vec2(1.0, -1.0);
    const float h = 0.001;
    return normalize(k.xyy * map(p + k.xyy * h).x + k.yyx * map(p + k.yyx * h).x +
                     k.yxy * map(p + k.yxy * h).x + k.xxx * map(p + k.xxx * h).x);
}

vec2 march(vec3 ro, vec3 rd){
    float t = 0.02, m = -1.0;
    for (int i = 0; i < 150; i++){
        vec2 h = map(ro + rd * t);
        if (h.x < 0.001 * t){ m = h.y; break; }
        t += h.x;
        if (t > 120.0) break;
    }
    return vec2(t, m);
}

vec2 marchR(vec3 ro, vec3 rd){
    float t = 0.02, m = -1.0;
    vec2 h = vec2(1.0, -1.0);
    for (int i = 0; i < 60; i++){
        h = map(ro + rd * t);
        if (h.x < 0.005 * t) break;
        t += h.x;
        if (t > 50.0) break;
    }
    if (t < 50.0 && h.y > 0.5) m = h.y;
    return vec2(t, m);
}

// soft shadow: the closest miss along the ray, relative to the distance travelled, gives the penumbra
float softShadow(vec3 ro, vec3 rd, float tmax){
    gShadowRay = true;
    float res = 1.0, t = 0.04;
    for (int i = 0; i < 32; i++){
        float h = map(ro + rd * t).x;
        res = min(res, 8.0 * h / t);
        t += clamp(h, 0.03, 0.4);
        if (res < 0.005 || t > tmax) break;
    }
    gShadowRay = false;
    res = clamp(res, 0.0, 1.0);
    return res * res * (3.0 - 2.0 * res);
}

float calcAO(vec3 p, vec3 n){
    float occ = 0.0, sca = 1.0;
    for (int i = 0; i < 5; i++){
        float h = 0.02 + 0.15 * float(i) / 4.0;
        occ += (h - map(p + h * n).x) * sca;
        sca *= 0.9;
    }
    return clamp(1.0 - 2.5 * occ, 0.0, 1.0);
}

// ---------------------------------------------------------------- surfaces
vec3 woodCol(vec3 p, out float seam){
    const float W = 0.3, L = 2.4;
    float px = p.x / W;
    float id = floor(px), fx = fract(px);
    float pzz = (p.z + hash12(vec2(id, 7.0)) * L) / L;
    float idz = floor(pzz), fz = fract(pzz);
    float h = hash12(vec2(id, idz));
    float g = fbm(vec2(fx * 2.0 + h * 37.0, p.z * 0.25 + h * 13.0) * vec2(3.0, 4.0));
    float rings = 0.5 + 0.5 * sin((g * 5.0 + fx * 1.5 + h * 6.0) * 6.2831);
    vec3 c = mix(vec3(0.16, 0.07, 0.03), vec3(0.50, 0.27, 0.12), 0.35 + 0.4 * rings * h + 0.25 * h);
    c *= 0.8 + 0.4 * noise(vec2(p.x * 40.0, p.z * 2.0));
    seam = min(min(fx, 1.0 - fx) * W, min(fz, 1.0 - fz) * L);
    return c * mix(0.35, 1.0, smoothstep(0.0, 0.01, seam));
}

vec3 floorCol(vec3 p, out float gloss){
    if (uTheme < 0.5){
        float seam; vec3 c = woodCol(p, seam);
        gloss = smoothstep(0.0, 0.01, seam) * 0.8;
        return c;
    }
    if (uTheme < 1.5){
        // steel plates with diamond tread
        vec2 t = p.xz / 2.0, f = fract(t);
        float seam = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)) * 2.0;
        vec2 g = p.xz * 4.0, gf = fract(g);
        float alt = mod(floor(g.x) + floor(g.y), 2.0);
        float dl = alt < 0.5 ? abs(gf.x - gf.y) : abs(gf.x + gf.y - 1.0);
        float tread = smoothstep(0.12, 0.05, dl) * smoothstep(0.15, 0.3, min(min(gf.x, 1.0 - gf.x), min(gf.y, 1.0 - gf.y)) + 0.2);
        vec3 c = vec3(0.15, 0.16, 0.17) * (0.7 + 0.5 * fbm(p.xz * 1.3)) + tread * 0.06;
        c = mix(c, vec3(0.2, 0.1, 0.04), smoothstep(0.62, 0.8, fbm(p.xz * 0.7 + 4.0)) * 0.7);
        c *= mix(0.3, 1.0, smoothstep(0.0, 0.02, seam));
        gloss = 0.7 * (1.0 - tread * 0.6) * smoothstep(0.0, 0.02, seam);
        return c;
    }
    // polished black marble
    vec2 t = p.xz / 1.5, f = fract(t), id = floor(t);
    float seam = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)) * 1.5;
    float v = fbm(p.xz * 0.9 + id * 3.1);
    float vein = 1.0 - smoothstep(0.0, 0.035, abs(sin((p.x * 0.7 + p.z * 0.4 + v * 5.0) * 2.0)));
    float chk = mod(id.x + id.y, 2.0);
    vec3 c = mix(vec3(0.03, 0.025, 0.035), vec3(0.06, 0.05, 0.065), chk) + vein * vec3(0.25, 0.08, 0.14) * (0.5 + v);
    c *= mix(0.3, 1.0, smoothstep(0.0, 0.012, seam));
    gloss = smoothstep(0.0, 0.012, seam);
    return c;
}

vec3 wallCol(vec3 p, vec3 n, inout vec3 emi){
    vec2 uv = abs(n.x) > 0.5 ? p.zy : (abs(n.z) > 0.5 ? p.xy : p.xz);
    if (uTheme < 0.5){
        vec2 b = uv / vec2(1.1, 0.45);
        b.x += 0.5 * mod(floor(b.y), 2.0);
        vec2 f = fract(b), id = floor(b);
        float mortar = smoothstep(0.0, 0.04, min(min(f.x, 1.0 - f.x) * 1.1, min(f.y, 1.0 - f.y) * 0.45));
        vec3 c = vec3(0.30, 0.24, 0.19) * (0.6 + 0.5 * hash12(id)) * (0.7 + 0.6 * fbm(uv * 4.0));
        return c * mix(0.3, 1.0, mortar);
    }
    if (uTheme < 1.5){
        vec2 pn = uv / vec2(1.5, 1.2), f = fract(pn), id = floor(pn);
        float seam = min(min(f.x, 1.0 - f.x) * 1.5, min(f.y, 1.0 - f.y) * 1.2);
        vec3 c = mix(vec3(0.12, 0.14, 0.16), vec3(0.2, 0.22, 0.25), hash12(id)) * (0.75 + 0.5 * fbm(uv * 3.0));
        c = mix(c, vec3(0.25, 0.12, 0.05), smoothstep(0.55, 0.8, fbm(vec2(uv.x * 4.0, uv.y * 0.7))) * 0.6);
        vec2 corner = vec2(0.75, 0.6) - abs(f - 0.5) * vec2(1.5, 1.2);
        c += smoothstep(0.035, 0.02, length(corner - vec2(0.09))) * 0.12;
        c *= mix(0.25, 1.0, smoothstep(0.0, 0.025, seam));
        if (p.y < 0.45){
            float s = step(0.5, fract((uv.x + p.y) * 1.4));
            c = mix(vec3(0.02), vec3(0.55, 0.42, 0.05), s) * (0.7 + 0.3 * fbm(uv * 6.0));
        }
        return c;
    }
    vec2 b = uv / vec2(1.6, 0.8);
    b.x += 0.5 * mod(floor(b.y), 2.0);
    vec2 f = fract(b), id = floor(b);
    float mortar = smoothstep(0.0, 0.04, min(min(f.x, 1.0 - f.x) * 1.6, min(f.y, 1.0 - f.y) * 0.8));
    vec3 c = vec3(0.1, 0.08, 0.1) * (0.6 + 0.7 * hash12(id)) * (0.6 + 0.8 * fbm(uv * 2.5));
    float vein = 1.0 - smoothstep(0.0, 0.02, abs(fbm(uv * 1.3 + 3.0) - 0.5));
    emi += vec3(1.0, 0.15, 0.35) * vein * 0.16 * (0.7 + 0.3 * sin(uTime * 1.5 + uv.x * 2.0));
    return c * mix(0.3, 1.0, mortar);
}

// dressed stone blocks for pillars and arches
vec3 stoneCol(vec3 p, vec3 n){
    vec3 base = uTheme < 0.5 ? vec3(0.33, 0.26, 0.19) : (uTheme < 1.5 ? vec3(0.21, 0.22, 0.24) : vec3(0.15, 0.11, 0.16));
    vec2 uv = abs(n.y) > 0.6 ? p.xz : (abs(n.x) > abs(n.z) ? p.zy : p.xy);
    vec2 b = uv / vec2(0.9, 0.45);
    b.x += 0.5 * mod(floor(b.y), 2.0);
    vec2 f = fract(b);
    float joint = smoothstep(0.0, 0.03, min(min(f.x, 1.0 - f.x) * 0.9, min(f.y, 1.0 - f.y) * 0.45));
    return base * (0.75 + 0.35 * hash12(floor(b))) * (0.65 + 0.6 * fbm(uv * 3.0)) * mix(0.55, 1.0, joint);
}

vec3 ceilCol(vec3 p){
    if (uTheme < 0.5){ float s; return woodCol(vec3(p.z, 0.0, p.x), s) * 0.5; }
    if (uTheme < 1.5){
        vec2 g = abs(fract(p.xz * 2.0) - 0.5);
        return vec3(0.05, 0.055, 0.06) + smoothstep(0.42, 0.47, max(g.x, g.y)) * 0.06;
    }
    return vec3(0.06, 0.045, 0.06) * (0.6 + 0.8 * fbm(p.xz));
}

// ---------------------------------------------------------------- lighting
vec3 lampAt(vec2 c){ return vec3(c.x, 4.12, c.y); }
// one lamp in ten is failing: it dims now and then, smoothly, so it looks the same at any frame rate
float lampPower(vec2 c){
    float h = hash12(c * 0.37);
    if (h < 0.9) return 1.0;
    return 1.0 - 0.4 * smoothstep(0.55, 0.8, noise(vec2(uTime * 2.5, h * 57.0)));
}

vec3 shade(vec3 ro, vec3 rd, float t, float m, bool full, out vec3 n, out float gloss){
    vec3 p = ro + rd * t;
    n = calcNormal(p);
    vec3 alb = vec3(0.0), emi = vec3(0.0);
    float spec = 0.2;
    gloss = 0.0;
    if (m > 19.5){
        // arena door: steel panels, hazard stripes on the meeting edges, a red or green status light
        float bestD = 1e9, op = 0.0, al = 0.0;
        for (int i = 0; i < 6; i++){
            vec4 G = uGate[i];
            if (G.z < -0.5) break;
            vec2 o = p.xz - G.xy;
            float along = G.z < 0.5 ? o.y : o.x, across = G.z < 0.5 ? o.x : o.y;
            float dd = abs(across) + max(abs(along) - 2.75, 0.0);
            if (dd < bestD){ bestD = dd; al = along; op = G.w; }
        }
        float lowTop = 2.6 * (1.0 - op), upBot = 2.6 + op * (CEIL - 2.6);
        vec2 lc = vec2(al, p.y < lowTop + 0.01 ? p.y - lowTop : p.y - upBot);
        vec2 fs = abs(fract(lc / vec2(0.92, 0.9)) - 0.5);
        alb = vec3(0.17, 0.19, 0.22) * (0.8 + 0.3 * fbm(lc * 2.0)) * (1.0 - 0.6 * smoothstep(0.46, 0.49, max(fs.x, fs.y)));
        if (abs(lc.y) < 0.3) alb = mix(vec3(0.03), vec3(0.75, 0.6, 0.08), step(0.5, fract((al + p.y) * 2.5)));
        vec3 lightC = uDoorLocked > 0.5 ? vec3(1.0, 0.1, 0.05) : vec3(0.1, 1.0, 0.3);
        if (abs(p.y - (lowTop - 0.45)) < 0.05 && abs(al) < 1.8) emi = lightC * (3.0 + sin(uTime * 4.0));
        spec = 0.6;
    }
    else if (m < 1.5){ alb = floorCol(p, gloss); spec = 0.7; }
    else if (m < 2.5){ alb = stoneCol(p, n); spec = 0.15; }
    else if (m < 3.5){ emi = themeAccent() * 2.2 * (0.9 + 0.1 * sin(uTime * 5.0 + p.x + p.z)); }
    else if (m < 4.5){ alb = wallCol(p, n, emi); spec = uTheme > 0.5 && uTheme < 1.5 ? 0.3 : 0.1; }
    else if (m < 5.5){ alb = vec3(0.10, 0.10, 0.11); spec = 0.9; }
    else if (m < 6.5){
        vec3 c = uD[0].xyz; float bd = 1e9, typ = 1.0, fl = 0.0, awake = 0.0, ch = 0.0;
        for (int i = 0; i < 8; i++){
            float d = length(p - uD[i].xyz);
            if (uD[i].w > 0.5 && d < bd){ bd = d; c = uD[i].xyz; typ = uD[i].w; fl = uDX[i].x; awake = uDX[i].w; ch = uDX[i].y; }
        }
        vec3 eyeC; float eyeW;
        if (typ < 1.5){ alb = vec3(0.25, 0.22, 0.32); eyeC = mix(vec3(0.8, 0.3, 1.0), vec3(1.0, 0.1, 0.05), ch); eyeW = 0.93; }
        else if (typ < 2.5){ alb = vec3(0.32, 0.12, 0.10); eyeC = vec3(1.0, 0.15, 0.1); eyeW = 0.9; }
        else if (typ < 3.5){ alb = vec3(0.30, 0.24, 0.10); eyeC = vec3(1.0, 0.6, 0.1); eyeW = 0.88; }
        else { alb = vec3(0.12, 0.11, 0.14); eyeC = vec3(0.0); eyeW = 2.0; }
        spec = 1.0;
        vec3 dp = p - c;
        float eye = smoothstep(eyeW, eyeW + 0.04, dot(normalize(dp), normalize(uCamPos - c)));
        float core = typ > 3.5 ? step(length(dp), 1.0) : 1.0;
        emi = eyeC * eye * 5.0 * core + vec3(1.0) * fl * 1.5;
        emi += vec3(1.0, 0.12, 0.05) * ch * (0.5 + 0.5 * sin(uTime * 30.0)) * 0.9;   // a charging kamikaze pulses red
        if (typ > 3.5 && core > 0.5){
            vec3 tc = normalize(uCamPos - c);
            vec3 R = normalize(cross(vec3(0.0, 1.0, 0.0), tc)), U = cross(tc, R);
            vec2 uv = vec2(dot(dp, R), dot(dp, U)) / 0.95;
            if (dot(normalize(dp), tc) > 0.2){
                float r = length(uv), an = atan(uv.y, uv.x);
                float gap = min(abs(an - 0.785), abs(an + 2.356));        // the slash leaves the ring here
                float ring = step(abs(r - 0.62), 0.065) * step(0.34, gap);
                float slash = step(abs(uv.x - uv.y) * 0.7071, 0.06) * step(0.3, r) * step(r, 0.92);
                float mark = max(ring, slash);
                alb = mix(vec3(0.02), vec3(0.9), mark);
                emi = vec3(0.9, 0.92, 1.0) * mark * (0.3 + 0.9 * awake);
                if (r < 0.25){
                    alb = r > 0.21 ? vec3(0.6, 0.62, 0.66) : vec3(0.02);
                    float g = smoothstep(0.21, 0.0, r);
                    emi = mix(vec3(0.7, 0.0, 0.0), vec3(1.0, 0.85, 0.45), smoothstep(0.08, 0.0, r)) * g * (1.2 + 5.0 * awake);
                }
                emi += vec3(1.0) * fl * 1.5;
            }
        }
        if (typ < 1.5 && length(dp) > 0.4){ alb = vec3(0.55, 0.56, 0.6); emi = vec3(1.0, 0.35, 0.1) * ch * 0.4; }   // steel saw
    }
    else if (m < 7.5){ alb = ceilCol(p); spec = 0.05; }
    else if (m < 8.5){ emi = themeLamp() * 4.0 * lampPower(CELL * (floor(p.xz / CELL) + 0.5)); }
    else if (m < 9.5){
        vec3 ec = uTheme > 1.5 ? vec3(1.0, 0.2, 0.45) : (uExit.w > 0.5 ? vec3(0.3, 0.9, 1.0) : vec3(0.8, 0.1, 0.05));
        float sw = 0.6 + 0.4 * sin(length(p.xz - uExit.xz) * 9.0 - uTime * 6.0);
        emi = ec * (uTheme > 1.5 ? 3.0 : 1.5 + 1.5 * uExit.w) * sw;
    }
    else if (m < 13.5 || m > 14.5){ alb = girlColor(p, n, m, emi, spec); }
    else if (m < 14.5){
        float bd = 1e9, typ = 1.0; vec3 q = vec3(0.0), nl = n;
        for (int i = 0; i < 12; i++){
            vec4 P = uP[i];
            if (P.w < 0.5) break;
            float d = length(p - P.xyz);
            if (d < bd){
                bd = d; typ = P.w;
                mat2 R = rot(uTime * 1.5 + float(i));
                q = p - P.xyz; q.xz = R * q.xz;
                nl = n; nl.xz = R * nl.xz;
            }
        }
        vec2 fuv = abs(nl.y) > 0.5 ? q.xz : (abs(nl.x) > 0.5 ? q.zy : q.xy);
        spec = 0.5;
        if (typ > 11.5){} else if (typ > 11.5){
            float rim = smoothstep(-0.022, -0.012, heater(q.xy));
            float star = step(sdStar5(q.xy - vec2(0.0, -0.01), 0.07, 0.45), 0.0);
            alb = mix(mix(vec3(0.12, 0.3, 0.8), vec3(0.78, 0.82, 0.92), rim), vec3(0.95, 0.78, 0.25), star);
            emi = vec3(0.3, 0.6, 1.0) * rim * 0.7 + vec3(1.0, 0.75, 0.25) * star * 0.5;
            spec = 1.0;
        } else if (typ > 10.5){
            float stripe = step(abs(q.y - 0.05), 0.02);
            float chip = step(abs(q.x + 0.09), 0.03) * step(abs(q.y + 0.03), 0.025);
            alb = mix(mix(vec3(0.95, 0.75, 0.2), vec3(0.1), stripe), vec3(0.8, 0.8, 0.85), chip);
            emi = vec3(1.0, 0.8, 0.3) * (0.6 + 0.4 * sin(uTime * 5.0)) * (1.0 - stripe);
            spec = 1.0;
        } else if (typ > 9.5){
            alb = mix(vec3(0.1, 0.55, 0.15), vec3(0.75), step(0.13, abs(q.y)));
            float bolt = step(abs(q.y - 0.02 + abs(fract(atan(q.z, q.x) * 1.2732) - 0.5) * 0.08), 0.025);
            emi = vec3(0.5, 1.0, 0.2) * bolt * (1.5 + 0.5 * sin(uTime * 8.0));
        } else if (typ < 2.5){
            float k = typ < 1.5 ? 1.0 : 1.5;
            vec2 a = abs(fuv);
            float cross = max(step(a.x, 0.03 * k) * step(a.y, 0.09 * k), step(a.y, 0.03 * k) * step(a.x, 0.09 * k));
            alb = mix(vec3(0.8, 0.8, 0.78), vec3(0.75, 0.04, 0.03), cross);
            emi = vec3(0.9, 0.1, 0.05) * cross * 0.4;
        } else if (typ < 3.5){
            alb = mix(vec3(0.22, 0.26, 0.1), vec3(0.8, 0.6, 0.1), step(abs(q.y), 0.02));
        } else if (typ < 4.5){
            alb = mix(vec3(0.5, 0.07, 0.04), vec3(0.8, 0.6, 0.2), step(abs(q.y + 0.06), 0.03));
        } else if (typ < 5.5){
            alb = mix(vec3(0.28, 0.3, 0.25), vec3(0.7, 0.08, 0.04), step(0.2, abs(q.x)));
        } else if (typ < 6.5){
            alb = vec3(0.12, 0.13, 0.15);
            emi = vec3(0.3, 0.9, 1.0) * step(abs(q.y), 0.05) * (1.5 + 0.5 * sin(uTime * 6.0));
        } else {
            float sub = weaponSDF(q / 1.3, typ - 5.0).y;
            alb = sub < 0.5 ? vec3(0.12, 0.12, 0.13) : (sub < 1.5 ? vec3(0.35, 0.17, 0.07) : (sub < 2.5 ? vec3(0.05) : (sub < 3.5 ? vec3(0.6, 0.06, 0.04) : vec3(0.14, 0.16, 0.09))));
            if (sub > 1.5 && sub < 2.5) emi = vec3(0.3, 0.9, 1.0) * 2.5;
            spec = 0.9;
        }
        emi += alb * 0.25;
    }
    else { alb = vec3(0.6, 0.24, 0.24); spec = 0.5; }

    if (m > 9.5 && m < 19.5 && (m < 13.5 || m > 14.5)) emi += vec3(1.0, 0.1, 0.1) * uGirlHit * 0.8;
    float ao = full && uDebug != 3.0 ? calcAO(p, n) : 1.0;
    vec3 lc = themeLamp();
    vec3 col = alb * lc * 0.035 * ao + emi;

    // own cell lamp (with shadow) and all four neighbouring lamps that no wall separates from us
    vec2 cc = CELL * (floor(p.xz / CELL) + 0.5);
    for (int k = 0; k < 5; k++){
        vec2 c = cc;
        if (k > 0 && uDebug == 2.0) continue;
        if (k > 0){
            c += k == 1 ? vec2(CELL, 0.0) : (k == 2 ? vec2(-CELL, 0.0) : (k == 3 ? vec2(0.0, CELL) : vec2(0.0, -CELL)));
            if (wallDist(0.5 * (cc + c)) < 0.0) continue;
        }
        vec3 l = lampAt(c) - p;
        float d2 = dot(l, l), d = sqrt(d2);
        l /= d;
        float dif = max(dot(n, l), 0.0);
        if (dif <= 0.0) continue;
        float att = 4.2 * lampPower(c) / (1.0 + 0.3 * d2);
        float sh = (full && k == 0 && uDebug != 1.0) ? softShadow(p + n * 0.01, l, d - 0.3) : 1.0;
        float sp = pow(max(dot(n, normalize(l - rd)), 0.0), 40.0) * spec * 2.0;
        col += lc * att * sh * dif * (alb + sp);
    }
    // stone next to a glowing ring picks up its colour
    if (m > 1.5 && m < 2.5){
        vec2 lq = p.xz - CELL * floor(p.xz / CELL + 0.5);
        if (length(lq) < 1.0) col += alb * themeAccent() * 1.2 * exp(-abs(p.y - 2.3) * 4.0);
    }

    // drones, explosions, portal, cage and muzzle flash
    for (int i = 0; i < 8; i++){
        vec4 D = uD[i];
        if (D.w == 0.0) continue;
        vec3 l = D.xyz - p;
        float d2 = dot(l, l);
        l *= inversesqrt(d2);
        vec3 lcol; float inten;
        if (D.w > 0.5){ inten = D.w > 3.5 ? 3.0 : 0.5; lcol = D.w < 1.5 ? vec3(0.6, 0.25, 1.0) : (D.w < 2.5 ? vec3(1.0, 0.2, 0.1) : vec3(1.0, 0.55, 0.15)); }
        else { float big = uDX[i].z > 3.5 ? 4.0 : 1.0; inten = 14.0 * big * (1.0 + D.w) * (1.0 + D.w); lcol = vec3(1.0, 0.55, 0.2); }
        col += lcol * inten * max(dot(n, l), 0.0) * alb / (1.0 + d2 * 1.2);
    }
    for (int i = 0; i < 4; i++){
        vec4 B = uB[i];
        if (B.w <= 0.0) continue;
        vec3 l = B.xyz - p;
        float d2 = dot(l, l);
        l *= inversesqrt(d2);
        col += vec3(1.0, 0.55, 0.2) * 14.0 * (1.0 - B.w) * (1.0 - B.w) * max(dot(n, l), 0.0) * alb / (1.0 + d2 * 1.2);
    }
    if (uExit.w > 0.0){
        vec3 l = vec3(uExit.x, 1.5, uExit.z) - p;
        float d2 = dot(l, l);
        l *= inversesqrt(d2);
        vec3 ec = uTheme > 1.5 ? vec3(1.0, 0.2, 0.45) * uExit.w : vec3(0.3, 0.9, 1.0);
        col += ec * 3.0 * max(dot(n, l), 0.0) * alb / (1.0 + d2 * 0.4);
    }
    if (uGirl.w > -0.5){
        vec3 gp = uGirl.xyz + vec3(0.0, 3.0, 0.0) + normalize(vec3(uCamPos.x - uGirl.x, 0.0, uCamPos.z - uGirl.z) + 1e-4) * 1.6;
        vec3 l = gp - p;
        float d2 = dot(l, l);
        l *= inversesqrt(d2);
        col += vec3(1.0, 0.85, 0.7) * 5.0 * max(dot(n, l), 0.0) * alb / (1.0 + d2 * 0.6);
    }
    if (uFlash > 0.0){
        vec3 l = uCamPos + uCamF * 0.9 + uCamR * 0.25 - uCamU * 0.15 - p;
        float d2 = dot(l, l);
        l *= inversesqrt(d2);
        col += vec3(1.0, 0.8, 0.5) * uFlash * 6.0 * max(dot(n, l), 0.0) * alb / (1.0 + d2 * 0.5);
    }
    return col;
}

// ---------------------------------------------------------------- weapon in the player's hands (view space)
vec3 gunOff(){ return vec3(0.30 + uBob.x, -0.30 + uBob.y - uSwitch * 0.45, 0.72 - uRecoil * 0.08); }

vec2 gunMap(vec3 p){
    p -= gunOff();
    p.xz = rot(0.08) * p.xz;
    p.yz = rot(-uRecoil * 0.25) * p.yz;
    return weaponSDF(p, uWeapon);
}

vec3 gunNormal(vec3 p){
    const vec2 k = vec2(1.0, -1.0);
    const float h = 0.0005;
    return normalize(k.xyy * gunMap(p + k.xyy * h).x + k.yyx * gunMap(p + k.yyx * h).x +
                     k.yxy * gunMap(p + k.yxy * h).x + k.xxx * gunMap(p + k.xxx * h).x);
}

vec3 gunTipView(){
    vec3 l = uWeapon < 1.5 ? vec3(0.0, 0.01, 0.48) : (uWeapon < 2.5 ? vec3(0.0, 0.02, 0.52) : (uWeapon < 3.5 ? vec3(0.0, 0.0, 0.47) : vec3(0.0, 0.0, 0.52)));
    l.yz = rot(uRecoil * 0.25) * l.yz;
    l.xz = rot(-0.08) * l.xz;
    return l + gunOff();
}

// in-scattered light of the lamps along a ray. Each cell only sees its own lamp (walls block the rest),
// and the glow of one lamp, 1 / (1 + k d^2), integrates to an arctangent: no sampling noise at all
float lampScatter(vec3 ro, vec3 rd, float tmax){
    const float k = 3.0;
    vec2 p = ro.xz, d = rd.xz;
    vec2 cell = floor(p / CELL);
    vec2 st = vec2(d.x >= 0.0 ? 1.0 : -1.0, d.y >= 0.0 ? 1.0 : -1.0);
    vec2 ad = max(abs(d), vec2(1e-5));
    vec2 tNext = ((cell + max(st, 0.0)) * CELL - p) * st / ad;
    vec2 tDelta = CELL / ad;
    float t0 = 0.0, acc = 0.0;
    for (int i = 0; i < 12; i++){
        float t1 = min(min(tNext.x, tNext.y), tmax);
        vec2 c = (cell + 0.5) * CELL;
        vec3 L = lampAt(c) - ro;
        float s0 = dot(L, rd);
        float a = sqrt((1.0 + k * max(dot(L, L) - s0 * s0, 0.0)) / k);
        acc += lampPower(c) * (atan((t1 - s0) / a) - atan((t0 - s0) / a)) / (k * a) * exp(-max(s0, 0.0) * 0.04);
        if (t1 >= tmax) break;
        t0 = t1;
        if (tNext.x < tNext.y){ tNext.x += tDelta.x; cell.x += st.x; }
        else { tNext.y += tDelta.y; cell.y += st.y; }
    }
    return acc;
}

// distance from a map point to the route polyline, and how far along the route that closest point is
float routeDist(vec2 w, out float along){
    float best = 1e9, acc = 0.0;
    along = 0.0;
    vec2 a = uPath[0].xy;
    for (int k = 1; k < 128; k++){
        if (float(k) >= uPathN) break;
        vec4 v = uPath[k / 2];
        vec2 b = (k - (k / 2) * 2) == 0 ? v.xy : v.zw;
        vec2 ab = b - a;
        float ll = max(dot(ab, ab), 1e-4);
        float t = clamp(dot(w - a, ab) / ll, 0.0, 1.0);
        float d = length(w - a - ab * t);
        if (d < best){ best = d; along = acc + t * sqrt(ll); }
        acc += sqrt(ll);
        a = b;
    }
    return best;
}

// glow of a point seen along the ray, hidden behind the first hit
float pointGlow(vec3 ro, vec3 rd, float t, vec3 c, float core, float halo){
    vec3 oc = c - ro;
    float tc = clamp(dot(oc, rd), 0.0, t);
    float d = length(oc - rd * tc);
    return exp(-d * d * core) + halo / (d * d + halo * 4.0);
}

// ---------------------------------------------------------------- main
void main(){
    vec2 uv = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
    vec3 ro = uCamPos;
    vec3 rd = normalize(uCamF * 1.1 + uv.x * uCamR + uv.y * uCamU);
    vec3 fogc = themeFog();

    vec2 h = march(ro, rd);
    float t = h.x;
    vec3 col, n;
    float gloss;
    if (h.y > 0.5){
        col = shade(ro, rd, t, h.y, true, n, gloss);
        if (h.y < 1.5){
            vec3 p = ro + rd * t;
            vec3 rn = normalize(n + 0.012 * vec3(noise(p.xz * 2.0) - 0.5, 0.0, noise(p.zx * 2.0 + 3.0) - 0.5));
            vec3 r = reflect(rd, rn);
            vec2 hr = marchR(p + n * 0.02, r);
            vec3 rc = fogc;
            if (hr.y > 0.0){
                vec3 dn; float dg;
                rc = shade(p, r, hr.x, hr.y, false, dn, dg);
                rc = min(rc, vec3(1.5)) * exp(-hr.x * 0.05);
            }
            float fr = 0.04 + 0.96 * pow(1.0 - max(dot(n, -rd), 0.0), 5.0);
            float refl = uTheme > 1.5 ? 0.35 + 0.6 * fr : 0.08 + 0.5 * fr;
            col += rc * refl * gloss;
        }
    } else {
        col = fogc;
        t = 120.0;
    }
    col = mix(col, fogc, 1.0 - exp(-t * 0.03));

    // light scattering in the dusty air around the lamps: exact integral along the ray, cell by cell
    if (uDebug != 4.0) col += themeLamp() * lampScatter(ro, rd, min(t, 40.0)) * (uTheme > 0.5 && uTheme < 1.5 ? 0.03 : 0.045);

    // projectiles
    for (int i = 0; i < 24; i++){
        vec4 g = uG[i];
        if (g.w < 0.5) break;
        if (g.w < 1.5) col += vec3(1.0, 0.35, 0.12) * pointGlow(ro, rd, t, g.xyz, 700.0, 0.004) * 2.0;
        else if (g.w < 3.5) col += vec3(0.9, 0.25, 1.0) * pointGlow(ro, rd, t, g.xyz, 250.0, 0.008) * 2.0;
        else if (g.w < 4.5) col += vec3(1.0, 0.7, 0.35) * pointGlow(ro, rd, t, g.xyz, 400.0, 0.01) * 2.5;
        else if (g.w < 5.5) col += vec3(0.3, 0.85, 1.0) * pointGlow(ro, rd, t, g.xyz, 500.0, 0.006) * 2.5;
        else if (g.w < 6.5) col += vec3(1.0, 0.75, 0.35) * pointGlow(ro, rd, t, g.xyz, 6000.0, 0.0006) * 3.0;
        else { float a = 1.0 - (g.w - 7.0); col += vec3(1.0, 0.8, 0.5) * pointGlow(ro, rd, t + 0.3, g.xyz, 1500.0, 0.002) * 3.0 * a * a; }
    }
    // pickups glow a little so they can be spotted from afar
    for (int i = 0; i < 12; i++){
        vec4 P = uP[i];
        if (P.w < 0.5) break;
        vec3 hc = P.w > 11.5 ? vec3(0.3, 0.6, 1.0) : P.w > 10.5 ? vec3(1.0, 0.8, 0.3) * 3.0 : P.w > 9.5 ? vec3(0.4, 1.0, 0.3) : P.w < 2.5 ? vec3(1.0, 0.25, 0.2) : (P.w < 6.5 ? vec3(1.0, 0.75, 0.3) : vec3(1.0, 0.9, 0.5));
        col += hc * pointGlow(ro, rd, t, P.xyz, 5.0, 0.0) * 0.07;
    }
    // rocket blasts
    for (int i = 0; i < 4; i++){
        vec4 B = uB[i];
        if (B.w <= 0.0) continue;
        vec3 oc = B.xyz - ro;
        float d = length(oc - rd * clamp(dot(oc, rd), 0.0, t));
        float e = B.w, r = 0.6 + 3.2 * e;
        col += mix(vec3(1.0, 0.8, 0.45), vec3(1.0, 0.3, 0.05), e) * (1.0 - e) * (1.0 - e) * 2.2 * exp(-d * d / (r * r * 0.35));
    }
    // drone eyes glow and explosion fireballs
    for (int i = 0; i < 8; i++){
        vec4 D = uD[i];
        if (D.w == 0.0) continue;
        vec3 oc = D.xyz - ro;
        float tc = clamp(dot(oc, rd), 0.0, t);
        float d = length(oc - rd * tc);
        if (D.w > 0.5){
            vec3 hc = D.w < 1.5 ? mix(vec3(0.6, 0.3, 1.0), vec3(1.0, 0.15, 0.05) * (1.5 + sin(uTime * 30.0)), uDX[i].y) : (D.w < 2.5 ? vec3(1.0, 0.25, 0.15) : (D.w < 3.5 ? vec3(1.0, 0.6, 0.2) : vec3(1.0, 0.15, 0.3)));
            col += hc * (D.w > 3.5 ? 0.12 : 0.02) / (d * d + 0.02);
        } else {
            float big = uDX[i].z > 3.5 ? 3.0 : (uDX[i].z > 2.5 ? 1.5 : 1.0);
            float e = -D.w, r = (0.4 + 3.0 * e) * big;
            vec3 fc = mix(vec3(1.0, 0.85, 0.5), vec3(1.0, 0.25, 0.05), e);
            col += fc * (1.0 - e) * (1.0 - e) * 4.0 * exp(-d * d / (r * r * 0.4));
        }
    }
    // portal column / cage aura
    if (uExit.w > 0.0){
        vec2 o = ro.xz - uExit.xz;
        float tc = clamp(-dot(o, rd.xz) / max(dot(rd.xz, rd.xz), 1e-4), 0.0, t);
        vec3 q = ro + rd * tc;
        float d = length(q.xz - uExit.xz);
        float inY = smoothstep(0.0, 0.3, q.y) * smoothstep(CEIL, CEIL - 1.5, q.y);
        if (uTheme < 1.5){
            float sw = 0.75 + 0.25 * sin(q.y * 5.0 - uTime * 7.0 + atan(q.z - uExit.z, q.x - uExit.x) * 3.0);
            col += vec3(0.3, 0.85, 1.0) * exp(-d * d * 1.2) * 0.6 * sw * inY * uExit.w;
        } else {
            col += vec3(1.0, 0.2, 0.45) * exp(-(d - 1.3) * (d - 1.3) * 6.0) * 0.12 * inY * uExit.w;
        }
    }
    // tracer of the last shot
    if (uTr1.w > 0.0){
        vec3 ba = uTr1.xyz - uTr0.xyz, w = ro - uTr0.xyz;
        float d1 = dot(rd, ba), d2 = dot(ba, ba), d3 = dot(rd, w), d4 = dot(ba, w);
        float s = clamp((d4 - d1 * d3) / max(d2 - d1 * d1, 1e-5), 0.0, 1.0);
        float tt = clamp(-d3 + d1 * s, 0.0, t);
        float d = length(ro + rd * tt - (uTr0.xyz + ba * s));
        col += vec3(1.0, 0.8, 0.5) * uTr1.w * (exp(-d * d * 3000.0) * 1.5 + 0.0015 / (d * d + 0.002));
    }

    // weapon
    vec3 rv = normalize(vec3(uv, 1.1));
    vec3 gc = vec3(0.30, -0.30 - uSwitch * 0.45, 0.77);
    float gb = dot(gc, rv);
    if (uHud > 0.5 && gb * gb - dot(gc, gc) + 0.56 > 0.0){
        float gt = 0.15;
        bool hit = false;
        for (int i = 0; i < 72; i++){
            float d = gunMap(rv * gt).x;
            if (d < 0.0004){ hit = true; break; }
            gt += d;
            if (gt > 2.0) break;
        }
        if (hit){
            vec3 gp = rv * gt;
            vec3 gn = gunNormal(gp);
            float gm = gunMap(gp).y;
            vec3 base = vec3(0.07, 0.075, 0.08), ge = vec3(0.0);
            float sk = 0.7;
            if (gm > 0.5 && gm < 1.5){ base = vec3(0.3, 0.14, 0.06) * (0.7 + 0.6 * noise(vec2(gp.x * 30.0, gp.z * 5.0))); sk = 0.25; }
            else if (gm > 1.5 && gm < 2.5){ base = vec3(0.05); ge = vec3(0.3, 0.9, 1.0) * (2.0 + sin(uTime * 8.0)); }
            else if (gm > 2.5 && gm < 3.5) base = vec3(0.5, 0.05, 0.03);
            else if (gm > 3.5) base = vec3(0.1, 0.12, 0.07);
            vec3 c = base * themeLamp() * 0.08 + ge;
            vec3 lw = lampAt(CELL * (floor(uCamPos.xz / CELL) + 0.5)) - uCamPos;
            float att = 2.4 / (1.0 + 0.3 * dot(lw, lw));
            lw = normalize(lw);
            vec3 lv = vec3(dot(lw, uCamR), dot(lw, uCamU), dot(lw, uCamF));
            float dif = max(dot(gn, lv), 0.0);
            float sp = pow(max(dot(gn, normalize(lv - rv)), 0.0), 60.0);
            c += att * themeLamp() * (dif * base + sp * sk);
            vec3 lk = normalize(vec3(-0.5, 0.8, 0.3));
            c += themeAccent() * 0.4 * base * max(dot(gn, lk), 0.0);
            c += pow(1.0 - max(dot(gn, -rv), 0.0), 4.0) * themeLamp() * 0.12 * sk;
            vec3 fc = uWeapon > 3.5 ? vec3(0.3, 0.8, 1.0) : vec3(1.0, 0.7, 0.4);
            c += uFlash * fc * max(gn.z, 0.0) * 1.5;
            col = c;
        }
    }
    if (uFlash > 0.0 && uHud > 0.5){
        vec3 tip = gunTipView();
        vec3 pr = rv * dot(tip, rv) - tip;
        float d = length(pr);
        float star = exp(-abs(pr.x * pr.y) * 4000.0) * exp(-d * 18.0);
        vec3 fc = uWeapon > 3.5 ? vec3(0.4, 0.85, 1.0) : vec3(1.0, 0.75, 0.4);
        float big = uWeapon > 1.5 && uWeapon < 3.5 ? 1.8 : 1.0;
        col += fc * uFlash * (exp(-d * d * 900.0 / big) * 6.0 + star * 2.0 * big);
    }

    // tone mapping + grading
    col *= 1.1;
    col = (col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14);
    col = pow(clamp(col, 0.0, 1.0), vec3(0.4545));
    vec2 q = gl_FragCoord.xy / uRes;
    col *= 0.45 + 0.55 * pow(16.0 * q.x * q.y * (1.0 - q.x) * (1.0 - q.y), 0.2);
    col += (hash12(gl_FragCoord.xy + uTime) - 0.5) * 0.02;
    col = mix(col, vec3(0.6, 0.0, 0.0), clamp(uHurt, 0.0, 1.0) * 0.7 * smoothstep(0.2, 0.9, length(uv)));
    col = mix(col, vec3(dot(col, vec3(0.299, 0.587, 0.114))), clamp(uGray, 0.0, 1.0));

    if (uHud > 0.5){
        // crosshair and hit marker
        vec2 a = abs(uv);
        float cr = max(step(a.x, 0.0015) * step(0.007, a.y) * step(a.y, 0.022),
                       step(a.y, 0.0015) * step(0.007, a.x) * step(a.x, 0.022));
        col = mix(col, vec3(1.0, 0.92, 0.75), cr * 0.85);
        if (uHit > 0.0){
            vec2 b = abs(rot(0.785) * uv);
            float hm = max(step(b.x, 0.0018) * step(0.012, b.y) * step(b.y, 0.03),
                           step(b.y, 0.0018) * step(0.012, b.x) * step(b.x, 0.03));
            col = mix(col, vec3(1.0, 0.3, 0.2), hm * clamp(uHit, 0.0, 1.0));
        }
        // health bar
        vec2 sp = gl_FragCoord.xy / uRes.y;
        if (sp.x > 0.035 && sp.x < 0.345 && sp.y > 0.035 && sp.y < 0.065){
            col = mix(col, vec3(0.0), 0.6);
            if (sp.x > 0.04 && sp.x < 0.34 && sp.y > 0.04 && sp.y < 0.06){
                float f = (sp.x - 0.04) / 0.3;
                if (f < uHealth / 100.0) col = mix(vec3(0.9, 0.15, 0.05), vec3(1.0, 0.7, 0.25), f) * (0.8 + 0.4 * (sp.y - 0.04) / 0.02);
                if (sp.y > 0.051 && f < uArmor / 100.0) col = mix(vec3(0.2, 0.45, 1.0), vec3(0.65, 0.85, 1.0), (sp.y - 0.051) / 0.009);
            }
        }
        // boss health
        if (uBoss > 0.0 && abs(uv.x) < 0.31 && uv.y > 0.405 && uv.y < 0.43){
            col = mix(col, vec3(0.0), 0.65);
            if (abs(uv.x) < 0.3 && uv.y > 0.41 && uv.y < 0.425 && (uv.x + 0.3) / 0.6 < uBoss)
                col = mix(vec3(0.6, 0.02, 0.1), vec3(1.0, 0.25, 0.4), (uv.y - 0.41) / 0.015);
        }
        // minimap, rotated so that forward is up
        vec2 mc = vec2(0.5 * uRes.x / uRes.y - 0.17, 0.33);
        vec2 mq = (uv - mc) / 0.14;
        float ml = length(mq);
        if (ml < 1.06){
            if (ml < 1.0){
                vec2 f2 = normalize(uCamF.xz + vec2(1e-5, 0.0));
                vec2 r2 = vec2(f2.y, -f2.x);
                const float MR = 16.0;
                vec2 wp = uCamPos.xz + (r2 * mq.x + f2 * mq.y) * MR;
                float wd = wallDist(wp);
                vec3 mcol = wd > 0.0 ? vec3(0.09, 0.08, 0.075) : vec3(0.012);
                mcol = mix(mcol, themeAccent() * 0.8, smoothstep(0.45, 0.2, abs(wd)));
                if (uPathN > 1.5){
                    float along, rd = routeDist(wp, along);
                    float dash = step(0.45, fract(along * 0.3 - uTime * 1.5));
                    mcol = mix(mcol, vec3(1.0, 0.78, 0.25) * (0.55 + 0.45 * dash), smoothstep(0.75, 0.45, rd));
                }
                bool showExit = uTheme < 1.5 ? uExit.w > 0.5 : (uDoorLocked < 0.5 || texture2D(uWalls, (floor(uExit.xz / CELL) + 0.5) * uGrid.zw).b > 0.5);
                if (showExit){
                    vec2 ed = (uExit.xz - uCamPos.xz) / MR;
                    vec2 em = vec2(dot(ed, r2), dot(ed, f2));
                    float el = length(em);
                    if (el > 0.9) em *= 0.9 / el;
                    vec3 ec = uTheme > 1.5 ? vec3(1.0, 0.4, 0.7) : (uExit.w > 0.5 ? vec3(0.3, 1.0, 1.0) : vec3(1.0, 0.3, 0.2));
                    mcol = mix(mcol, ec, smoothstep(0.085, 0.055, length(mq - em)) * (0.6 + 0.4 * sin(uTime * 5.0)));
                }
                for (int i = 0; i < 8; i++){
                    if (uD[i].w < 0.5) continue;
                    vec2 dd = (uD[i].xz - uCamPos.xz) / MR;
                    float dl = length(mq - vec2(dot(dd, r2), dot(dd, f2)));
                    mcol = mix(mcol, vec3(1.0, 0.2, 0.15), smoothstep(uD[i].w > 3.5 ? 0.1 : 0.06, 0.035, dl));
                }
                float arrow = step(abs(mq.x) * 2.67 + mq.y, 0.1) * step(-0.06, mq.y);
                mcol = mix(mcol, vec3(1.0), arrow);
                col = mix(col, mcol, 0.85);
            }
            col = mix(col, themeAccent(), smoothstep(0.025, 0.0, abs(ml - 1.02)) * 0.9);
        }
    }

    // automap (Tab): the whole maze, but only what you have already seen
    if (uMap > 0.01){
        vec2 ext = (uGrid.xy - 1.0) * CELL;
        float sc = min(0.9 * uRes.x / uRes.y / ext.x, 0.82 / ext.y);
        vec2 wp = uv / sc + ext * 0.5;
        vec3 mc = vec3(0.0);
        bool inside = wp.x > -1.0 && wp.y > -1.0 && wp.x < ext.x + 1.0 && wp.y < ext.y + 1.0;
        if (inside){
            vec2 cell = clamp(floor(wp / CELL), vec2(0.0), uGrid.xy - 2.0);
            float seen = texture2D(uWalls, (cell + 0.5) * uGrid.zw).b;
            mc = seen > 0.5 ? vec3(0.13, 0.11, 0.09) : vec3(0.015);
            if (wallDist(wp) < 0.0) mc = themeAccent() * (seen > 0.5 ? 0.85 : 0.07);
            if (uPathN > 1.5){
                float along, rd = routeDist(wp, along);
                float dash = step(0.45, fract(along * 0.2 - uTime * 1.5));
                mc = mix(mc, vec3(1.0, 0.78, 0.25) * (0.55 + 0.45 * dash), smoothstep(0.9, 0.5, rd));
            }
            float eseen = texture2D(uWalls, (floor(uExit.xz / CELL) + 0.5) * uGrid.zw).b;
            if (eseen > 0.5 || uPathN > 1.5 || (uTheme > 1.5 && uDoorLocked < 0.5)){
                vec3 ecol = uTheme > 1.5 ? vec3(1.0, 0.4, 0.7) : (uExit.w > 0.5 ? vec3(0.3, 1.0, 1.0) : vec3(1.0, 0.3, 0.2));
                mc = mix(mc, ecol, smoothstep(1.5, 1.1, length(wp - uExit.xz)) * (0.6 + 0.4 * sin(uTime * 5.0)));
            }
            vec2 f2 = normalize(uCamF.xz + vec2(1e-5, 0.0));
            vec2 dq = (wp - uCamPos.xz) * sc;
            float fw = dot(dq, f2), sd = dot(dq, vec2(f2.y, -f2.x));
            mc = mix(mc, vec3(1.0), step(abs(sd) * 2.6 + fw, 0.02) * step(-0.014, fw));
        }
        col = mix(col, mc, uMap * (inside ? 0.93 : 0.6));
    }

    col *= 1.0 - 0.55 * uPause;
    col = mix(col, vec3(0.0), clamp(uFade, 0.0, 1.0));
    col = mix(col, vec3(1.0), clamp(uWhite, 0.0, 1.0));
    gl_FragColor = vec4(col, 1.0);
}
