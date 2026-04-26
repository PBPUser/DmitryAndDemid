#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float border_width;

const vec3 from_color = vec3(1., 1., 1.);
const vec3 to_color = vec3(.85, .85, .85);

void main()
{
    vec4 tcolor = texture(texture0, fragTexCoord);
    gl_FragColor = vec4(mix(from_color,to_color,uv.y+2.-1.),tcolor[3]);
}
