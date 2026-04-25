#version 330

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
    float shadow = 0.;
    for(float j = 1; j < depth; j++){
        vec2 off = vec2(1.33*j) * direction;
        shadow += texture2D(texture0, fragTexCoord+(off/res))[3] * 0.35 / j;
        shadow += texture2D(texture0, fragTexCoord-(off/res))[3] * 0.35 / j;
    }
    vec4 color = texture(texture0, fragTexCoord);
    gl_FragColor = mix(vec4(1.0, 1.0, 1.0, shadow) ,color, color[3]);
}
