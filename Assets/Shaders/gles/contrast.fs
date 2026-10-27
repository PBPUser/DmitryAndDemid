#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float contrastLevel;
uniform float opacity;

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 uv = fragTexCoord;
    vec4 texelColor = texture(texture0, uv);
    float contrastColor = (texelColor.x + texelColor.y + texelColor.z) < .5 ? 0. : 1.;
    _fragColorOut = vec4(mix((texelColor * colDiffuse).xyz, vec3(contrastColor), contrastLevel), opacity * texelColor.a * (0.8 + 0.2 * (1.0-contrastColor)*contrastLevel ));
}
