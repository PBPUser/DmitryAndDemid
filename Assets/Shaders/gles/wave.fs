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
uniform float scale;
uniform float xPower;
uniform float offsetX;
uniform float offsetY;
uniform vec4 color;

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 uvx = (uv + vec2(1.)) / vec2(2.);
    float wave = 1.0-smoothstep((1.-uvx.y)-sin(pow(uvx.x,xPower)*scale+offsetX)/3.14+offsetY, 0., 1.);
    _fragColorOut = color * wave;
}
