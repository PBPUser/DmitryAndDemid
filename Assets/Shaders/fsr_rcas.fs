#version 330

// AMD FidelityFX Super Resolution 1.0 — RCAS, robust contrast-adaptive sharpening, after the reference in
// ffx_fsr1.h (MIT). A 5-tap cross: the sharpening lobe is sized so no channel can overshoot its local
// min/max, then the neighbours are folded in with that negative weight. `sharpness` is RCAS's own scale,
// 0 = strongest, 2 = off (the setting's 0..1 is mapped onto it by FsrPass).
//
// texture0 is the frame to sharpen (a render target read with a positive full-size rect, so it arrives
// upside down — the 1 - y below turns it back); inputSize is its size in pixels.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 inputSize;
uniform float sharpness;

vec3 fetch(ivec2 p) {
    p = clamp(p, ivec2(0), ivec2(inputSize) - 1);
    return texelFetch(texture0, p, 0).rgb;
}

float luma(vec3 c) { return c.b * 0.5 + (c.r * 0.5 + c.g); }

void main() {
    ivec2 ip = ivec2(vec2(fragTexCoord.x, 1.0 - fragTexCoord.y) * inputSize);
    vec3 b = fetch(ip + ivec2( 0, -1));
    vec3 d = fetch(ip + ivec2(-1,  0));
    vec3 e = fetch(ip);
    vec3 f = fetch(ip + ivec2( 1,  0));
    vec3 h = fetch(ip + ivec2( 0,  1));

    // Noise detection: a soft weight that backs the sharpening off on high-frequency noise.
    float bL = luma(b), dL = luma(d), eL = luma(e), fL = luma(f), hL = luma(h);
    float nz = 0.25 * (bL + dL + fL + hL) - eL;
    float mxL = max(max(bL, dL), max(max(eL, fL), hL));
    float mnL = min(min(bL, dL), min(min(eL, fL), hL));
    nz = clamp(abs(nz) * (1.0 / max(mxL - mnL, 1e-4)), 0.0, 1.0);
    nz = -0.5 * nz + 1.0;

    vec3 mn4 = min(min(b, d), min(f, h));
    vec3 mx4 = max(max(b, d), max(f, h));
    vec2 peakC = vec2(1.0, -4.0);
    vec3 hitMin = mn4 * (1.0 / (4.0 * max(mx4, vec3(1e-4))));
    vec3 hitMax = (peakC.x - mx4) * (1.0 / min(4.0 * mn4 + peakC.y, vec3(-1e-4)));
    vec3 lobeRGB = max(-hitMin, hitMax);
    float lobe = max(-(0.25 - 1.0 / 16.0), min(max(max(lobeRGB.r, lobeRGB.g), lobeRGB.b), 0.0)) * exp2(-sharpness);
    lobe *= nz;
    float rcpL = 1.0 / (4.0 * lobe + 1.0);
    vec3 pix = ((b + d + f + h) * lobe + e) * rcpL;
    gl_FragColor = vec4(clamp(pix, 0.0, 1.0), 1.0);
}
