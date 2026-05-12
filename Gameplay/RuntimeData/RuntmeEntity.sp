# HEADER
[0x0] Bit mask: {
    [0x00001] Is Bullet
    [0x00002] Group Child
    [0x00004] Group parent
    [0x00008] Use Create Script
    [0x00010] Use Remove Script
    [0x00020] Clear Protected
    [0x00040] Dangerous for player
    [0x10000] Is Collectable 
}
[0x1] Group Id
[0x2] Transparency
[0x10] Gamebox Position X
[0x11] Gamebox Position Y
[0x12] Source Position X
[0x13] Source Position Y
[0x14] Source Width
[0x15] Source Height
# if bullet
[0x0] Bit mask: {
    [0x080] Grazed
    [0x100] In Collectable State
}
[0x3] Color
[0x4] Collectable Score Modifier
# if entity
[0x0] Bit mask: {
    [0x080] Use Bad Drop Scenario
    [0x100] Drop when cleared
    [0x200] Is boss
    [0x400] Use Die Script
}
[0x3] Bad Drop Scenario
[0x4] Good Drop Scenario
# if boss
[0x5] Boss Id
Floating Points:
[0x0] Health
[0x1] Appear Speed
[0x2] Collision Scaling
[0x3] Rendering Scaling X
[0x4] Rendering Scaling Y
[0x5] Rendering Rotation
[0x6] In-Script Rotation
[0x7] Speed
[0x8] Speed Modifier