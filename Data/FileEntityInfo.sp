# HEADER
[0x0] Bit mask: {
    [0x01] Is Bullet
    [0x02] Group Child
    [0x04] Group parent
    [0x08] Use Create Script
    [0x10] Use Remove Script
    [0x20] Clear Protected
    [0x40] Dangerous for player
}
[0x1] Create Script Index
[0x2] Update Script Index
[0x3] Remove Script Index
[0x4] Scaling
[0x5] Group Id
[0x6] Transparency
# if bullet
[0x7] Color
# if entity
[0x0] Bit mask: {
    [0x80] Use Bad Drop Scenario
    [0x100] Drop when cleared
    [0x200] Is boss
    [0x400] Use Die Script
}
[0x7] Bad Drop Scenario
[0x8] Good Drop Scenario
[0x9] Health
[0xA] Appear Speed
[0xB] Die Script Index
# if boss
[0xC] Boss Id
# STRUCTURE
HEADER
VISUAL USED