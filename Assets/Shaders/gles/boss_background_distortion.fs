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
const vec2 res = vec2(384., 448.);
const float size = 160.;
const vec4 colorOverlay = vec4(.3, .1, .6, 1);


vec2 zoom(vec2 coord, vec2 pivot, float strength){
    return (coord-pivot) * strength + pivot;
}

void main(){
    _fragColorOut = vec4(0.0);
    vec2 texelSize = vec2(1)/res;
    vec2 ftc = fragTexCoord;
    vec2 pos = position;
    pos.y = res.y - pos.y;
    vec2 ftcR = ftc * res;
    float tt = time > 1.0 ? 2.0-time : time;
    float d = clamp(1.0-(distance(ftcR, pos)/(size*tt)), 0.0, 1.0);
    ftcR *= vec2(1.0) + vec2(sin(realTime * 2.0 +  (uv.y* 5.0+realTime*2.0) ), cos(realTime * 2.0 + (realTime*2.0+uv.x* 5.0))) * 0.05 * d;
    _fragColorOut = texture(texture0, zoom(ftcR, pos, 1.0 - pow(d, 4.0) * .25) / res);
    _fragColorOut = mix(_fragColorOut,colorOverlay, .4 * d);
}
