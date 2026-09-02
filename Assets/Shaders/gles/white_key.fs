#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// A sprite bullet whose art sits on a plain white ground — object.png is a JPEG, so it carries no alpha of
// its own. Anything close to white goes transparent, the rest draws as-is; otherwise identical to
// basic_bullet_shader (the atlas sub-rect mask and the opacity uniform). Bullet visuals name it as Effect.

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
uniform int opacity;

// Sample the bullet's own sub-rect on the texture (everything outside is empty).
vec4 qexture(sampler2D tex, vec2 pos){
    vec2 uvs = pos * output_resolution;
	if(uvs.x > position.x && uvs.y > position.y && uvs.x < position.x + resolution.x && uvs.y < position.y + resolution.y)
		return texture(tex, pos);
	return vec4(0);
}

void main()
{
	_fragColorOut = vec4(0.0);
	vec4 c = qexture(texture0, fragTexCoord);
	float white = min(c.r, min(c.g, c.b));
	float keep = 1.0 - smoothstep(0.82, 0.96, white);
	_fragColorOut = vec4(c.rgb, c.a * keep) * colDiffuse * vec4(1.0, 1.0, 1.0, float(opacity) / 255.0);
}
