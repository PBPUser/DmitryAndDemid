#version 330

// One point light, stamped into a background's light map (see Common/BackgroundLighting.cs). The caller draws
// a quad covering the light's bounding square and blends it additively over the map, one draw per light -
// which is why there is no light array here, and why the system takes any number of lights without the
// uniform-array support the backends lack.
//
// Additive blending is SrcAlpha/One, so the fragment's own alpha is the multiplier the hardware applies: this
// writes the light's colour in rgb and the distance falloff in alpha, and the blend does colour x falloff.
//
// Uniforms:
//   light_color  rgb = colour, a = intensity (the caller has already folded pulse and flicker into it)
//   falloff      curve exponent - 1 is a linear cone, 2 reads like a normal lamp, higher tightens the core

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // unused; the light is fully procedural (same as houses.fs)
uniform vec4 colDiffuse;
uniform vec4 light_color;
uniform float falloff;

void main()
{
    // The quad covers the light's bounding square, so this is the distance from its centre in radii.
    float d = length(fragTexCoord - 0.5) * 2.0;
    float f = pow(clamp(1.0 - d, 0.0, 1.0), falloff);
    gl_FragColor = vec4(light_color.rgb * light_color.a, f);
}
