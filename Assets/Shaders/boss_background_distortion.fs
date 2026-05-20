#version 330

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
    vec2 texelSize = vec2(1)/res;
    vec2 ftc = fragTexCoord;
    vec2 pos = position;
    pos.y = res.y - pos.y;
    vec2 ftcR = ftc * res;
    float tt = time > 1 ? 2-time : time;
    float d = clamp(1-(distance(ftcR, pos)/(size*tt)), 0, 1);
    ftcR *= vec2(1) + vec2(sin(realTime * 2 +  (uv.y* 5+realTime*2) ), cos(realTime * 2 + (realTime*2+uv.x* 5))) * 0.05 * d;
    gl_FragColor = texture(texture0, zoom(ftcR, pos, 1 - pow(d, 4) * .25) / res);
    gl_FragColor = mix(gl_FragColor,colorOverlay, .4 * d);
}