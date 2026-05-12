#version 330
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float border_width;
uniform vec2 res;
const vec3 from_color = vec3(1.);
const vec3 to_color = vec3(.6);
const vec4 border_color = vec4(0.,0.,0.,1.);

vec2 zoom(float factor, vec2 pos, vec2 center){
	return (pos-center)/factor+center;
}
void main(){
	gl_FragColor = texture(texture0, fragTexCoord);
	vec2 texel = vec2(1)/res;
	vec2 p1 = fragTexCoord;
	vec2 p2 = fragTexCoord;
	vec2 p3 = fragTexCoord;
	vec2 p4 = fragTexCoord;
	for(float x = 0; x < border_width; x+=1)
	for(float y = 0; y < border_width; y+=1){
		if(texture(texture0, fragTexCoord+vec2(x,y)*texel)[3] > 0.)
			if(distance(p1, fragTexCoord) < distance(vec2(x,y)*texel, vec2(0)))
				p1 = fragTexCoord+vec2(x,y)*texel;
		if(texture(texture0, fragTexCoord+vec2(x,-y)*texel)[3] > 0.)
			if(distance(p2, fragTexCoord) < distance(vec2(x,-y)*texel, vec2(0)))
				p2 = fragTexCoord+vec2(x,-y)*texel;
		if(texture(texture0, fragTexCoord+vec2(-x,y)*texel)[3] > 0.)
			if(distance(p3, fragTexCoord) < distance(vec2(-x,y)*texel, vec2(0)))
				p3 = fragTexCoord+vec2(-x,y)*texel;
		if(texture(texture0, fragTexCoord+vec2(-x,-y)*texel)[3] > 0.)
			if(distance(p4, fragTexCoord) < distance(vec2(-x,-y)*texel, vec2(0)))
				p4 = fragTexCoord+vec2(-x,-y)*texel;
	}
	float s = .01;
	s += texture(texture0, zoom(
		border_width, fragTexCoord, p1
	))[3];
	s += texture(texture0, zoom(
		border_width, fragTexCoord, p2
	))[3];
	s += texture(texture0, zoom(
		border_width, fragTexCoord, p3
	))[3];
	s += texture(texture0, zoom(
		border_width, fragTexCoord, p4
	))[3];
	gl_FragColor[3] = clamp(gl_FragColor[3], 0,1)+ (s/2.);
}
