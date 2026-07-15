#version 330
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform vec2 res;
uniform float alpha;

// Cheap full-saturation rainbow from a hue in [0,1).
vec3 rainbow(float h){
	return clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
}

void main(){
	vec4 tex = texture(texture0, fragTexCoord);
	vec2 texel = vec2(1.0) / res;
	if (tex.a > 0.5){
		// The star body: a rainbow that scrolls across it over time.
		float h = fract(fragTexCoord.x * 0.6 + fragTexCoord.y * 0.4 + time * 0.35);
		gl_FragColor = vec4(rainbow(h), alpha);
	} else {
		// Outside the star: a semi-transparent black outline wherever a nearby texel is solid.
		float m = 0.0;
		for (float x = -8.0; x <= 8.0; x += 1.0)
		for (float y = -8.0; y <= 8.0; y += 1.0)
			m = max(m, texture(texture0, fragTexCoord + vec2(x, y) * texel).a);
		gl_FragColor = vec4(0.0, 0.0, 0.0, (m > 0.5 ? 0.5 : 0.0) * alpha);
	}
}
