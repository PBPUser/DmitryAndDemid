#version 330

// Cinematic approach to Drogichin (Belarus, Brest region). One continuous camera move:
//   1. fly low along a countryside road (camera near the ground, looking forward);
//   2. climb into a cloud layer that whites the screen out;
//   3. emerge high and pitch down, flying above the town grid (streets, red roofs, greens,
//      the central avenue running through — the aerial look of the reference photo).
// The whole scene is a single ground plane coloured procedurally, plus a Beer-Lambert cloud
// slab; the camera height/pitch are animated off `time`, so it is one shader, one pass.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // unused; scene is procedural
uniform vec4 colDiffuse;
uniform float time;

const vec2 res = vec2(384.0, 448.0);

// choreography, seconds
const float T_ROAD = 7.0;     // low along the road until here
const float T_RISE = 12.0;    // deep in the cloud whiteout around here
const float T_DOWN = 16.0;    // settled looking down over the town by here

const float SPEED   = 20.0;   // forward flight (m/s)
const float TOWN_Z  = 260.0;  // the town starts this far down the road
const float CELL    = 14.0;   // block spacing (house + yard + street)

const float CLOUD_LO = 42.0;  // cloud slab, metres
const float CLOUD_HI = 118.0;

// palette (warm summer day, to match the photo)
const vec3 SKY_TOP   = vec3(0.28, 0.44, 0.72);
const vec3 SKY_HORIZ = vec3(0.82, 0.87, 0.92);
const vec3 GRASS     = vec3(0.34, 0.46, 0.24);
const vec3 GRASS2    = vec3(0.27, 0.39, 0.20);
const vec3 ROAD_COL  = vec3(0.27, 0.27, 0.29);
const vec3 ROOF_RED  = vec3(0.62, 0.20, 0.17);
const vec3 ROOF_RED2 = vec3(0.72, 0.30, 0.21);
const vec3 WALL_COL  = vec3(0.83, 0.81, 0.75);
const vec3 TREE_COL  = vec3(0.18, 0.32, 0.16);
const vec3 CLOUD_COL = vec3(0.93, 0.95, 0.99);

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

vec3 skyColor(vec3 rd) {
    float t = clamp(rd.y * 1.4 + 0.2, 0.0, 1.0);
    return mix(SKY_HORIZ, SKY_TOP, t);
}

// Colour of the ground plane at world (x, z). P.x = across the road, P.y = down the road (z).
vec3 groundColor(vec2 P) {
    float ax = abs(P.x);
    float townMix = smoothstep(TOWN_Z - 40.0, TOWN_Z + 40.0, P.y);   // country -> town

    // dashed centre line of the main avenue (runs the whole length at x ~ 0)
    float dash = step(0.5, fract(P.y * 0.12)) * (1.0 - smoothstep(0.2, 0.4, ax));

    // --- open country: one road through fields, with tree clumps ---
    vec3 field = mix(GRASS, GRASS2, fbm(P * 0.06));
    float trees = smoothstep(0.60, 0.72, fbm(P * 0.13 + 3.0));
    field = mix(field, TREE_COL, trees * (1.0 - townMix) * 0.9);
    float mainRoad = 1.0 - smoothstep(3.5, 5.0, ax);
    vec3 country = mix(field, ROAD_COL, mainRoad);
    country = mix(country, vec3(0.82, 0.80, 0.42), dash * mainRoad);

    // --- town grid: streets + house plots (red roofs) + parks/yards ---
    vec2 cell = floor(P / CELL);
    vec2 loc  = mod(P, CELL);
    float street = clamp(
        (1.0 - smoothstep(2.0, 3.2, min(loc.x, CELL - loc.x))) +
        (1.0 - smoothstep(2.0, 3.2, min(loc.y, CELL - loc.y))), 0.0, 1.0);
    float r = hash21(cell);
    vec3 plot;
    if (r < 0.30) {
        plot = mix(GRASS, TREE_COL, smoothstep(0.4, 0.7, fbm(P * 0.2)));   // park / green yard
    } else {
        float roof = 1.0 - smoothstep(3.6, 4.3, max(abs(loc.x - CELL * 0.5), abs(loc.y - CELL * 0.5)));
        vec3 roofc = mix(ROOF_RED, ROOF_RED2, hash21(cell + 2.3));
        plot = mix(WALL_COL, roofc, roof);
    }
    float townMain = 1.0 - smoothstep(4.0, 5.5, ax);   // the avenue continues through town
    vec3 town = mix(plot, ROAD_COL, max(street, townMain));
    town = mix(town, vec3(0.82, 0.80, 0.42), dash * townMain);

    return mix(country, town, townMix);
}

void main() {
    float T = time;

    // Camera: rise from 3 m to 300 m and pitch from near-level to (almost) straight down.
    float rise  = smoothstep(T_ROAD - 1.0, T_DOWN, T);
    float camY  = mix(3.0, 300.0, rise);
    float pitch = mix(0.12, 1.55, smoothstep(T_RISE - 1.5, T_DOWN, T));   // fwd -> looking down
    vec3 ro = vec3(0.0, camY, T * SPEED);

    vec2 sp = fragTexCoord - 0.5;
    sp.x *= res.x / res.y;
    vec3 fwd = normalize(vec3(0.0, -sin(pitch), cos(pitch)));
    vec3 rgt = normalize(cross(fwd, vec3(0.0, 1.0, 0.0)));
    vec3 upv = cross(rgt, fwd);
    vec3 rd  = normalize(sp.x * rgt - sp.y * upv + 1.25 * fwd);

    // Ground plane y = 0.
    vec3 col;
    float tg = (rd.y < -0.0008) ? (-ro.y / rd.y) : 100000.0;
    if (tg < 90000.0) {
        vec3 hp = ro + rd * tg;
        col = groundColor(hp.xz);
        // fog by HORIZONTAL distance from the camera track, so a top-down view (small horizontal
        // distance) stays crisp while the road recedes into haze toward the horizon.
        float horiz = length(hp.xz - ro.xz);
        float fog = 1.0 - exp(-horiz * 0.006);
        col = mix(col, SKY_HORIZ, fog);
    } else {
        col = skyColor(rd);
    }

    // Cloud slab, Beer-Lambert along the segment of the ray inside CLOUD_LO..CLOUD_HI. When the
    // camera sits inside the slab (the climb) the path is huge -> whiteout; skimming through it from
    // above leaves only light wisps over the town.
    if (abs(rd.y) > 1e-4) {
        float ta = (CLOUD_LO - ro.y) / rd.y;
        float tb = (CLOUD_HI - ro.y) / rd.y;
        float tEnter = max(min(ta, tb), 0.0);
        float tExit  = min(max(ta, tb), tg);   // clouds behind the ground do not show
        if (tExit > tEnter) {
            float dens = 0.0;
            for (int i = 0; i < 6; i++) {
                float f = (float(i) + 0.5) / 6.0;
                vec3 spos = ro + rd * mix(tEnter, tExit, f);
                dens += fbm(spos.xz * 0.02 + vec2(0.0, T * 0.15));
            }
            // Ramp the cloud up into the whiteout, then ease it off once above the town.
            float gain = mix(0.35, 1.6, smoothstep(T_ROAD, T_RISE, T));
            gain *= 1.0 - 0.80 * smoothstep(T_DOWN, T_DOWN + 4.0, T);
            float cloud = 1.0 - exp(-gain * (dens / 6.0) * (tExit - tEnter) * 0.025);
            col = mix(col, CLOUD_COL, clamp(cloud, 0.0, 1.0));
        }
    }

    col = pow(clamp(col, 0.0, 1.0), vec3(0.92));
    gl_FragColor = vec4(col, 1.0);
}
