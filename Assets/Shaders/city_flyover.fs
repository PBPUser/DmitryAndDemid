#version 330

// Extra stage: a night flight down the avenue of a modern city, between its towers. The camera flies forward
// forever, high up and climbing, drifting from side to side and banking into each drift, with glass towers on
// both sides of the avenue lit by grids of windows, red beacons on the roofs and sodium lamps along the kerbs,
// and a cloud deck between the tower tops that it starts under, rises through and ends up above.
// Likhanov32D rasterises in 2D only — the 3D here is raymarched in this fragment shader, the way houses.fs
// does its low-house field; the towers are a domain-repeated grid of boxes with the avenue's lane left empty.
// Driven by a single `time` uniform (seconds).

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // unused; scene is fully procedural
uniform vec4 colDiffuse;
uniform float time;

const vec2 res = vec2(384.0, 448.0);   // playfield render texture (portrait)

const float SPEED      = 28.0;   // forward flight, metres/second
// The flight starts high over the avenue and keeps climbing: CAM_START metres up at the first tick, easing
// up by CAM_CLIMB more with a time constant of CLIMB_TIME seconds — still rising minutes in, never quite done.
const float CAM_START  = 78.0;
const float CAM_CLIMB  = 115.0;
const float CLIMB_TIME = 75.0;
// A cloud deck between the tower tops: the camera starts under it, climbs through it and ends up above,
// looking down on the towers that poke through. Lit from below by the city, so it is not black at night.
const float CLOUD_LO   = 100.0;
const float CLOUD_HI   = 150.0;
const vec3  CLOUD_COL  = vec3(0.34, 0.30, 0.42);
const vec3  CLOUD_GLOW = vec3(0.52, 0.38, 0.34);   // the deck's underside, warmed by the lamps below
const vec2  CELL       = vec2(36.0, 36.0);   // tower plot spacing (tower + street)
const float AVENUE     = 30.0;   // half-width of the avenue the camera flies down: no towers inside it
const float MAX_DIST   = 620.0;
const float LAMP_STEP  = 24.0;   // street lamps every this many metres along both kerbs

// palette (night, with the city's own glow on the horizon)
const vec3 SKY_TOP   = vec3(0.015, 0.025, 0.07);
const vec3 SKY_HORIZ = vec3(0.17, 0.10, 0.22);
const vec3 FOG_COL   = vec3(0.10, 0.08, 0.16);
const vec3 MOON_DIR_C = vec3(-0.35, 0.80, 0.30);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) {
        v += a * vnoise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return v;
}

// The tower standing on grid cell `id`: its height (0 = none) and half-footprint. The cells the avenue runs
// through are empty, a few others hold a low pavilion instead of a tower.
float towerHeight(vec2 id, out vec2 hw) {
    float cx = (id.x + 0.5) * CELL.x;
    float r  = hash21(id);
    hw = vec2(mix(8.0, 13.0, hash21(id + 7.3)), mix(8.0, 13.0, hash21(id + 1.7)));
    float h = mix(38.0, 150.0, pow(hash21(id + 3.1), 1.6));
    if (abs(cx) < AVENUE) h = 0.0;
    else if (r < 0.14) h = 7.0;
    return h;
}

// Distance from `p` to the tower on cell `id` (MAX_DIST when the cell is empty); `top` gets its height.
float boxAt(vec3 p, vec2 id, out float top) {
    vec2 hw;
    float h = towerHeight(id, hw);
    top = h;
    if (h <= 0.0) return MAX_DIST;
    vec2 c = (id + 0.5) * CELL;
    vec3 d3 = vec3(abs(p.x - c.x), abs(p.y - h * 0.5), abs(p.z - c.y)) - vec3(hw.x, h * 0.5, hw.y);
    return length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0);
}

// Scene SDF: the ground plane and the towers. A tower never crosses its own cell, but the nearest one to a
// point can stand in a neighbouring cell, so the 2x2 of cells around the point is tested — cheap, and it
// keeps the march from overshooting a tall facade at a cell boundary. `mat`: 0 ground, 1 wall, 2 roof.
float map(vec3 p, out float mat, out vec2 hitId, out float hitH) {
    vec2 id    = floor(p.xz / CELL);
    vec2 local = p.xz - (id + 0.5) * CELL;
    vec2 nb    = vec2(local.x < 0.0 ? -1.0 : 1.0, local.y < 0.0 ? -1.0 : 1.0);

    float best = MAX_DIST, bestH = 0.0, top, d;
    vec2 bestId = id;
    d = boxAt(p, id, top);
    if (d < best) { best = d; bestId = id; bestH = top; }
    d = boxAt(p, id + vec2(nb.x, 0.0), top);
    if (d < best) { best = d; bestId = id + vec2(nb.x, 0.0); bestH = top; }
    d = boxAt(p, id + vec2(0.0, nb.y), top);
    if (d < best) { best = d; bestId = id + vec2(0.0, nb.y); bestH = top; }
    d = boxAt(p, id + nb, top);
    if (d < best) { best = d; bestId = id + nb; bestH = top; }

    float ground = p.y;
    if (best < ground) {
        mat = (p.y > bestH - 0.7) ? 2.0 : 1.0;
        hitId = bestId;
        hitH = bestH;
        return best;
    }
    mat = 0.0;
    hitId = id;
    hitH = 0.0;
    return ground;
}

vec3 calcNormal(vec3 p) {
    float m, h;
    vec2 id;
    vec2 e = vec2(0.02, 0.0);
    return normalize(vec3(
        map(p + e.xyy, m, id, h) - map(p - e.xyy, m, id, h),
        map(p + e.yxy, m, id, h) - map(p - e.yxy, m, id, h),
        map(p + e.yyx, m, id, h) - map(p - e.yyx, m, id, h)));
}

vec3 skyColor(vec3 rd) {
    float t = clamp(rd.y * 2.2 + 0.1, 0.0, 1.0);
    vec3 col = mix(SKY_HORIZ, SKY_TOP, t);
    // a scatter of stars, only where the sky is dark enough to show them
    vec2 sp = rd.xz / max(rd.y, 0.05);
    float s = hash21(floor(sp * 60.0));
    col += vec3(0.8, 0.85, 1.0) * step(0.994, s) * t * 0.8;
    // the moon: a soft disc up and to the left
    float m = dot(rd, normalize(MOON_DIR_C));
    col += vec3(0.85, 0.85, 0.75) * smoothstep(0.9975, 0.9990, m) * 0.9;
    col += vec3(0.30, 0.30, 0.40) * smoothstep(0.985, 0.999, m) * 0.25;
    return col;
}

// The avenue: asphalt with lane markings, kerbs, cross streets between the blocks and pools of lamp light.
vec3 groundColor(vec2 xz) {
    vec3 asphalt = vec3(0.045, 0.05, 0.065);
    float ax = abs(xz.x);
    vec3 col = asphalt;
    float walk = smoothstep(AVENUE - 5.0, AVENUE - 4.5, ax) * (1.0 - smoothstep(AVENUE - 0.5, AVENUE, ax));
    col = mix(col, vec3(0.12, 0.12, 0.14), walk);
    float dash   = step(0.5, fract(xz.y / 8.0));
    float centre = (1.0 - smoothstep(0.15, 0.45, ax)) * dash;
    float edge   = 1.0 - smoothstep(0.15, 0.45, abs(ax - (AVENUE - 6.5)));
    col += vec3(0.5, 0.45, 0.25) * (centre + edge * 0.6) * 0.45;
    vec2 g = mod(xz, CELL);
    float crossing = 1.0 - smoothstep(2.0, 3.0, min(g.y, CELL.y - g.y));
    col = mix(col, asphalt * 1.4, crossing * step(AVENUE, ax));
    float lz = mod(xz.y, LAMP_STEP) - LAMP_STEP * 0.5;
    float lx = ax - (AVENUE - 3.0);
    float pool = exp(-(lz * lz + lx * lx) / 45.0);
    col += vec3(1.0, 0.72, 0.38) * pool * 0.75;
    return col;
}

// A tower's glass facade: dark glass with a grid of windows, a random share of them lit in warm or cool
// light and a few switching on and off over time.
vec3 facadeLight(vec3 pos, vec3 n, vec2 id) {
    float side = abs(n.x) > 0.5 ? 0.0 : 51.0;
    float u = abs(n.x) > 0.5 ? pos.z : pos.x;
    vec2 w  = vec2(u / 2.6, pos.y / 3.4);
    vec2 wi = floor(w);
    vec2 wf = fract(w);
    float pane = step(0.18, wf.x) * step(wf.x, 0.88) * step(0.22, wf.y) * step(wf.y, 0.80);
    float seed  = hash21(wi + id * 13.7 + side);
    float blink = hash21(wi + floor(time * 0.15 + seed * 9.0) + id);
    float lit   = step(0.58, seed) * step(0.12, blink);
    vec3 warm = vec3(1.0, 0.80, 0.52);
    vec3 cool = vec3(0.62, 0.84, 1.0);
    vec3 wc = mix(warm, cool, step(0.5, hash21(wi * 3.1 + id + side)));
    float bright = mix(0.35, 1.0, hash21(wi + 0.77 + id));
    return wc * lit * pane * bright;
}

void main() {
    vec2 sp = fragTexCoord - 0.5;
    sp.x *= res.x / res.y;

    // Camera: forward at a steady speed, drifting side to side across the avenue and banking into the drift,
    // with a slow bob in height and a slight downward pitch so the street shows under the towers.
    float T = time;
    float drift = sin(T * 0.31) * 7.0 + sin(T * 0.77) * 2.0;
    float driftRate = cos(T * 0.31) * 0.31 * 7.0 + cos(T * 0.77) * 0.77 * 2.0;
    // Altitude: the climb, plus a slow bob. The pitch steepens with it so the avenue stays in the frame as
    // the camera rises over the towers instead of drifting up to an empty horizon.
    float climb = 1.0 - exp(-T / CLIMB_TIME);
    float camY  = CAM_START + CAM_CLIMB * climb + sin(T * 0.5) * 3.0;
    vec3 ro = vec3(drift, camY, T * SPEED);
    float yaw   = driftRate / SPEED;
    float pitch = 0.22 + 0.30 * climb;
    float roll  = -driftRate * 0.05;
    vec3 fwd = normalize(vec3(sin(yaw), -sin(pitch), cos(yaw) * cos(pitch)));
    vec3 rgt = normalize(cross(fwd, vec3(0.0, 1.0, 0.0)));
    vec3 upv = cross(rgt, fwd);
    vec3 rr  = rgt * cos(roll) + upv * sin(roll);
    vec3 uu  = -rgt * sin(roll) + upv * cos(roll);
    vec3 rd  = normalize(sp.x * rr - sp.y * uu + 1.1 * fwd);

    vec3 moon = normalize(MOON_DIR_C);

    float t = 0.0, mat = 0.0, hitH = 0.0;
    vec2 hitId = vec2(0.0);
    bool hit = false;
    for (int i = 0; i < 120; i++) {
        vec3 pos = ro + rd * t;
        float d = map(pos, mat, hitId, hitH);
        if (d < 0.0015 * t + 0.003) { hit = true; break; }
        t += d;
        if (t > MAX_DIST) break;
    }

    vec3 col;
    if (hit) {
        vec3 pos = ro + rd * t;
        vec3 n   = calcNormal(pos);
        float diff = clamp(dot(n, moon), 0.0, 1.0);
        float amb  = 0.22 + 0.10 * n.y;
        float sky  = clamp(n.y * 0.5 + 0.5, 0.0, 1.0);

        if (mat < 0.5) {
            col = groundColor(pos.xz) * (amb + diff * 0.45 + 0.45);
        } else if (mat < 1.5) {
            vec3 glass = mix(vec3(0.05, 0.07, 0.11), vec3(0.09, 0.08, 0.12), hash21(hitId + 9.9));
            float fres = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
            col = glass * (amb + diff * 0.6) + skyColor(reflect(rd, n)) * fres * 0.35;
            col += facadeLight(pos, n, hitId);
            // the kerb lamps throw their light up the first floors
            col += vec3(1.0, 0.72, 0.38) * exp(-pos.y * 0.09) * 0.10;
        } else {
            col = vec3(0.07, 0.075, 0.09) * (amb + diff * 0.5 + sky * 0.2);
            vec2 c  = (hitId + 0.5) * CELL;
            float bd = length(pos.xz - c);
            float blink = smoothstep(0.2, 0.9, sin(time * 2.5 + hash21(hitId) * 6.2832));
            col += vec3(1.0, 0.12, 0.10) * exp(-bd * bd / 3.0) * blink;
        }

        // Distance haze, thickened and thinned by drifting noise so it hangs in patches rather than a flat veil.
        float haze = 0.55 + 0.9 * fbm(pos.xz * 0.012 + vec2(T * 0.05, -T * 0.11));
        float fog = 1.0 - exp(-t * 0.0042 * haze);
        col = mix(col, FOG_COL, fog);
    } else {
        col = skyColor(rd);
    }

    // The cloud deck: Beer-Lambert along the ray's run through the CLOUD_LO..CLOUD_HI slab, sampled on
    // drifting fbm so it is banks and holes, not a sheet. Under it the deck glows with the city; above it, the
    // towers stand out of it. Whatever the ray hit behind the slab is what it fades toward the deck colour.
    float tHit = hit ? t : MAX_DIST;
    if (abs(rd.y) > 1e-4) {
        float ta = (CLOUD_LO - ro.y) / rd.y;
        float tb = (CLOUD_HI - ro.y) / rd.y;
        float tEnter = max(min(ta, tb), 0.0);
        float tExit  = min(max(ta, tb), tHit);
        if (tExit > tEnter) {
            float dens = 0.0;
            for (int i = 0; i < 6; i++) {
                float f = (float(i) + 0.5) / 6.0;
                vec3 spos = ro + rd * mix(tEnter, tExit, f);
                float n = fbm(spos.xz * 0.016 + vec2(T * 0.06, T * 0.03));
                dens += smoothstep(0.35, 0.75, n);
            }
            float cloud = 1.0 - exp(-(dens / 6.0) * (tExit - tEnter) * 0.06);
            // Looking up from under the deck shows its lit underside; looking down from above, its dim top.
            float under = clamp((CLOUD_LO - ro.y) / 40.0 + 0.5, 0.0, 1.0);
            vec3 deck = mix(CLOUD_COL, CLOUD_GLOW, under);
            col = mix(col, deck, clamp(cloud, 0.0, 1.0));
        }
    }

    // Wisps right at the camera while it is inside the deck: a screen-space veil that thickens with the
    // local density, so passing through the layer reads as flying through it and not as a colour change.
    float inside = smoothstep(CLOUD_LO - 8.0, CLOUD_LO + 10.0, ro.y) * (1.0 - smoothstep(CLOUD_HI - 10.0, CLOUD_HI + 8.0, ro.y));
    if (inside > 0.0) {
        float veil = fbm(sp * 3.0 + vec2(T * 0.4, T * 0.9)) * inside;
        col = mix(col, CLOUD_COL, clamp(veil * 0.8, 0.0, 0.85));
    }

    // a light vignette so the bullets in the middle stay the brightest thing on screen
    float vig = 1.0 - 0.35 * dot(sp, sp);
    col *= vig;

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
