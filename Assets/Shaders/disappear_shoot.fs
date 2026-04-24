#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float u_time;
uniform vec2 pos;

const vec3 color = vec3(0.008, 0.075, 0.004);
const vec2 offset_per_second = vec2(.01, 0.1);
const vec2 screen_size = vec2(384., 448.);

void main()
{
    vec2 uv_offset = uv;
    uv_offset = (uv_offset + vec2(1.))/2.;
    uv_offset.y = 1.-uv_offset.y;
    float d = distance(uv_offset*screen_size, pos)/448.;
    float alpha = 1.-smoothstep(u_time, u_time, d*4.);
    float alphax = clamp(1.-(u_time * 2.), 0., 1.);
    //gl_FragColor = vec4(color, alpha*alphax);
    gl_FragColor = vec4(color, alphax*alpha);
}
