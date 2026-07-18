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
    [0x80000] Is Laser (straight beam; see laser fields below)
}
[0x1] Group Id
[0x2] Transparency
[0x3] Spawn Id
[0x13] Source Position X
[0x14] Source Position Y
[0x15] Source Width
[0x16] Source Height
[0x17] Appear Timestamp
# if collectable
[0x4] Type {
    0 - Power
    1 - Large Power
    2 - Full Power
    3 - Score
    4 - Heart
    5 - Heart Piece
    6 - Star
    7 - Star Piece
}
# if bullet
[0x0] Bit mask: {
    [0x080] Grazed
    [0x100] In Collectable State
    [0x200] Is Near To Player
    [0x400] Dangerous for Enemies
    [0x800] Is Used
}
[0x4] Color
[0x5] Collectable Score Modifier
# if entity
[0x0] Bit mask: {
    [0x080] Use Bad Drop Scenario
    [0x100] Drop when cleared
    [0x200] Is boss
    [0x400] Use Die Script
    [0x800] Is Died
}
[0x4] Bad Drop Scenario
[0x5] Good Drop Scenario
[0x6] Added score when killed
[0xA] Death Timestamp
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
[0x2] Collision Scaling / Collectable State Velocity X
[0x3] Rendering Scaling X
[0x4] Rendering Scaling Y
[0x5] Rendering Rotation
[0x6] In-Script Rotation / Collectable State Velocity Y
[0x7] Speed
[0x8] Speed Modifier
[0x9] Damage
[0xA] Max Health
[0x10] GameBox Position X
[0x11] GameBox Position Y
[0x12] GameBox Position Z
[0x13] Collision
[0x14] Target Move X
[0x15] Target Move Y
[0x16] Target-Move Speed
[0x17] Velocity X
[0x18] Velocity Y
[0x20] GameBox Position X Delta
[0x21] GameBox Position Y Delta
[0x22] GameBox Position Z Delta
[0x23] GameBox Rotation Delta
[0x50] Laser Length (Is Laser / Is Ray; emitter = Position 0x10/0x11, angle = Rendering Rotation 0x5)
[0x51] Laser Width (base width at the emitter)
[0x52] Ray Spread (Is Ray only) — extra half-width px added per side at the tip; cone widens base -> base+2*spread

# HEADER (Is Laser / Is Ray) — beam life in ticks, phases run telegraph -> fire -> fade from Created At
[0x50] Laser Telegraph Ticks
[0x51] Laser Fire Ticks
[0x52] Laser Fade Ticks
# Flag mask (Header[0]): 0x80000 Is Laser (parallel beam), 0x400000 Is Ray (widening gradient cone)