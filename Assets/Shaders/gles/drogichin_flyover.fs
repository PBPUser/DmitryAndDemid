#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Stage 1: Drogichin, opening on the square in front of the district executive committee — the view in the
// photo on the town's Wikipedia page: the paved square, the Lenin statue on the left, the white committee
// building with its red roof, columns and flag, cypresses along its front, and the main street running off to
// the right under an overcast sky. The camera walks the square, lifts off over the street, climbs through the
// cloud deck and cruises out along the main street looking down on the real town.
//
// The ground is the town's OpenStreetMap extract, rasterised into texture0 (Assets/Textures/drogichin_osm.png,
// built by a script from the OSM data; 2400 m across, origin on the square, x east, z north, top row north):
//   R = road class (250 main street, 210 tertiary, 170 residential, 120 the paved square, 110 service,
//       70 footpath), G = building storeys x 40, B = 255 water / 150 green / 60 residential yard.
// Buildings are extruded from the map by a heightfield march; the set pieces on the square (statue, columns,
// flag, cypresses, trees) are hand-placed SDFs marched with it. fragTexCoord is 0 at the top of the picture.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // the town map, see above
uniform vec4 colDiffuse;
uniform float time;

const vec2 res = vec2(384.0, 448.0);
const float EXTENT = 2400.0;
const float MAP_PX = 1024.0;

// choreography, seconds
const float T_WALK  = 8.0;    // on the square at eye level
const float T_LIFT  = 15.0;   // lifted off over the street
const float T_CLOUD = 22.0;   // up through the deck, looking down
const float CRUISE  = 9.0;    // m/s over the town after that

const float CLOUD_LO = 90.0;
const float CLOUD_HI = 170.0;
const float STOREY = 3.1;
const float ROOF = 1.6;
const float HMAX = 6.0 * STOREY + ROOF;   // the tallest block the map encodes (G = 240)

// palette: the photo's overcast day
const vec3 SKY_TOP   = vec3(0.60, 0.65, 0.72);
const vec3 SKY_HORIZ = vec3(0.86, 0.88, 0.90);
const vec3 CLOUD_COL = vec3(0.90, 0.91, 0.93);
const vec3 GRASS     = vec3(0.34, 0.46, 0.24);
const vec3 GRASS2    = vec3(0.27, 0.39, 0.20);
const vec3 YARD      = vec3(0.40, 0.44, 0.28);
const vec3 ASPHALT   = vec3(0.27, 0.27, 0.29);
const vec3 WATER     = vec3(0.30, 0.42, 0.55);
const vec3 PAVE_GREY = vec3(0.47, 0.45, 0.44);
const vec3 PAVE_RED  = vec3(0.58, 0.32, 0.26);
const vec3 WALL      = vec3(0.84, 0.82, 0.76);
const vec3 WALL_TH   = vec3(0.96, 0.95, 0.92);
const vec3 BAND_TH   = vec3(0.93, 0.85, 0.55);
const vec3 ROOF_RED  = vec3(0.62, 0.20, 0.17);
const vec3 ROOF_RED2 = vec3(0.70, 0.29, 0.21);
const vec3 TREE_COL  = vec3(0.18, 0.32, 0.16);
const vec3 BRONZE    = vec3(0.16, 0.14, 0.12);
// A low sun through the overcast, from the north-west — ahead and to the right of the camera on the square —
// so the building, the statue and the cypresses throw their shadows across the paving toward the viewer.
const vec3 SUN_DIR_C = vec3(-0.55, 0.26, 0.72);   // ~16 degrees up: long shadows over the paving

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

// ---- the map -------------------------------------------------------------------------------------------
// World metres -> the texel under them, snapped to texel centres so the filter never blends two storeys.
// Outside the extent returns a = -1: open country.
vec4 mapAt(vec2 xz) {
    vec2 m = vec2(xz.x / EXTENT + 0.5, 0.5 - xz.y / EXTENT);
    if (m.x < 0.0 || m.x > 1.0 || m.y < 0.0 || m.y > 1.0) return vec4(0.0, 0.0, 0.0, -1.0);
    m = (floor(m * MAP_PX) + 0.5) / MAP_PX;
    return texture(texture0, m);
}

float storeysAt(vec2 xz) {
    return floor(mapAt(xz).g * 255.0 / 40.0 + 0.5);
}

float heightAt(vec2 xz) {
    float s = storeysAt(xz);
    return s > 0.5 ? s * STOREY + ROOF : 0.0;
}

bool isTownhall(vec2 xz) {
    return xz.x > -96.0 && xz.x < -44.0 && xz.y > -57.0 && xz.y < 24.0;
}

// ---- the set pieces on the square ----------------------------------------------------------------------
float sdBox(vec3 p, vec3 c, vec3 h) {
    vec3 d = abs(p - c) - h;
    return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
}

// A cypress: an upright cone. Scaled down a little so the march never overshoots its slanted side.
float sdCypress(vec3 p, vec3 base, float h, float r) {
    vec2 q = vec2(length(p.xz - base.xz), p.y - base.y);
    float t = clamp(q.y / h, 0.0, 1.0);
    float d = q.x - r * (1.0 - t);
    return max(d, max(-q.y, q.y - h)) * 0.7;
}

// Materials: 6 stone/white, 7 the flag, 8 bronze, 9 foliage, 10 trunk.
float pieces(vec3 p, out float mat) {
    float d = 1e9;
    mat = 0.0;
    // columns along the committee building's east front, the one the square looks at, every 6 m
    if (p.z > -26.0 && p.z < 4.0) {
        vec3 q = p;
        q.z = mod(q.z + 3.0, 6.0) - 3.0;
        float dc = sdBox(q, vec3(-44.3, 4.8, 0.0), vec3(0.55, 4.8, 0.55));
        if (dc < d) { d = dc; mat = 6.0; }
    }
    // the flag on its roof
    float roofTop = 3.0 * STOREY + ROOF;
    float dp = sdBox(p, vec3(-58.0, roofTop + 4.5, -12.0), vec3(0.12, 4.5, 0.12));
    if (dp < d) { d = dp; mat = 6.0; }
    float df = sdBox(p, vec3(-58.0, roofTop + 8.0, -10.5), vec3(0.06, 0.8, 1.5));
    if (df < d) { d = df; mat = 7.0; }
    // Lenin on his pedestal, on the square's west half
    float dped = sdBox(p, vec3(-22.0, 1.3, 8.0), vec3(1.7, 1.3, 1.7));
    if (dped < d) { d = dped; mat = 6.0; }
    float dfig = sdBox(p, vec3(-22.0, 4.7, 8.0), vec3(0.7, 2.1, 0.55));
    if (dfig < d) { d = dfig; mat = 8.0; }
    // five cypresses in front of the building
    for (int i = 0; i < 5; i++) {
        float dc = sdCypress(p, vec3(-40.5, 0.0, -19.0 + float(i) * 5.5), 6.5, 1.3);
        if (dc < d) { d = dc; mat = 9.0; }
    }
    // trees: the big ones at the square's south-west, a pair across the street to the north
    for (int i = 0; i < 4; i++) {
        vec3 c = i == 0 ? vec3(-38.0, 7.5, -32.0) : i == 1 ? vec3(-57.0, 8.0, -38.0)
               : i == 2 ? vec3(-14.0, 7.0, 34.0) : vec3(22.0, 7.5, 31.0);
        float r = 4.5 + 0.5 * float(i == 1);
        float dl = length(p - c) - r;
        if (dl < d) { d = dl; mat = 9.0; }
        float dt = sdBox(p, vec3(c.x, c.y * 0.5, c.z), vec3(0.28, c.y * 0.5, 0.28));
        if (dt < d) { d = dt; mat = 10.0; }
    }
    return d;
}

vec3 pieceNormal(vec3 p) {
    float m;
    vec2 e = vec2(0.05, 0.0);
    return normalize(vec3(
        pieces(p + e.xyy, m) - pieces(p - e.xyy, m),
        pieces(p + e.yxy, m) - pieces(p - e.yxy, m),
        pieces(p + e.yyx, m) - pieces(p - e.yyx, m)));
}

// ---- the march: heightfield (map buildings + ground) and the set pieces together ------------------------
// mat: 0 ground, 1 wall, 2 roof, 6..10 pieces.
bool march(vec3 ro, vec3 rd, out vec3 pos, out float mat, out float tOut) {
    float t = 0.0;
    if (ro.y > HMAX + 1.0) {
        if (rd.y >= 0.0) return false;
        t = (HMAX + 1.0 - ro.y) / rd.y;   // nothing stands above the tallest block: jump straight down to it
    }
    float tPrev = t;
    for (int i = 0; i < 260; i++) {
        vec3 p = ro + rd * t;
        if (p.y > HMAX + 1.0 && rd.y > 0.0) return false;
        float h = heightAt(p.xz);
        if (h > 0.0 && p.y <= h) {
            // Inside a block: bisect back toward the last point outside it, so the wall or roof is hit where
            // it actually is and not up to a step early (which turned the facades into stepped noise).
            float a = tPrev, b = t;
            for (int k = 0; k < 7; k++) {
                float m = 0.5 * (a + b);
                vec3 q = ro + rd * m;
                if (q.y <= heightAt(q.xz)) b = m; else a = m;
            }
            pos = ro + rd * b;
            h = heightAt(pos.xz);
            mat = pos.y > h - ROOF ? 2.0 : 1.0;
            tOut = b;
            return true;
        }
        if (p.y <= 0.0) {
            // The ground plane: land exactly on it.
            float tg = rd.y < 0.0 ? tPrev + (0.0 - (ro.y + rd.y * tPrev)) / rd.y : t;
            pos = ro + rd * tg;
            pos.y = 0.0;
            mat = 0.0;
            tOut = tg;
            return true;
        }
        float pm;
        float dp = pieces(p, pm);
        if (dp < 0.04 + 0.0015 * t) { pos = p; mat = pm; tOut = t; return true; }
        // Heightfield-safe: never further than the height still to fall, capped so a house is not stepped over.
        float above = p.y - h;
        float step = min(dp, max(0.35, min(above * 0.85, 2.5 + t * 0.02)));
        tPrev = t;
        t += step;
        if (t > 3200.0) return false;
    }
    return false;
}

// How much sun reaches `p`: a second, shorter march toward the sun through the same buildings and set pieces.
// Buildings occlude hard (they are the heightfield); the set pieces give a penumbra from how close the ray
// passed them relative to how far it had travelled. 1 = fully lit, 0 = in shadow.
float sunShadow(vec3 p, vec3 l) {
    float res = 1.0;
    float t = 0.5;
    for (int i = 0; i < 40; i++) {
        vec3 q = p + l * t;
        if (q.y > HMAX + 1.0) break;               // above everything that casts
        float h = heightAt(q.xz);
        if (h > 0.0 && q.y <= h) return 0.0;
        float pm;
        float dp = pieces(q, pm);
        if (dp < 0.02) return 0.0;
        res = min(res, 6.0 * dp / t);
        float above = q.y - h;
        t += clamp(min(dp, above * 0.8), 0.3, 3.0);
        if (t > 260.0) break;
    }
    return clamp(res, 0.0, 1.0);
}

vec3 skyColor(vec3 rd) {
    float t = clamp(rd.y * 1.6 + 0.15, 0.0, 1.0);
    vec3 col = mix(SKY_HORIZ, SKY_TOP, t);
    // low overcast: a slow drift of brighter and darker cloud across the top
    col *= 0.94 + 0.08 * fbm(rd.xz / max(rd.y, 0.08) * 0.05 + vec2(time * 0.01, 0.0));
    return col;
}

// Ground colour from the map: the square's paving, roads, water, greens, yards, and open country beyond.
vec3 groundColor(vec2 xz) {
    vec4 m = mapAt(xz);
    if (m.a < 0.0) {
        vec3 field = mix(GRASS, GRASS2, fbm(xz * 0.06));
        float trees = smoothstep(0.60, 0.72, fbm(xz * 0.13 + 3.0));
        return mix(field, TREE_COL, trees * 0.9);
    }
    float r = m.r * 255.0;
    float b = m.b * 255.0;
    vec3 col = mix(GRASS, GRASS2, fbm(xz * 0.08));
    if (b > 200.0) col = WATER;
    else if (b > 100.0) col = mix(GRASS, TREE_COL, smoothstep(0.45, 0.7, fbm(xz * 0.2)));
    else if (b > 30.0) col = mix(YARD, GRASS2, fbm(xz * 0.15));
    if (r > 100.0 && r < 140.0) {
        // the paved square: 0.5 m tiles, grey with a course of red ones every so often, as in the photo
        vec2 tile = floor(xz / 0.5);
        float course = step(0.80, hash21(vec2(floor(tile.y / 12.0), 0.0)));
        float redRun = course * step(mod(tile.y, 12.0), 1.0);
        vec3 pave = mix(PAVE_GREY, PAVE_RED, redRun) * (0.92 + 0.10 * hash21(tile));
        col = pave;
    } else if (r > 60.0) {
        col = ASPHALT * (0.9 + 0.2 * fbm(xz * 0.7));
        if (r > 240.0) {
            // the main street: a dashed centre line and pale kerbs
            float dash = step(0.5, fract(dot(xz, normalize(vec2(0.97, -0.23))) * 0.12));
            float centre = 1.0 - smoothstep(0.2, 0.45, abs(dot(xz - vec2(30.0, 9.0), vec2(0.23, 0.97))));
            col = mix(col, vec3(0.82, 0.80, 0.42), dash * centre);
        }
    }
    return col;
}

void main() {
    _fragColorOut = vec4(0.0);
    float T = time;
    vec2 sp = fragTexCoord - 0.5;
    sp.x *= res.x / res.y;

    // ---- the camera --------------------------------------------------------------------------------------
    vec3 ro, look;
    vec3 upRef = vec3(0.0, 1.0, 0.0);
    vec2 dir = normalize(vec2(0.97, -0.23));   // the main street, east-south-east
    if (T < T_WALK) {
        float u = T / T_WALK;
        ro = vec3(46.0 - 9.0 * u, 1.7 + 0.12 * sin(T * 1.9), -6.0 + 1.2 * sin(T * 0.45));
        look = vec3(-60.0, 6.5, 6.0);
    } else if (T < T_LIFT) {
        float u = smoothstep(T_WALK, T_LIFT, T);
        ro = vec3(37.0 + 30.0 * u, 1.7 + 68.0 * u * u, -6.0 + 40.0 * u);
        look = mix(vec3(-60.0, 6.5, 6.0), vec3(-40.0, 0.0, 0.0), u);
    } else if (T < T_CLOUD) {
        float u = smoothstep(T_LIFT, T_CLOUD, T);
        ro = vec3(67.0 + 30.0 * u, 70.0 + 230.0 * u, 34.0 + 30.0 * u);
        look = mix(vec3(-40.0, 0.0, 0.0), ro + vec3(dir.x * 60.0, -300.0, dir.y * 60.0), u);
        upRef = normalize(mix(vec3(0.0, 1.0, 0.0), vec3(dir.x, 0.0, dir.y), u));
    } else {
        float s = (T - T_CLOUD) * CRUISE;
        ro = vec3(97.0 + dir.x * s, 300.0 + 6.0 * sin(T * 0.2), 64.0 + dir.y * s);
        look = ro + vec3(dir.x * 60.0, -300.0, dir.y * 60.0);
        upRef = vec3(dir.x, 0.0, dir.y);
    }
    vec3 fwd = normalize(look - ro);
    vec3 rgt = normalize(cross(fwd, upRef));
    vec3 upv = cross(rgt, fwd);
    vec3 rd  = normalize(sp.x * rgt - sp.y * upv + 1.25 * fwd);

    // ---- the scene ---------------------------------------------------------------------------------------
    vec3 col;
    vec3 pos;
    float mat, tHit;
    bool hit = march(ro, rd, pos, mat, tHit);
    if (hit) {
        vec3 n;
        vec3 base;
        if (mat < 0.5) {
            n = vec3(0.0, 1.0, 0.0);
            base = groundColor(pos.xz);
        } else if (mat < 2.5) {
            // Building from the map. The wall normal comes from which way the footprint edge runs.
            float hx = heightAt(pos.xz + vec2(0.6, 0.0)) - heightAt(pos.xz - vec2(0.6, 0.0));
            float hz = heightAt(pos.xz + vec2(0.0, 0.6)) - heightAt(pos.xz - vec2(0.0, 0.6));
            bool townhall = isTownhall(pos.xz);
            if (mat > 1.5) {
                n = vec3(0.0, 1.0, 0.0);
                vec2 cell = floor(pos.xz / 9.0);
                base = mix(ROOF_RED, ROOF_RED2, hash21(cell + 2.3));
            } else {
                n = abs(hx) > abs(hz) ? vec3(-sign(hx), 0.0, 0.0) : vec3(0.0, 0.0, -sign(hz));
                if (abs(hx) < 1e-3 && abs(hz) < 1e-3) n = vec3(0.0, 1.0, 0.0);
                base = townhall ? WALL_TH : WALL * (0.9 + 0.2 * hash21(floor(pos.xz / 9.0)));
                // windows in a grid up the wall; the committee building gets its yellow ground-floor band
                float u = abs(n.x) > 0.5 ? pos.z : pos.x;
                vec2 w = vec2(fract(u / 3.0), fract(pos.y / STOREY));
                float pane = step(0.30, w.x) * step(w.x, 0.70) * step(0.30, w.y) * step(w.y, 0.80);
                base = mix(base, vec3(0.20, 0.24, 0.30), pane * 0.85);
                if (townhall && pos.y < STOREY) base = mix(BAND_TH, base, pane);
            }
        } else {
            n = pieceNormal(pos);
            if (mat < 6.5) base = WALL_TH;
            else if (mat < 7.5) base = pos.y > 3.0 * STOREY + ROOF + 7.6 ? vec3(0.80, 0.12, 0.12) : vec3(0.10, 0.55, 0.20);
            else if (mat < 8.5) base = BRONZE;
            else if (mat < 9.5) base = TREE_COL * (0.85 + 0.4 * fbm(pos.xz * 1.5 + pos.y));
            else base = vec3(0.30, 0.22, 0.16);
        }
        // Overcast sky light from above, plus a low sun that casts: the sun term is the diffuse against the
        // sun direction times what the shadow march lets through (offset off the surface so a face never
        // shadows itself), and a touch of contact darkening where the ground meets a wall or a piece.
        vec3 sun = normalize(SUN_DIR_C);
        float skyLight = 0.40 + 0.25 * clamp(n.y * 0.7 + 0.3, 0.0, 1.0);
        float diff = clamp(dot(n, sun), 0.0, 1.0);
        float shade = diff > 0.0 ? sunShadow(pos + n * 0.35, sun) : 0.0;
        float lit = skyLight + 0.70 * diff * shade;
        if (mat < 0.5) {
            float pm;
            float near = min(pieces(pos + vec3(0.0, 0.4, 0.0), pm), 6.0);
            float wallNear = heightAt(pos.xz + vec2(1.2, 0.0)) + heightAt(pos.xz - vec2(1.2, 0.0))
                           + heightAt(pos.xz + vec2(0.0, 1.2)) + heightAt(pos.xz - vec2(0.0, 1.2));
            lit *= 0.82 + 0.18 * smoothstep(0.0, 4.0, near);
            lit *= wallNear > 0.0 ? 0.78 : 1.0;
        }
        col = base * lit;
        // Haze by HORIZONTAL distance from the camera, so the top-down view from 300 m stays crisp while
        // the street recedes into it toward the horizon on the square.
        float horiz = length(pos.xz - ro.xz);
        float fog = 1.0 - exp(-horiz * 0.0016);
        col = mix(col, SKY_HORIZ, fog * 0.85);
    } else {
        col = skyColor(rd);
        tHit = 3200.0;
    }

    // ---- the cloud deck ----------------------------------------------------------------------------------
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
                dens += fbm(spos.xz * 0.02 + vec2(0.0, T * 0.15));
            }
            // Thin overcast: a soft pass through the deck on the way up, then only wisps over the town.
            float gain = mix(0.25, 1.1, smoothstep(T_WALK, T_LIFT + 3.0, T));
            gain *= 1.0 - 0.9 * smoothstep(T_CLOUD, T_CLOUD + 4.0, T);
            float cloud = 1.0 - exp(-gain * (dens / 6.0) * (tExit - tEnter) * 0.025);
            col = mix(col, CLOUD_COL, clamp(cloud, 0.0, 1.0));
        }
    }

    col = pow(clamp(col, 0.0, 1.0), vec3(0.92));
    _fragColorOut = vec4(col, 1.0);
}
