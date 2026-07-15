#version 330

// Input vertex attributes
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

// Input uniform values
uniform mat4 mvp;
uniform vec2 screenSize;

// Output vertex attributes (to fragment shader)
out vec2 fragTexCoord;
out vec2 uv;
out vec4 fragColor;

// NOTE: Add here your custom variables

void main()
{
    // Send vertex attributes to fragment shader
    fragTexCoord = vertexTexCoord;
    //uv = vec2(vertexPosition[0], vertexPosition[1]) / screenSize;
    fragColor = vertexColor;
    uv = (mvp*vec4(vertexPosition,1.0)).xy;
    // Calculate final vertex position
    gl_Position = mvp*vec4(vertexPosition, 1.0);
}
