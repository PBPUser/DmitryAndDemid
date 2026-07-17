#version 330

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

// Sample the bullet's own sub-rect on the shared atlas (everything outside is empty).
vec4 qexture(sampler2D sampler, vec2 pos){
    vec2 uvs = pos * output_resolution;
	if(uvs.x > position.x && uvs.y > position.y && uvs.x < position.x + resolution.x && uvs.y < position.y + resolution.y)
		return texture(sampler, pos);
	return vec4(0);
}

void main()
{
	vec4 base = qexture(texture0, fragTexCoord);

	// Local 0..1 coordinates inside the circle sprite.
	vec2 luv = (fragTexCoord * output_resolution - position) / resolution;

	// Dark stripes across the disc whose position wobbles ("shakes") over time.
	float t = float(time) * 0.15;
	float wob = sin(luv.x * 12.0 + t) * 0.06 + sin(luv.x * 5.0 - t * 1.3) * 0.03;
	float tri = abs(fract((luv.y + wob) * 5.0) - 0.5) * 2.0; // triangle wave: 0 at a line, 1 between
	float lineMask = smoothstep(0.30, 0.05, tri);

	vec3 col = base.rgb * mix(1.0, 0.15, lineMask);
	gl_FragColor = vec4(col, base.a) * colDiffuse;
}
