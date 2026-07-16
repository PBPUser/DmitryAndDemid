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
uniform float depth;
uniform vec2 res;

const vec2 direction = vec2(-1.0,1.0);

void main()
{
    _fragColorOut = vec4(0.0);
    float shadow = 0.;
    for(float j = 1.0; j < depth; j++){
        vec2 off = vec2(1.33*j) * direction;
        shadow += texture(texture0, fragTexCoord+(off/res))[3] * 0.35 / j;
        shadow += texture(texture0, fragTexCoord-(off/res))[3] * 0.35 / j;
    }
    vec4 color = texture(texture0, fragTexCoord);
    _fragColorOut = mix(vec4(1.0, 1.0, 1.0, shadow) ,color, color[3]);
}
