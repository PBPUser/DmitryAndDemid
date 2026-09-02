#version 330

// The complaints box of Dmitry's fourth stage-3 card — a Soviet "книга жалоб" wall box, baked once at startup
// into the "GrievanceBox" texture (Helper.RenderGrievanceBox) and drawn as a plain entity sprite from there.
// A dark-red bevelled frame around a red plate, a slot along the top to post a grievance through, and four
// brass screws. The label is text, laid over this by the bake rather than drawn here.

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 resolution;   // the bake's pixel size (160x80)

float roundedBox(vec2 p, vec2 halfSize, float r)
{
    vec2 q = abs(p) - halfSize + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void main()
{
    vec2 p = (fragTexCoord - 0.5) * resolution;    // pixels, origin at the centre, +y down

    float outer = roundedBox(p, resolution * 0.5 - 1.0, 9.0);
    float body = 1.0 - smoothstep(-1.0, 0.6, outer);
    float inner = roundedBox(p, resolution * 0.5 - 8.0, 5.0);
    float plate = 1.0 - smoothstep(-1.0, 0.6, inner);

    // Bevel: the frame catches light on its upper-left run and falls dark on the lower-right.
    float light = clamp(0.5 - (p.x + p.y * 1.4) / (resolution.x + resolution.y), 0.0, 1.0);
    vec3 frame = vec3(0.40, 0.05, 0.05) * (0.65 + 0.75 * light);
    vec3 red = vec3(0.70, 0.10, 0.09) * (0.88 + 0.28 * light);
    // A faint brushed grain on the plate so it is not a flat fill.
    red *= 0.96 + 0.04 * sin(p.y * 2.1 + sin(p.x * 0.35) * 2.0);
    vec3 col = mix(frame, red, plate);

    // The slot, a dark bar near the top with a lit lip under it.
    float slot = roundedBox(p - vec2(0.0, -24.0), vec2(54.0, 3.5), 3.0);
    float slotMask = 1.0 - smoothstep(-0.5, 0.8, slot);
    float lip = (1.0 - smoothstep(0.0, 2.0, slot - 1.0)) * (1.0 - slotMask) * step(0.0, p.y + 24.0);
    col = mix(col, vec3(0.07, 0.015, 0.015), slotMask);
    col += vec3(0.30, 0.10, 0.08) * lip;

    // Brass screws in the frame's corners.
    vec2 corner = abs(p) - (resolution * 0.5 - 6.0);
    float screw = 1.0 - smoothstep(2.2, 3.0, length(corner));
    float screwSlot = 1.0 - smoothstep(0.4, 0.9, abs(corner.x + corner.y) / 1.4142) ;
    vec3 brass = vec3(0.78, 0.62, 0.22) * (0.7 + 0.5 * light);
    col = mix(col, brass * (1.0 - 0.5 * screwSlot * screw), screw);

    gl_FragColor = vec4(col, body) * colDiffuse;
}
