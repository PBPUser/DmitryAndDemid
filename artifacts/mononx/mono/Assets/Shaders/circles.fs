#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;      // 0 -> 1 across the effect's lifetime; drives the fade-out envelope
uniform float realTime;  // seconds; drives the continuous outward swell of the shells
uniform vec2 position;   // emanation centre, in playfield pixels (res space)

const vec2 res = vec2(384.0, 448.0);
const int RINGS = 5;

// Every literal is written as a float on purpose: some drivers (Intel) reject int literals promoted into
// float contexts and the whole shader then fails to compile. Keep the decimal points.

// A soft, semi-transparent spherical shell at `radius` with half-thickness `thickness` (both in pixels).
float shell(vec2 pPx, float radius, float thickness){
    float d = abs(distance(pPx, position) - radius);
    return 1.0 - smoothstep(0.0, thickness, d);
}

void main(){
    gl_FragColor = texture(texture0, fragTexCoord);

    // fragTexCoord's y is flipped relative to playfield space (negative-height source rects), so unflip
    // before comparing against `position`, which is authored in playfield pixels.
    vec2 pPx = vec2(fragTexCoord.x, 1.0 - fragTexCoord.y) * res;

    float fade = clamp(1.0 - time, 0.0, 1.0);   // the whole burst fades out as the effect ends
    fade *= fade;                               // ease the tail so it lingers then vanishes softly

    vec3 tint = vec3(0.62, 0.80, 1.0);          // cool, volumetric blue-white glow
    float acc = 0.0;
    for(int i = 0; i < RINGS; i++){
        // Each shell is born at the centre and swells outward, staggered in phase; fract() recycles it so
        // the emanation is continuous. Thicker as it grows, and faintest at birth and at the far edge —
        // the overlap of several translucent shells is what reads as volume rather than flat rings.
        float phase = fract(realTime * 0.45 + float(i) / float(RINGS));
        float radius = phase * 240.0;
        float thickness = 18.0 + phase * 42.0;
        float born = sin(phase * 3.14159265);
        acc += shell(pPx, radius, thickness) * born;
    }
    // A soft bright heart at the centre so the shells appear to pour out of something.
    acc += (1.0 - smoothstep(0.0, 70.0, distance(pPx, position))) * 0.6;

    acc = clamp(acc, 0.0, 1.0) * fade * 0.33;   // overall translucency
    gl_FragColor = mix(gl_FragColor, vec4(tint, 1.0), acc);
}
