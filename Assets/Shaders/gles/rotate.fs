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
uniform float roll;
uniform float pitch;
uniform float yaw;
uniform float focal;

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 aspect = vec2(1.);
    vec3 pos = vec3((uv) * aspect, 0.0);
    float cr = cos(roll), sr = sin(roll);
    float cy = cos(yaw),   sy = sin(yaw);
    float cp = cos(pitch), sp = sin(pitch);
    mat3 rx = mat3(
        vec3(1.0, 0.0,  0.0),
        vec3(0.0, cp,   sp),
        vec3(0.0, -sp,  cp)
    );
    mat3 ry = mat3(
        vec3(cy, 0.0, -sy),
        vec3(0.0, 1.0,  0.0),
        vec3(sy, 0.0,  cy)
    );
    mat3 rz = mat3(
        vec3(cr, sr, 0.0),
        vec3(-sr, cr, 0.0),
        vec3(0.0, 0.0, 1.0)
    );
    mat3 rot = rz * ry * rx;
    vec3 rotated = rot * pos;
    float z = focal + rotated.z;
    if (z <= 0.001)
      discard;
    vec2 projUV = (rotated.xy / z) / aspect + 0.5;
    if(projUV.x < 0. || projUV.x > 1. || projUV.y < 0. || projUV.y > 1.)
        _fragColorOut = vec4(0.);
    else
        _fragColorOut = texture(texture0, projUV);
}
