#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// The mystical toilet arriving: a big semi-transparent brown circle that collapses ONTO it — starting wide
// enough to swallow most of the playfield and shrinking down to the sprite — while lightning strikes inward
// along its shrinking rim. Paired with a short screen shake on the C# side (see GameBox.SpawnMysticalToilet).
//
// Same conventions as circles.fs: a full playfield pass over the already-drawn scene, `position` in playfield
// pixels, `time` running 0 -> 1 across the effect's life, `realTime` in seconds for the flicker. Every literal
// is written with a decimal point on purpose — some drivers reject int literals promoted to float and then the
// whole shader fails to compile.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;      // 0 -> 1 across the effect's lifetime
uniform float realTime;  // seconds; drives the lightning's flicker
uniform vec2 position;   // the toilet's centre, in playfield pixels

const vec2 res = vec2(384.0, 448.0);
const float START_RADIUS = 300.0;   // wide enough to reach well past the playfield's edges
const float END_RADIUS = 26.0;      // about the toilet sprite's own half-size
const int BOLTS = 7;

const vec3 BROWN = vec3(0.40, 0.24, 0.10);
const vec3 SPARK = vec3(1.00, 0.86, 0.55);   // the lightning, warm white so it reads against the brown

float hash11(float n) { return fract(sin(n) * 43758.5453); }

void main()
{
    _fragColorOut = vec4(0.0);
    vec4 base = texture(texture0, fragTexCoord);

    // fragTexCoord's y is flipped relative to playfield space (negative-height source rects), so unflip before
    // comparing against `position`, which is authored in playfield pixels.
    vec2 pPx = vec2(fragTexCoord.x, 1.0 - fragTexCoord.y) * res;
    vec2 delta = pPx - position;
    float dist = length(delta);

    float t = clamp(time, 0.0, 1.0);
    // Collapse fast at first and settle at the end, so it reads as being pulled in rather than sliding shut.
    float ease = 1.0 - (1.0 - t) * (1.0 - t);
    float radius = mix(START_RADIUS, END_RADIUS, ease);
    float fade = 1.0 - t;          // the whole flourish thins out as it closes
    fade *= fade;

    // The body of the circle, plus a denser rim so the shrinking edge stays readable once it is small.
    float disc = 1.0 - smoothstep(radius * 0.55, radius, dist);
    float rim = 1.0 - smoothstep(0.0, radius * 0.22, abs(dist - radius));
    float brown = clamp(disc * 0.55 + rim * 0.75, 0.0, 1.0);

    // Lightning striking inward. Each bolt owns an angle that is re-drawn ~14 times a second (the floor() on
    // realTime), and wobbles with distance so it forks rather than running straight. The width is angular, so
    // a bolt naturally tapers as it converges — which is what sells it as travelling INTO the centre.
    float angle = atan(delta.y, delta.x);
    float bolts = 0.0;
    for (int i = 0; i < BOLTS; i++)
    {
        float seed = float(i) + floor(realTime * 14.0) * 0.37;
        float root = hash11(seed) * 6.2831853;
        float wobble = sin(dist * 0.13 + seed * 7.0) * 0.11 + sin(dist * 0.31 + seed * 13.0) * 0.05;
        float offset = abs(mod(angle - root - wobble + 3.1415927, 6.2831853) - 3.1415927);
        float halfWidth = 0.018 + 0.022 * hash11(seed + 3.0);
        float line = 1.0 - smoothstep(0.0, halfWidth, offset);
        // Live only inside the closing circle, and brightest as it nears the middle.
        line *= smoothstep(radius * 1.02, radius * 0.15, dist);
        bolts = max(bolts, line * (0.55 + 0.45 * hash11(seed + 9.0)));
    }

    vec3 tint = mix(BROWN, SPARK, clamp(bolts, 0.0, 1.0));
    float amount = clamp(brown * 0.72 + bolts * 0.85, 0.0, 1.0) * fade;
    _fragColorOut = mix(base, vec4(tint, 1.0), amount);
}
