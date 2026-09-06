#version 330

// Staff roll: the steppe village of Parizh in Chelyabinsk oblast, and the 50-metre replica of the Eiffel
// Tower (really a cell mast) that stands at the edge of it. Low houses scattered over flat grassland, a dirt
// track running out to the tower, an enormous evening sky, and the KURY-GRIL board bolted across the tower
// low down.
//
// Likhanov32D rasterises in 2D only; the 3D here is raymarched in this fragment shader, the same way
// city_flyover.fs and houses.fs do theirs. Driven by `time` (seconds) and `resolution` (the target's pixels).
// texture0 is the sign: the lettering is baked to a texture on the C# side (ParisChelyabinskBackground) and
// sampled for the board's face, which keeps the game's own font and transliteration out of the shader.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // unused: everything here is procedural, the cast are billboards drawn over it
uniform vec4 colDiffuse;
uniform float time;
uniform vec2 resolution;

// The camera is flown on the C# side (ParisChelyabinskBackground): the roll is a scripted flight through the
// scene, and the same basis has to project the billboards it passes, so there is one source of truth for it
// and the shader is handed the result rather than working it out again.
uniform vec3 camPos;
uniform vec3 camRight;
uniform vec3 camUp;
uniform vec3 camFwd;

// Focal length of that camera. MUST match ParisChelyabinskBackground.Focal, or the billboards drift off the
// scene they are supposed to be standing in.
const float FOCAL = 1.35;

const float MAX_DIST = 900.0;

// The tower. The real one is about a fifth of the Paris original; these are its metres.
const float TOWER_H  = 50.0;
const float PLAT1    = 0.30;   // the platform heights, as a fraction of TOWER_H
const float PLAT2    = 0.58;
const float LEG_R    = 1.15;   // corner-leg half-thickness, metres

// palette - a late, low sun over the steppe
const vec3 SUN_DIR_C  = vec3(-0.42, 0.30, -0.86);
const vec3 SUN_COL    = vec3(1.00, 0.72, 0.42);
const vec3 SKY_TOP    = vec3(0.16, 0.30, 0.58);
const vec3 SKY_HORIZ  = vec3(0.94, 0.62, 0.36);
const vec3 GRASS_A    = vec3(0.30, 0.31, 0.17);
const vec3 GRASS_B    = vec3(0.44, 0.42, 0.22);
const vec3 DUST       = vec3(0.52, 0.44, 0.30);
const vec3 IRON       = vec3(0.30, 0.21, 0.15);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * vnoise(p); p *= 2.07; a *= 0.5; }
    return v;
}

float sdBox(vec3 p, vec3 b) {
    vec3 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
}

// Half-spacing of the tower corner legs at height y: the Eiffel flare, wide at the feet and easing into an
// almost parallel shaft above the second platform.
float towerHalf(float y) {
    float u = clamp(y / TOWER_H, 0.0, 1.2);
    return TOWER_H * (0.045 + 0.125 * exp(-u * 3.2));
}

// The tower: four flaring corner legs, the platforms they carry, the girder courses between them, the mast,
// and the board. The legs lean, so this is a bounded approximation rather than an exact distance - the march
// scales its steps down to suit (see MARCH_SAFE).
float sdTower(vec3 p, out float mat) {
    mat = 1.0;
    float y = p.y;
    // Cheap bound: the cylinder the whole tower stands in. This has to be a LOWER bound on the real distance
    // and never negative - an early-out that went negative above the tower was read as a hit and capped it
    // with a spiked mushroom.
    float bound = max(length(p.xz) - (towerHalf(0.0) + LEG_R + 1.0),
                      max(-1.0 - y, y - TOWER_H * 1.30));
    if (bound > 1.0) return bound;
    float w = towerHalf(y);

    // The four corner legs: the distance to the four corner LINES (+-w, +-w), which is what leaves the frame
    // open between them. Taking the distance to the square itself would give one solid tapering column.
    float d = length(abs(p.xz) - vec2(w)) - LEG_R;

    // platforms: a square deck a little wider than the legs that carry it, hollowed so it reads as a deck
    // with a rail rather than a solid lid
    for (int i = 0; i < 2; i++) {
        float py = TOWER_H * (i == 0 ? PLAT1 : PLAT2);
        float pw = towerHalf(py) + (i == 0 ? 2.6 : 1.7);
        float slab = sdBox(p - vec3(0.0, py, 0.0), vec3(pw, 0.42, pw));
        float inner = sdBox(p - vec3(0.0, py + 0.9, 0.0), vec3(pw - 1.5, 1.2, pw - 1.5));
        slab = max(slab, -inner);
        if (slab < d) { d = slab; mat = 2.0; }
    }

    // girder courses up the shaft, hollow so the sky shows through the frame
    for (int i = 0; i < 5; i++) {
        float f = float(i) / 4.0;
        float by = mix(2.5, TOWER_H * PLAT2 - 2.0, f);
        float bw = towerHalf(by);
        float bar = sdBox(p - vec3(0.0, by, 0.0), vec3(bw + 0.2, 0.34, bw + 0.2));
        float hole = sdBox(p - vec3(0.0, by, 0.0), vec3(bw - 0.9, 1.0, bw - 0.9));
        bar = max(bar, -hole);
        if (bar < d) { d = bar; mat = 1.0; }
    }

    // the mast on top
    float mast = max(length(p.xz) - 0.55, abs(y - TOWER_H * 1.12) - TOWER_H * 0.14);
    if (mast < d) { d = mast; mat = 3.0; }
    return d;
}

// A giant fork stuck in the steppe beside the village - the game's own emblem, at the scale of the tower it
// stands next to. Modelled rather than billboarded, so the flight goes round it and it catches the low sun on
// its own edges.
const vec3  FORK_AT   = vec3(-78.0, 0.0, -104.0);
const float FORK_LEAN = 0.20;   // radians it leans back, so it reads as driven in rather than screwed down

float sdFork(vec3 p) {
    p -= FORK_AT;
    float c = cos(FORK_LEAN), sn = sin(FORK_LEAN);
    p.xy = mat2(c, -sn, sn, c) * p.xy;
    float d = sdBox(p - vec3(0.0, 9.5, 0.0), vec3(1.6, 9.8, 0.75));    // handle, buried at the bottom
    d = min(d, sdBox(p - vec3(0.0, 20.6, 0.0), vec3(1.05, 2.0, 0.58)));  // neck
    d = min(d, sdBox(p - vec3(0.0, 23.6, 0.0), vec3(3.6, 1.5, 0.62)));   // the head the tines rise from
    for (int i = 0; i < 4; i++)
        d = min(d, sdBox(p - vec3((float(i) - 1.5) * 2.25, 28.4, 0.0), vec3(0.62, 4.6, 0.52)));
    return d;
}

// One village house per grid cell, well away from the tower: a gabled box, its roof a wedge.
float sdVillage(vec3 p, out float mat) {
    const vec2 CELL = vec2(26.0, 26.0);
    mat = 0.0;
    vec2 id = floor(p.xz / CELL);
    vec2 c  = (id + 0.5) * CELL;
    // leave the ground around the tower, and the track running out to it, clear
    if (length(c) < 46.0 || abs(c.x) < 7.0 || hash21(id * 1.7) < 0.42) return MAX_DIST;
    vec2 jit = vec2(hash21(id + 3.1), hash21(id + 8.4)) * 8.0 - 4.0;
    vec3 o = p - vec3(c.x + jit.x, 0.0, c.y + jit.y);
    float h = mix(3.0, 4.6, hash21(id + 5.5));
    float body = sdBox(o - vec3(0.0, h * 0.5, 0.0), vec3(4.6, h * 0.5, 3.4));
    vec3 r = o - vec3(0.0, h, 0.0);
    float roof = max(max(abs(r.z) * 0.86 + r.y * 0.5 - 2.2, r.y - 1.9), abs(r.x) - 5.0);
    mat = (roof < body) ? 6.0 : 5.0;
    return min(body, roof);
}

float map(vec3 p, out float mat) {
    float m2;
    float mt;
    float t = sdTower(p, mt);
    float v = sdVillage(p, m2);
    float ground = p.y - (fbm(p.xz * 0.012) - 0.5) * 2.2;   // the steppe, gently rolling
    float f = sdFork(p);
    float d = ground; mat = 0.0;
    if (v < d) { d = v; mat = m2; }
    if (t < d) { d = t; mat = mt; }
    if (f < d) { d = f; mat = 7.0; }
    return d;
}

vec3 calcNormal(vec3 p) {
    float m;
    vec2 e = vec2(0.03, 0.0);
    return normalize(vec3(
        map(p + e.xyy, m) - map(p - e.xyy, m),
        map(p + e.yxy, m) - map(p - e.yxy, m),
        map(p + e.yyx, m) - map(p - e.yyx, m)));
}

// How far out of the atmosphere the flight has climbed: 0 down on the steppe, 1 in space at the end of it.
float spaceness() {
    return smoothstep(55.0, 360.0, camPos.y);
}

// Space: black, with a scatter of stars fixed to the ray direction.
vec3 spaceColor(vec3 rd) {
    vec2 sp2 = rd.xz / max(abs(rd.y), 0.08);
    float s = hash21(floor(sp2 * 420.0 + 40.0));
    float star = step(0.986, s) * (0.4 + 0.6 * hash21(floor(sp2 * 420.0)));
    vec3 col = vec3(0.015, 0.012, 0.03) + vec3(0.9, 0.92, 1.0) * star;
    // the band of the galaxy, so it is not an empty black field
    float band = exp(-pow((rd.y - 0.12) * 4.5, 2.0));
    col += vec3(0.10, 0.09, 0.16) * band * (0.4 + 0.6 * fbm(sp2 * 1.6));
    return col;
}

vec3 skyColor(vec3 rd, vec3 sun) {
    float t = clamp(rd.y * 1.6 + 0.06, 0.0, 1.0);
    vec3 col = mix(SKY_HORIZ, SKY_TOP, pow(t, 0.7));
    float s = clamp(dot(rd, sun), 0.0, 1.0);
    col += SUN_COL * pow(s, 220.0) * 2.2;                 // the disc
    col += SUN_COL * pow(s, 6.0) * 0.30;                  // and the glow around it
    if (rd.y > 0.001) {                                   // long evening cloud banks, drifting
        vec2 cp = rd.xz / rd.y;
        float n = fbm(cp * 0.55 + vec2(time * 0.012, time * 0.004));
        float bands = smoothstep(0.48, 0.82, n) * smoothstep(0.0, 0.25, rd.y);
        vec3 lit = mix(vec3(0.62, 0.45, 0.44), vec3(1.0, 0.86, 0.72), pow(s, 3.0));
        col = mix(col, lit, bands * 0.75);
    }
    // Up there the air is gone: the sky darkens to space and the sun keeps only its disc.
    float sky = spaceness();
    if (sky > 0.0) {
        vec3 out_ = spaceColor(rd) + SUN_COL * pow(s, 400.0) * 3.0;
        col = mix(col, out_, sky);
    }
    return col;
}

vec3 groundColor(vec2 xz) {
    vec3 col = mix(GRASS_A, GRASS_B, fbm(xz * 0.09));
    col = mix(col, DUST, smoothstep(0.55, 0.85, fbm(xz * 0.4 + 11.0)) * 0.35);
    float road = 1.0 - smoothstep(2.6, 4.4, abs(xz.x));   // the track out to the tower
    col = mix(col, DUST * 1.15, road * 0.85);
    return col;
}

void main() {
    vec2 res = resolution.x > 1.0 ? resolution : vec2(640.0, 480.0);
    vec2 sp = (fragTexCoord * res - 0.5 * res) / res.y;

    vec3 ro = camPos;
    vec3 rd = normalize(sp.x * camRight - sp.y * camUp + FOCAL * camFwd);

    vec3 sun = normalize(SUN_DIR_C);

    // The legs lean, so the tower field understates the distance; step conservatively through it.
    const float MARCH_SAFE = 0.55;
    float t = 0.6, mat = 0.0;
    bool hit = false;
    vec3 pos = ro;
    for (int i = 0; i < 150; i++) {
        pos = ro + rd * t;
        float d = map(pos, mat);
        if (d < 0.004 * t + 0.01) { hit = true; break; }
        t += max(d * MARCH_SAFE, 0.02);
        if (t > MAX_DIST) break;
    }

    vec3 col;
    if (hit) {
        vec3 n = calcNormal(pos);
        float diff = clamp(dot(n, sun), 0.0, 1.0);
        float amb  = 0.30 + 0.22 * clamp(n.y, 0.0, 1.0);
        vec3 lit = SUN_COL * diff * 1.15 + mix(SKY_HORIZ, SKY_TOP, 0.5) * amb;

        if (mat < 0.5) {
            col = groundColor(pos.xz) * lit;
        } else if (mat < 1.5) {
            col = IRON * lit * 1.1;                                  // the ironwork
        } else if (mat < 2.5) {
            col = IRON * 1.25 * lit;                                 // platform decks
        } else if (mat < 3.5) {
            col = vec3(0.42, 0.34, 0.28) * lit;                      // the mast
        } else if (mat < 5.5) {
            col = mix(vec3(0.55, 0.50, 0.42), vec3(0.68, 0.62, 0.50),
                      hash21(floor(pos.xz * 0.1))) * lit;            // house walls
        } else if (mat < 6.5) {
            col = vec3(0.34, 0.19, 0.16) * lit;                      // house roofs
        } else {
            // The fork: bare steel, so it takes the sky along its edges as well as the sun on its faces.
            float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
            col = vec3(0.66, 0.68, 0.72) * lit * 1.15
                  + skyColor(reflect(rd, n), sun) * fres * 0.45
                  + SUN_COL * pow(clamp(dot(reflect(rd, n), sun), 0.0, 1.0), 40.0) * 0.9;
        }

        float fog = 1.0 - exp(-t * 0.0032);
        col = mix(col, mix(SKY_HORIZ, SKY_TOP, 0.15), fog);
    } else {
        col = skyColor(rd, sun);
    }

    col = pow(clamp(col, 0.0, 1.0), vec3(0.92));
    float vig = 1.0 - 0.30 * dot(sp, sp);
    gl_FragColor = vec4(clamp(col * vig, 0.0, 1.0), 1.0);
}
