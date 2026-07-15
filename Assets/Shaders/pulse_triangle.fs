#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;   // seconds; drives the pulse
uniform float dir;    // 0 = points right, 1 = left, 2 = up, 3 = down

// Every literal is a float on purpose — some drivers reject int literals promoted into float contexts.

void main(){
    // Re-map the fragment into a local space where the triangle always points toward +x (base at x=0, apex at
    // x=1, symmetric about y=0.5), so the shape maths is written once and the orientation is just a uniform.
    vec2 p;
    if(dir < 0.5)      p = fragTexCoord;                                  // right
    else if(dir < 1.5) p = vec2(1.0 - fragTexCoord.x, fragTexCoord.y);    // left
    else if(dir < 2.5) p = vec2(1.0 - fragTexCoord.y, fragTexCoord.x);    // up
    else               p = vec2(fragTexCoord.y, fragTexCoord.x);          // down

    float dy = abs(p.y - 0.5);
    float halfWidth = 0.5 * (1.0 - p.x);   // the triangle's half-height at this x
    float edge = halfWidth - dy;           // > 0 inside; also the distance to the two slanted edges
    if(p.x < 0.0 || p.x > 1.0 || edge < 0.0)
        discard;

    float pulse = 0.65 + 0.35 * sin(time * 6.28318);   // ~1 Hz brightness pulse, 0.30 .. 1.0

    // Black outline: a constant-thickness band just inside the slanted edges and the base (x = 0).
    float thickness = 0.08;
    bool outline = edge < thickness || p.x < thickness;

    // Red gradient: deeper red at the base, brighter toward the apex, with a slight centre-to-edge falloff.
    vec3 red = mix(vec3(0.45, 0.02, 0.02), vec3(1.0, 0.18, 0.12), p.x);
    red *= 1.0 - 0.35 * (dy / 0.5);

    vec3 col   = outline ? vec3(0.0) : red * (0.6 + 0.4 * pulse);
    float alpha = outline ? 1.0 : (0.70 + 0.30 * pulse);
    gl_FragColor = vec4(col, alpha) * colDiffuse;
}
