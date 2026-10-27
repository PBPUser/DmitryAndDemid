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

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 uv = vec2(fragTexCoord.x, 1.-fragTexCoord.y);
    vec4 texelColor = texture(texture0, uv);
    _fragColorOut = texelColor * colDiffuse;
}
