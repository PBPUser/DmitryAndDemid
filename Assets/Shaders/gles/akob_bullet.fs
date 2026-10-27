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
uniform vec2 resolution;
uniform vec2 output_resolution;
uniform vec2 position;
uniform int time;
uniform int created_at;

vec4 qexture(sampler2D sampler, vec2 pos){
    vec2 uvs = pos * output_resolution;
	if(uvs.x > position.x && uvs.y > position.y && uvs.x < position.x + resolution.x && uvs.y < position.y + resolution.y)
		return texture(sampler, pos);
	return vec4(0);
}

void main()
{
    _fragColorOut = vec4(0.0);
	vec4 texelColor = qexture(texture0, fragTexCoord);
	_fragColorOut = texelColor * colDiffuse;
}
