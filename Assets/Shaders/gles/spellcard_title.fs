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

void main(){
    _fragColorOut = vec4(0.0);
    float y = abs(mod(fragTexCoord.y,1.0));
	float py = pow(-1.8+(1.0-y),2.0);
	float s = sin(pow((y+2.0)*4.0,2.0) * 1.0-(14.9*pow(y,3.0))) * py;
    if(y > .25 || s < 0.0){
        _fragColorOut = vec4(0);
        return;
    }
    float transparency = pow(4.0-abs(4.0-((fragTexCoord.x-(y/4.0)) * 8.0)), 2.0*s) / 4.;
    float transparency2 = pow(abs(((fragTexCoord.x-(y/4.0)) * 8.0)), 2.0) / 4.;
    if(y > .125)
        transparency = transparency2;
    _fragColorOut = vec4(vec3(pow(s,4.0)*.75,1.0,pow(s,4.0)*.75),transparency * pow(s,1.0));
}
