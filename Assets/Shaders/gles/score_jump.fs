#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float letterWidth;
uniform float textWidth;
uniform vec2 resolution;
float easing(float x){
    return 1.0-pow(1.0-x, 5.0);
}

void main(){
    _fragColorOut = vec4(0.0);
    if(time < 0.4)
        return;
    float x = (fragTexCoord.x * resolution.x - (resolution.x - textWidth) / 2.0) / textWidth;
    if(mod(abs(x), 1.0) != x)
        return;
    float lw = letterWidth / textWidth;
    float letter = floor(x / lw);
    float letterFE = ((textWidth/letterWidth)-letter) - 1.0;
    float t = time - 0.4;
    float lt = t - 0.083 * letterFE;
    if(lt < 0.0)
        return;
    float ymp = lt / 0.083;
    float ymp2 = lt / 0.33;
    float opacity = clamp(ymp, 0.0, 1.0);
    float darkness = lt > 0.55 || lt < 0.083 || mod(lt / 0.033, 1.0) > 0.67  ? 1.0 : 0.25; 
    _fragColorOut = texture(texture0, fragTexCoord - vec2(0.0, easing(clamp(1.0-abs(ymp2-1.0), 0.0, 1.0)) * .04));
    _fragColorOut = vec4(_fragColorOut.rgb * darkness, _fragColorOut[3] * opacity);
}
