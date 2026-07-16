#version 330
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;

void main(){
    float y = abs(mod(fragTexCoord.y,1));
	float py = pow(-1.8+(1-y),2);
	float s = sin(pow((y+2)*4,2) * 1-(14.9*pow(y,3))) * py;
    if(y > .25 || s < 0){
        gl_FragColor = vec4(0);
        return;
    }
    float transparency = pow(4-abs(4-((fragTexCoord.x-(y/4)) * 8)), 2*s) / 4.;
    float transparency2 = pow(abs(((fragTexCoord.x-(y/4)) * 8)), 2) / 4.;
    if(y > .125)
        transparency = transparency2;
    gl_FragColor = vec4(vec3(pow(s,4)*.75,1,pow(s,4)*.75),transparency * pow(s,1));
}