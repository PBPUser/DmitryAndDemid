#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;

void main()
{
    vec2 uv = vec2(fragTexCoord.x, 1.-fragTexCoord.y);
    vec4 texelColor = texture2D(texture0, uv);
    gl_FragColor = texelColor * colDiffuse;
}
