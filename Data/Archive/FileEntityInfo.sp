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
# if bullet
[0x3] Color
[0x4] Collectable Score Modifier
# if entity
[0x0] Bit mask: {
    [0x80] Use Bad Drop Scenario
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
[0x2] Scaling
# STRUCTURE
HEADER
FLOATING_POINTS
VISUAL USED
UPDATE SCRIPT
CREATE SCRIPT
REMOVE SCRIPT