#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float statement;
uniform vec3 color;
uniform vec2 position;

const vec2 res = vec2(384, 448);

void main(){
    vec2 pos = (position / res) * 2. - 1.;
    pos.y = 1-pos.y;
    float transparency = 1.-abs(1.-statement);
    vec4 _color = texture(texture0, fragTexCoord);
    _color[3] *= transparency;
    
    gl_FragColor = _color;
}
