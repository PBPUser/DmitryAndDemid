#version 330

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
    vec2 uvx = (uv + vec2(1.)) / vec2(2.);
    float wave = 1-smoothstep((1.-uvx.y)-sin(pow(uvx.x,xPower)*scale+offsetX)/3.14+offsetY, 0., 1.);
    gl_FragColor = color * wave;
}
