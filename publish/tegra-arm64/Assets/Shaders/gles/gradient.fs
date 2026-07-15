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
uniform float border_width;
uniform vec2 res;
const float color_from = .8;
const float color_to = 1.;
const vec4 border_color = vec4(0.,0.,0.,1.);

vec2 zoom(float factor, vec2 pos, vec2 center){
	return (pos-center)/factor+center;
}
void main(){
    _fragColorOut = vec4(0.0);
	_fragColorOut = texture(texture0, fragTexCoord);
	if(_fragColorOut[3] > 0.01){
		_fragColorOut[0] = _fragColorOut[1] = _fragColorOut[2] = mix(color_from, color_to, mod(fragTexCoord.y,1.));
	}
	vec2 texel = vec2(1)/res;
	vec2 p1 = fragTexCoord;
	vec2 p2 = fragTexCoord;
	vec2 p3 = fragTexCoord;
	vec2 p4 = fragTexCoord;
	for(float x = 0.0; x < border_width; x+=1.0)
	for(float y = 0.0; y < border_width; y+=1.0){
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
	_fragColorOut[3] = clamp(_fragColorOut[3], 0.0,1.0)+ (s);
}
