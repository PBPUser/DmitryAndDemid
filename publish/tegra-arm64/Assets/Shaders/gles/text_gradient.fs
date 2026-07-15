#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 ftc = fragTexCoord;
    ftc.y = 1.0-ftc.y;
    _fragColorOut = texture(texture0,ftc) * (1.0 - pow(ftc.y, 2.0) * 0.2);
}
