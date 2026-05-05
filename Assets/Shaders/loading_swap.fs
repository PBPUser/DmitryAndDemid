#version 330

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDifuse;
uniform float time;
uniform sampler2D forkTexture;
uniform vec2 forkSize;

const mat4x4 forks1 = mat4x4(
	vec4(2, 0, 2, .4),
	vec4(2, .3, 2, 0),
	vec4(2, 0, 2, .4),
	vec4(2, .4, 2, 0)
);
const mat4x4 forks2 = mat4x4(
	vec4(1, 1, 1, 1),
	vec4(1, 1, 1, 1),
	vec4(1, 1, 1, 1),
	vec4(1, 1, 1, 1)
);

void main(){
	float t = mod(time, 1.);
    float transparency = 1.;
	vec2 pos = fragTexCoord;
	vec2 p[8];
	for(int i = 0; i < 8; i++)
	{
		p[i] = vec2(forks1[i/4][i%4], 0.0625 * i); 
	}
	float currentIndex = (1-(fragTexCoord.y+1)) * 8;
	float iD = mod(currentIndex, 1.);
	int cI1 = int(currentIndex);
	int cI2 = int(currentIndex+1)%8;
	float jv1 = clamp(p[cI1][0],-2,2);
	float jv2 = clamp(p[cI2][0],-2,2);
	float sx = jv1 + (jv2-jv1)*iD;
	if(pow(t, sx) * t * 2. < fragTexCoord.x)
		transparency = 0;
	for(int i = 0; i < 8; i++)
	{
		p[i] = vec2(forks1[i/4+2][i%4], 0.0625 * i); 
	}
	jv1 = clamp(p[cI1][0],-1,1);
	jv2 = clamp(p[cI2][0],-1,1);
	pos.y = 1-pos.y;
	sx = jv1 + (jv2-jv1)*iD;
	if(pow(t, sx) * (t * 2.) > 1-fragTexCoord.x)
		transparency = 1;
	if(time > 1)
		transparency = 1-transparency;
	if(time >= 2)
		transparency = 0;
    vec4 color = texture(texture0, fragTexCoord);
	color[3] = transparency;
	gl_FragColor = color;
}