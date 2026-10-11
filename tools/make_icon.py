"""Draws the 256x256 Thunderstore icon: a carrot over three furrows of hoed soil.

Thunderstore rejects anything that is not exactly 256x256 PNG, and there is no art asset to crop
from, so the icon is drawn - same approach and background as the other Cartur icons.

    python tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "package", "icon.png")

SIZE = 256
SCALE = 4  # drawn large and downsampled, so the curves are not stair-stepped

BACKGROUND = (38, 34, 30, 255)
SOIL = (92, 64, 42, 255)
SOIL_EDGE = (58, 40, 26, 255)
CARROT = (232, 122, 38, 255)
CARROT_EDGE = (140, 64, 18, 255)
RIDGE = (190, 92, 28, 255)
LEAF = (96, 160, 64, 255)
LEAF_EDGE = (44, 84, 30, 255)

s = SCALE
image = Image.new("RGBA", (SIZE * s, SIZE * s), BACKGROUND)
draw = ImageDraw.Draw(image)

# Three furrows across the bottom, each a rounded mound.
for i, y in enumerate((178, 204, 230)):
    inset = 18 + i * 4
    draw.rounded_rectangle([(inset * s, (y - 13) * s), ((SIZE - inset) * s, (y + 9) * s)],
                           radius=11 * s, fill=SOIL, outline=SOIL_EDGE, width=4 * s)

# Leaves: three blades fanning up from the carrot's shoulder.
for dx, top, lean in ((-30, 22, -22), (0, 10, 0), (30, 22, 22)):
    base = (128 * s, 92 * s)
    tip = ((128 + dx + lean) * s, top * s)
    w = 15 * s
    draw.polygon([(base[0] - w, base[1]), tip, (base[0] + w, base[1])],
                 fill=LEAF, outline=LEAF_EDGE, width=4 * s)

# The carrot: a long taper, point down into the soil, slightly turned.
body = [(86 * s, 86 * s), (170 * s, 86 * s), (138 * s, 196 * s), (126 * s, 206 * s), (116 * s, 196 * s)]
draw.polygon(body, fill=CARROT, outline=CARROT_EDGE, width=5 * s)
draw.rounded_rectangle([(84 * s, 78 * s), (172 * s, 100 * s)], radius=11 * s, fill=CARROT, outline=CARROT_EDGE, width=5 * s)
# Ridges across the root.
for y, half in ((120, 30), (146, 21), (170, 12)):
    draw.line([((128 - half) * s, y * s), ((128 + half - 8) * s, (y - 3) * s)], fill=RIDGE, width=5 * s)

image = image.resize((SIZE, SIZE), Image.LANCZOS)
image.save(OUT)
print("wrote", OUT, image.size)
