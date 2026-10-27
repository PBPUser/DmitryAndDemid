"""Rasterise a Drogichin OpenStreetMap extract into the data map the stage-1 flyover shader reads.

Usage (from the repo root):  python Tools/rasterize_drogichin.py <overpass-export.json>
The export is the Overpass API answer to the query in Tools/drogichin_overpass.ql (way["highway"|"building"|
"natural"="water"|"landuse"|"leisure"] inside the town bbox, `out geom;`). Map data (c) OpenStreetMap
contributors, ODbL — the generated texture inherits that attribution.

World frame: metres, origin at the square in front of the district executive committee (Райвыканкам),
x east, z north. The texture covers EXTENT x EXTENT metres; u = x/EXTENT + 0.5, v = 0.5 - z/EXTENT (top row is
north). Channels: R road (by class), G building, B ground cover (water 255, green 150, residential yard 60),
A always 255."""
import json, math, sys
from PIL import Image, ImageDraw

SRC = sys.argv[1] if len(sys.argv) > 1 else 'drogichin_osm.json'
OUT = 'Assets/Textures/drogichin_osm.png'
PREVIEW = 'drogichin_osm_preview.png'   # written next to the working directory, for a look
SIZE = 1024
EXTENT = 2400.0                      # metres covered by the texture, both axes
LAT0, LON0 = 52.185517, 25.160456    # the centre of the paved square east of the townhall (OSM pedestrian area)
M_PER_DEG_LAT = 111320.0
M_PER_DEG_LON = 111320.0 * math.cos(math.radians(LAT0))

ROAD = {'secondary': (255, 7.0), 'secondary_link': (255, 6.0), 'tertiary': (210, 6.0), 'tertiary_link': (210, 5.0),
        'residential': (170, 5.0), 'unclassified': (170, 5.0), 'pedestrian': (120, 0.0), 'service': (110, 3.0),
        'track': (90, 3.0), 'footway': (70, 1.6), 'path': (60, 1.6), 'steps': (60, 1.6)}


def to_px(lat, lon):
    x = (lon - LON0) * M_PER_DEG_LON
    z = (lat - LAT0) * M_PER_DEG_LAT
    return ((x / EXTENT + 0.5) * SIZE, (0.5 - z / EXTENT) * SIZE)


d = json.load(open(SRC, encoding='utf-8'))
els = d['elements']
img = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 255))
draw = ImageDraw.Draw(img)
px_per_m = SIZE / EXTENT

def poly(e):
    return [to_px(p['lat'], p['lon']) for p in e.get('geometry', [])]

# ground cover first (fills), then roads, then buildings on top
for e in els:
    t = e.get('tags', {}); pts = poly(e)
    if len(pts) < 3: continue
    if t.get('natural') == 'water':
        draw.polygon(pts, fill=(0, 0, 255, 255))
    elif t.get('landuse') in ('forest', 'grass', 'cemetery') or t.get('leisure') in ('park', 'pitch', 'stadium'):
        draw.polygon(pts, fill=(0, 0, 150, 255))
    elif t.get('landuse') == 'residential':
        draw.polygon(pts, fill=(0, 0, 60, 255))
for e in els:
    t = e.get('tags', {}); pts = poly(e)
    if 'highway' not in t or len(pts) < 2: continue
    val, width = ROAD.get(t['highway'], (0, 0))
    if val == 0: continue
    closed = len(pts) >= 4 and pts[0] == pts[-1]
    if t['highway'] == 'pedestrian' and (t.get('area') == 'yes' or closed):
        draw.polygon(pts, fill=(val, 0, 0, 255)); continue   # the paved square is a closed pedestrian way
    w = max(1, int(round(width * px_per_m)))
    draw.line(pts, fill=(val, 0, 0, 255), width=w, joint='curve')
for e in els:
    t = e.get('tags', {}); pts = poly(e)
    if 'building' not in t or len(pts) < 3: continue
    # G = storeys * 40 (1 -> 40 ... 6 -> 240): the shader extrudes a building to G/40 storeys. Untagged
    # houses count as one; the townhall is tagged 3, the blocks by the station 5.
    try:
        levels = float(t.get('building:levels', '1'))
    except ValueError:
        levels = 1.0
    g = int(max(40, min(255, round(levels * 40))))
    draw.polygon(pts, fill=(0, g, 0, 255))

img.save(OUT)
# a readable preview: roads white, buildings red, green/water tinted
pv = Image.new('RGB', (SIZE, SIZE), (20, 20, 20))
src = img.load(); dst = pv.load()
for y in range(SIZE):
    for x in range(SIZE):
        r, g, b, _ = src[x, y]
        if g: dst[x, y] = (200, 60, 40)
        elif r: dst[x, y] = (r, r, r)
        elif b == 255: dst[x, y] = (40, 90, 200)
        elif b: dst[x, y] = (30, 90 if b > 100 else 50, 30)
draw2 = ImageDraw.Draw(pv); draw2.ellipse((SIZE/2-4, SIZE/2-4, SIZE/2+4, SIZE/2+4), outline=(255, 255, 0))
pv.save(PREVIEW)

# the townhall and the square around it, in metres, to place the raymarched set pieces
for e in els:
    t = e.get('tags', {})
    if t.get('amenity') == 'townhall' or (t.get('highway') == 'pedestrian'):
        pts = [((p['lon'] - LON0) * M_PER_DEG_LON, (p['lat'] - LAT0) * M_PER_DEG_LAT) for p in e['geometry']]
        xs = [p[0] for p in pts]; zs = [p[1] for p in pts]
        print(t.get('name') or t.get('highway'), 'x %.0f..%.0f  z %.0f..%.0f' % (min(xs), max(xs), min(zs), max(zs)), 'pts', len(pts))
print('saved', OUT, 'and preview')
