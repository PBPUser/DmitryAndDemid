#version 410
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 position;
uniform float time;
uniform float realTime;
uniform int pointsCount;
uniform vec4 points;
uniform float progress;
const vec2 res = vec2(384, 448);
const float radius = 48;
const float size = 2;
const float borderWidth = 1;
float findAngle(vec2 offset){
    return 1-mod(((atan(offset.y, offset.x) / 3.141)+1)/2 + .25, 1);
}

void main(){
    vec2 ftc = vec2(fragTexCoord.x, 1-fragTexCoord.y) * res;
    float d = distance(ftc, position);
    bool isIn = d < radius;
    d = abs(radius - d);
    if(d > 3.0)
        return;
    else if(d > 2.0){
        gl_FragColor = vec4(0,1,0,.75);
        return;
    }
    else if(findAngle(position-ftc) > progress || d > 1.5 || d > 1.0 && !isIn){
        return;
    }
    else if(d > 1.0 && d <= 1.5 && isIn){
        gl_FragColor = vec4(0, 1, 0, 1);
        return;
    }
    gl_FragColor = vec4(1);
}