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
uniform sampler2D textureAttack;
uniform float time;
uniform float scale;
uniform vec2 position;

const vec2 res = vec2(384., 448.);

vec2 zoom(vec2 uv,float factor, vec2 pivot){
    vec2 pos = uv - pivot;
    pos *= factor;
    return pos + pivot;
}

vec2 rotate(vec2 uv, float angle, vec2 pivot){
    float c = cos(angle);
    float s = sin(angle);
    mat2 rot = mat2(vec2(c, -s), vec2(s, c));
    return (uv - pivot) * rot + pivot;
}

void main(){
    _fragColorOut = vec4(0.0);
    _fragColorOut = texture(texture0, fragTexCoord);
    vec2 tSize=vec2(textureSize(textureAttack, 0));
    vec2 tSize2=vec2(textureSize(texture0, 0));
    vec2 r = zoom((fragTexCoord + vec2(0.0, -.7)) * vec2(1.0, tSize2.y/tSize2.x * tSize.x/tSize.y * -1.0), 4.0, vec2(0.5));
    float angle = 0.4;
    vec2 t = rotate(r, angle, vec2(0));
    if(mod(t.y + 100.0, 2.0) > 1.0){
        r.x += time;
    }
    else{
        r.x -= time;
    }
    r = rotate(r, angle, vec2(0));
    if(abs(r.y) < 4.0){
        vec4 j = texture(textureAttack, r);
        _fragColorOut = mix(_fragColorOut, vec4(j.rgb, 1.0), j[3] * .3 * clamp(2.0 - abs(time * 4.0 - 2.0), 0.0, 1.0));
    }
    
}
