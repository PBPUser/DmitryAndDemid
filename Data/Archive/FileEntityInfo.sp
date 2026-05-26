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
[0x1] Group Id
[0x2] Transparency
[0x3] Spawn Id
# if bullet
[0x4] Color
[0x5] Collectable Score Modifier
# if entity
[0x0] Bit mask: {
    [0x80] Use Bad Drop Scenario
    [0x100] Drop when cleared
    [0x200] Is boss
    [0x400] Use Die Script
    [0x800] Override Visual Death Color
}
[0x4] Bad Drop Scenario
[0x5] Good Drop Scenario
[0x6] Added score when killed
[0xB] Rounds Die Color
[0xC] Particles Die Color
# if boss
[0x7] Boss Id
[0x8] Boss Health Bar Percent
[0x9] Boss Attack Index
[0x0] Bit mask: {
    [0x100000] Is Final
}
Floating Points:
[0x0] Health
[0x1] Appear Speed
[0x2] Scaling
# STRUCTURE
HEADER
FLOATING_POINTS
VISUAL USED
UPDATE SCRIPT
CREATE SCRIPT
REMOVE SCRIPT