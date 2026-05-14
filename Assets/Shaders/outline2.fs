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

vec4 textureA(sampler2D t, vec2 pos){
	if(pos.x < 0 || pos.x > 1 || pos.y < 0 || pos.y > 1)
		return vec4(0);
	return texture(t,pos);
}

void main(){
	gl_FragColor = texture(texture0, fragTexCoord);
	vec2 texel = vec2(1)/res;
	vec2 p1 = fragTexCoord;
	for(float x = 0; x < border_width / 2; x+=1)
	for(float y = 0; y < border_width / 2; y+=1){
		if(textureA(texture0, fragTexCoord+vec2(x,y)*texel)[3] > textureA(texture0, p1)[3])
			if(distance(p1, fragTexCoord) < distance(vec2(x,y)*texel, vec2(0)))
				p1 = fragTexCoord+vec2(x,y)*texel;
		if(textureA(texture0, fragTexCoord+vec2(x,-y)*texel)[3] > textureA(texture0, p1)[3])
			if(distance(p1, fragTexCoord) < distance(vec2(x,-y)*texel, vec2(0)))
				p1 = fragTexCoord+vec2(x,-y)*texel;
		if(textureA(texture0, fragTexCoord+vec2(-x,y)*texel)[3] > textureA(texture0, p1)[3])
			if(distance(p1, fragTexCoord) < distance(vec2(-x,y)*texel, vec2(0)))
				p1 = fragTexCoord+vec2(-x,y)*texel;
		if(textureA(texture0, fragTexCoord+vec2(-x,-y)*texel)[3] > textureA(texture0, p1)[3])
			if(distance(p1, fragTexCoord) < distance(vec2(-x,-y)*texel, vec2(0)))
				p1 = fragTexCoord+vec2(-x,-y)*texel;
	}
	float s = textureA(texture0, zoom(
		border_width, fragTexCoord, p1
	))[3];
	gl_FragColor[3] = clamp(gl_FragColor[3] + s, 0,1) ;
}
