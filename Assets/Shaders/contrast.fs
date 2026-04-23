#version 330

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
    vec2 uv = vec2(fragTexCoord.x, fragTexCoord.y);
    vec4 texelColor = texture2D(texture0, uv);
    float contrastColor = (texelColor.x + texelColor.y + texelColor.z) < .5 ? 0. : 1.;
    gl_FragColor = vec4(mix((texelColor * colDiffuse).xyz, vec3(contrastColor), contrastLevel), opacity * texelColor.a * (0.8 + 0.2 * (1-contrastColor)*contrastLevel ));
}
