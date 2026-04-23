#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float height;

void main()
{
    gl_FragColor = vec4(vec3(0.),.7*(1.-pow(abs(uv.x),4.)));
}
