#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Infinite low-house cityscape seen from a low-flying camera (~9 m above ground).
// Likhanov32D rasterises in 2D/orthographic only, so the whole perspective ground plane is raymarched
// here in the fragment shader — the 3D half of that name is this, not a geometry pipeline. Driven by a single `time` uniform (seconds); the camera
// flies forward forever over a domain-repeated grid of small houses.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // unused; scene is fully procedural
uniform vec4 colDiffuse;
uniform float time;

const vec2 res = vec2(384.0, 448.0);   // playfield render texture (portrait)

const float CAM_HEIGHT = 10.0;  // ~10 meters above the ground
const float SPEED      = 7.0;   // forward flight, meters/second
const vec2  CELL       = vec2(11.0, 11.0);   // house-plot spacing (house + yard + street)
const float MAX_DIST   = 320.0;

// sky / palette (warm dusk to sit behind the bullets)
const vec3 SKY_TOP   = vec3(0.24, 0.33, 0.52);
const vec3 SKY_HORIZ = vec3(0.86, 0.72, 0.54);
const vec3 SUN_DIR_C = vec3(-0.45, 0.72, -0.52);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// house SDF for one grid cell plus the ground plane. Returns distance; writes the
// hit material into `mat` (0 = ground, 1 = wall, 2 = roof) once close to a surface.
float map(vec3 p, out float mat) {
    float ground = p.y;

    vec2 id    = floor(p.xz / CELL);
    vec2 local = mod(p.xz, CELL) - CELL * 0.5;

    float r  = hash21(id);
    float h  = mix(2.2, 4.2, hash21(id + 3.1));                 // low houses (2-4 m tall)
    vec2  hw = vec2(mix(2.2, 3.4, hash21(id + 7.3)),
                    mix(2.2, 3.4, hash21(id + 1.7)));
    if (r < 0.22) h = 0.0;                                      // some empty lots / squares

    vec3 d3  = vec3(abs(local.x), abs(p.y - h * 0.5), abs(local.y)) - vec3(hw.x, h * 0.5, hw.y);
    float box = length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0);
    if (h <= 0.0) box = MAX_DIST;

    if (box < ground) {
        mat = (p.y > h - 0.35) ? 2.0 : 1.0;                    // roof vs wall
        return box;
    }
    mat = 0.0;
    return ground;
}

vec3 calcNormal(vec3 p) {
    float m;
    vec2 e = vec2(0.01, 0.0);
    return normalize(vec3(
        map(p + e.xyy, m) - map(p - e.xyy, m),
        map(p + e.yxy, m) - map(p - e.yxy, m),
        map(p + e.yyx, m) - map(p - e.yyx, m)));
}

vec3 skyColor(vec3 rd) {
    float t = clamp(rd.y * 1.6 + 0.15, 0.0, 1.0);
    return mix(SKY_HORIZ, SKY_TOP, t);
}

void main() {
    _fragColorOut = vec4(0.0);
    // Screen ray, portrait aspect. fragTexCoord runs 0 at the top of the picture to 1 at the bottom (the quad
    // comes from StageBackground.DrawProceduralQuad, the same on every backend), and the ray below flips the
    // sign of y so the top of the screen looks toward the horizon. This used to negate BOTH axes, a 180-degree
    // turn that compensated for the out-of-range coordinates the old render-target quad handed the shader on
    // one backend and put the sky at the bottom on the others.
    vec2 p = fragTexCoord - 0.5;
    p.x *= res.x / res.y;

    vec3 ro  = vec3(2.0, CAM_HEIGHT, time * SPEED);
    float pitch = 0.34;                                         // look down ~19 deg (horizon in upper third)
    vec3 fwd = normalize(vec3(0.0, -sin(pitch), cos(pitch)));
    vec3 rgt = normalize(cross(fwd, vec3(0.0, 1.0, 0.0)));
    vec3 upv = cross(rgt, fwd);
    vec3 rd  = normalize(p.x * rgt - p.y * upv + 1.0 * fwd);

    vec3 sun = normalize(SUN_DIR_C);

    float t   = 0.0;
    float mat = 0.0;
    bool  hit = false;
    for (int i = 0; i < 130; i++) {
        vec3 pos = ro + rd * t;
        float d  = map(pos, mat);
        if (d < 0.0015 * t + 0.002) { hit = true; break; }
        t += d;
        if (t > MAX_DIST) break;
    }

    vec3 col;
    if (hit) {
        vec3 pos = ro + rd * t;
        vec3 n   = calcNormal(pos);
        float diff = clamp(dot(n, sun), 0.0, 1.0);
        float amb  = 0.35 + 0.15 * n.y;

        vec3 base;
        if (mat < 0.5) {
            // ground: street grid over grass/asphalt
            vec2 g = mod(pos.xz, CELL);
            float road = smoothstep(0.0, 0.6, min(g.x, CELL.x - g.x)) *
                         smoothstep(0.0, 0.6, min(g.y, CELL.y - g.y));
            base = mix(vec3(0.30, 0.30, 0.33), vec3(0.34, 0.42, 0.28), road);
        } else {
            vec2 id  = floor(pos.xz / CELL);
            vec3 wall = mix(vec3(0.80, 0.68, 0.55), vec3(0.72, 0.55, 0.48), hash21(id + 9.9));
            vec3 roof = vec3(0.45, 0.28, 0.24);
            base = (mat > 1.5) ? roof : wall;
        }

        col = base * (amb + diff * 0.85);

        // distance fog fades the far houses into the horizon (hides the repeat + seam)
        float fog = 1.0 - exp(-t * 0.010);
        col = mix(col, SKY_HORIZ, fog);
    } else {
        col = skyColor(rd);
    }

    col = pow(clamp(col, 0.0, 1.0), vec3(0.9)); // mild lift

    // --- rain: cheap screen-space streaks falling down the screen, three layers for depth ---
    vec2 ruv = fragTexCoord;
    ruv.x *= res.x / res.y;
    float rain = 0.0;
    for (int k = 0; k < 3; k++) {
        float fk = float(k);
        float cols = 34.0 + fk * 26.0;                       // more, thinner columns further back
        float fall = 7.0 + fk * 5.0;                         // fall speed
        float ci   = floor(ruv.x * cols);
        float seed = hash21(vec2(ci, fk * 5.0 + 1.0));
        float y    = ruv.y * (3.5 + fk) + time * fall * (0.7 + seed * 0.6) + seed * 17.0;
        float cell = fract(y);
        float streak = smoothstep(0.0, 0.03, cell) * (1.0 - smoothstep(0.03, 0.45, cell));
        float on   = step(0.55, hash21(vec2(ci, floor(y))));  // sparse: only some cells carry a drop
        rain += streak * on * (0.6 - fk * 0.15);
    }
    col += vec3(0.62, 0.68, 0.80) * rain * 0.5;              // cool, semi-transparent streaks

    _fragColorOut = vec4(col, 1.0);
}
