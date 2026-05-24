#version 410
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
    return 1-pow(1-x, 5);
}

void main(){
    if(time < 0.4)
        return;
    float x = (fragTexCoord.x * resolution.x - (resolution.x - textWidth) / 2) / textWidth;
    if(mod(abs(x), 1) != x)
        return;
    float lw = letterWidth / textWidth;
    float letter = floor(x / lw);
    float letterFE = ((textWidth/letterWidth)-letter) - 1;
    float t = time - 0.4;
    float lt = t - 0.083 * letterFE;
    if(lt < 0)
        return;
    float ymp = lt / 0.083;
    float ymp2 = lt / 0.33;
    float opacity = clamp(ymp, 0, 1);
    float darkness = lt > 0.55 || lt < 0.083 || mod(lt / 0.033, 1) > 0.67  ? 1 : 0.25; 
    gl_FragColor = texture(texture0, fragTexCoord - vec2(0, easing(clamp(1-abs(ymp2-1), 0, 1)) * .04));
    gl_FragColor = vec4(gl_FragColor.rgb * darkness, gl_FragColor[3] * opacity);
}