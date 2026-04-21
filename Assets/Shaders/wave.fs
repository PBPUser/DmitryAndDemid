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
    float wave = 1-smoothstep(uv.y-sin(pow(uv.x,xPower)*scale+offsetX)/3.14+offsetY, 0., 1.);
    vec4 texelColor = texture2D(texture0, fragTexCoord);
    gl_FragColor = color * wave;
}
