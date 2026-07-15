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
uniform float time;
uniform float realTime;
uniform float scale;
uniform vec2 position;
const vec2 bulletSize = vec2(6,16);
const vec2 res = vec2(384., 448.);

void main(){
    _fragColorOut = vec4(0.0);
    _fragColorOut = texture(texture0, fragTexCoord);
    vec2 texelSize = vec2(1)/res;
    vec2 textureCoord = vec2(fragTexCoord.x, 1.0-fragTexCoord.y)*res;
    vec2 coordFromj = position - bulletSize/2.0, coordToj = position + bulletSize/2.0, coordFrom, coordTo;
    float opacity = 1.0-pow(time * 2.0 - 1.0, 2.0);
    float opacity2 = 0.0;
    for(float i = -3.0; i < 0.0; i++){
        coordFrom = coordFromj;
        coordTo = coordToj;
        coordFrom.y += i * 16.0 * time;
        coordTo.y += i * 16.0 * time;
        if(textureCoord.x > coordFrom.x && textureCoord.y > coordFrom.y && textureCoord.x < coordTo.x && textureCoord.y < coordTo.y){
            opacity2 += (2.0-abs(i)+1.0) * 0.25;
        }
    }
    _fragColorOut = mix(_fragColorOut, vec4(1), opacity * opacity2);
}
