"""result_bg.png — 1920x1080 night racecourse, opaque."""
import math, random
import numpy as np
from PIL import Image, ImageFilter
from common import *

W, H = 1920, 1080
rng = random.Random(7)

# centre 60% safe zone (kept clean)
SX0, SX1, SY0, SY1 = 384, 1536, 216, 864


def in_safe(x, y, pad=0):
    return SX0 - pad < x < SX1 + pad and SY0 - pad < y < SY1 + pad


# ---------------------------------------------------------------- base layer
def ellipse_pt(cx, cy, rx, ry, t):
    return cx + rx * math.cos(t), cy + ry * math.sin(t)


TCX, TCY = 960, 1560           # track ellipse centre (below the frame)
ORX, ORY = 1655, 705           # outer rail
IRX, IRY = 1495, 555           # inner rail

defs = f"""
<radialGradient id="sky" cx="960" cy="470" r="1250" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="#1A3224"/>
  <stop offset="0.45" stop-color="#11211A"/>
  <stop offset="1" stop-color="#060C09"/>
</radialGradient>
<linearGradient id="dirt" x1="0" y1="850" x2="0" y2="1080" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="#2E2A1E"/>
  <stop offset="1" stop-color="#1B1912"/>
</linearGradient>
<linearGradient id="grass" x1="0" y1="740" x2="0" y2="1080" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="#11211A"/>
  <stop offset="1" stop-color="#16291E"/>
</linearGradient>
<linearGradient id="stand" x1="0" y1="520" x2="0" y2="860" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="#0B140F"/>
  <stop offset="1" stop-color="#08100C"/>
</linearGradient>
<clipPath id="infield"><ellipse cx="{TCX}" cy="{TCY}" rx="{IRX}" ry="{IRY}"/></clipPath>
"""

b = [f'<rect width="{W}" height="{H}" fill="url(#sky)"/>']

# stars (top band, faint)
for _ in range(170):
    x, y = rng.uniform(0, W), rng.uniform(0, 560)
    r = rng.choice([0.8, 1.0, 1.2, 1.6])
    op = rng.uniform(0.15, 0.55) * (0.5 if in_safe(x, y, 40) else 1)
    b.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{CREAM}" opacity="{op:.2f}"/>')

# distant tree / hill line
b.append('<path d="M0,742 C220,712 420,730 640,716 C860,702 1060,722 1280,710 '
         'C1500,700 1700,724 1920,712 L1920,900 L0,900 Z" fill="#0D1913"/>')
# grass behind the track
b.append(f'<rect x="0" y="752" width="{W}" height="{H-752}" fill="url(#grass)"/>')
# far rail (very faint)
b.append('<path d="M0,790 Q960,772 1920,790" stroke="#F3EAD3" stroke-width="2" '
         'fill="none" opacity="0.14"/>')


# grandstands (left + mirrored right)
def grandstand(mirror):
    pts_roof = [(0, 500), (440, 612), (440, 630), (0, 548)]
    pts_body = [(0, 548), (420, 626), (420, 790), (0, 812)]
    def tr(p):
        return [(W - x if mirror else x, y) for x, y in p]
    g = []
    g.append('<polygon points="%s" fill="url(#stand)"/>' %
             " ".join(f"{x},{y}" for x, y in tr(pts_body)))
    g.append('<polygon points="%s" fill="#0C1712" stroke="#1E3528" stroke-width="2"/>' %
             " ".join(f"{x},{y}" for x, y in tr(pts_roof)))
    # roof edge glint
    (x0, y0), (x1, y1) = tr([(0, 548), (440, 630)])
    g.append(f'<line x1="{x0}" y1="{y0}" x2="{x1}" y2="{y1}" stroke="{GOLD}" '
             f'stroke-width="2" opacity="0.35"/>')
    # columns
    for cx in (60, 170, 280, 390):
        yt = 548 + cx / 420 * 78
        (xa, ya), (xb, yb) = tr([(cx, yt), (cx, 800 - cx / 420 * 10)])
        g.append(f'<line x1="{xa}" y1="{ya}" x2="{xb}" y2="{yb}" stroke="#050A07" stroke-width="7"/>')
    # seat tiers with crowd lights
    for k in range(7):
        yy0 = 600 + k * 27
        for _ in range(26):
            x = rng.uniform(8, 410)
            y = yy0 + x / 420 * 55 + rng.uniform(-4, 4)
            if y > 800:
                continue
            (xx, yy), = tr([(x, y)])
            col = rng.choice([GOLD, GOLD_L, CREAM, "#F0A860"])
            g.append(f'<circle cx="{xx:.1f}" cy="{yy:.1f}" r="{rng.choice([1.4,1.8,2.2])}" '
                     f'fill="{col}" opacity="{rng.uniform(0.25,0.65):.2f}"/>')
        (xa, ya), (xb, yb) = tr([(0, yy0 + 14), (420, yy0 + 14 + 55)])
        g.append(f'<line x1="{xa}" y1="{ya}" x2="{xb}" y2="{yb}" stroke="#1A2E22" '
                 f'stroke-width="2" opacity="0.8"/>')
    return "".join(g)


b.append(grandstand(False))
b.append(grandstand(True))


# floodlight towers
def tower(x):
    TY = 262
    g = [f'<line x1="{x}" y1="{TY+60}" x2="{x}" y2="620" stroke="#07100B" stroke-width="12"/>',
         f'<line x1="{x-18}" y1="620" x2="{x}" y2="{TY+180}" stroke="#07100B" stroke-width="5"/>',
         f'<line x1="{x+18}" y1="620" x2="{x}" y2="{TY+180}" stroke="#07100B" stroke-width="5"/>',
         f'<rect x="{x-62}" y="{TY-8}" width="124" height="74" rx="6" fill="#07100B" '
         f'stroke="#2A4232" stroke-width="2"/>']
    for r in range(3):
        for c in range(5):
            g.append(f'<circle cx="{x-44+c*22}" cy="{TY+10+r*20}" r="7.5" fill="#FFF6DC"/>')
    return "".join(g)


b.append(tower(86))
b.append(tower(W - 86))

# track band (between rails) + infield mowing stripes
b.append(f'<path fill-rule="evenodd" fill="url(#dirt)" d="'
         f'M{TCX-ORX},{TCY} A{ORX},{ORY} 0 1 1 {TCX+ORX},{TCY} A{ORX},{ORY} 0 1 1 {TCX-ORX},{TCY} Z '
         f'M{TCX-IRX},{TCY} A{IRX},{IRY} 0 1 0 {TCX+IRX},{TCY} A{IRX},{IRY} 0 1 0 {TCX-IRX},{TCY} Z"/>')
stripes = []
for i in range(-12, 13):
    x = 960 + i * 150
    stripes.append(f'<polygon points="{x},900 {x+75},900 {x+75+i*40},1100 {x+i*40},1100" '
                   f'fill="#1A3024" opacity="0.8"/>')
b.append(f'<g clip-path="url(#infield)"><rect x="0" y="900" width="{W}" height="200" fill="#14271C"/>'
         + "".join(stripes) + '</g>')
# hoof-track texture on dirt
for _ in range(260):
    t = rng.uniform(math.pi * 1.05, math.pi * 1.95)
    f = rng.uniform(0, 1)
    rx = IRX + (ORX - IRX) * f
    ry = IRY + (ORY - IRY) * f
    x, y = ellipse_pt(TCX, TCY, rx, ry, t)
    if y > H:
        continue
    b.append(f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{rng.uniform(2,6):.1f}" ry="{rng.uniform(1,2.2):.1f}" '
             f'fill="#3A3426" opacity="{rng.uniform(0.3,0.7):.2f}"/>')


def rail(rx, ry, lift, width, op, post_step):
    g = [f'<ellipse cx="{TCX}" cy="{TCY-lift}" rx="{rx}" ry="{ry}" fill="none" '
         f'stroke="{CREAM}" stroke-width="{width}" opacity="{op}"/>']
    t = math.pi
    while t < 2 * math.pi:
        x, y = ellipse_pt(TCX, TCY - lift, rx, ry, t)
        if 0 <= x <= W and y < H:
            g.append(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{x:.1f}" y2="{y+lift:.1f}" '
                     f'stroke="{CREAM}" stroke-width="{width*0.8:.1f}" opacity="{op*0.85:.2f}"/>')
        t += post_step
    return "".join(g)


b.append(rail(ORX, ORY, 22, 5, 0.85, 0.035))
b.append(rail(IRX, IRY, 18, 4, 0.55, 0.04))

base = render(svg(W, H, "".join(b), defs), W, H).convert("RGBA")


# ---------------------------------------------------------------- glow layers
def layer(body, blur, defs_=""):
    im = render(svg(W, H, body, defs_), W, H)
    if blur:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    return im


# spotlight cone from top-centre
base = Image.alpha_composite(base, layer(
    f'<polygon points="880,-60 1040,-60 1470,930 450,930" fill="#FFE3A0" opacity="0.075"/>', 55))
base = Image.alpha_composite(base, layer(
    f'<polygon points="920,-60 1000,-60 1260,930 660,930" fill="#FFEBBE" opacity="0.06"/>', 35))
# diagonal floodlight beams toward centre
base = Image.alpha_composite(base, layer(
    f'<polygon points="40,280 130,262 1010,860 760,900" fill="#FFF1CC" opacity="0.035"/>'
    f'<polygon points="{W-40},280 {W-130},262 {W-1010},860 {W-760},900" fill="#FFF1CC" opacity="0.035"/>', 40))
# central soft light
base = Image.alpha_composite(base, layer(
    '<circle cx="960" cy="500" r="700" fill="url(#cg)"/>', 0,
    '<radialGradient id="cg"><stop offset="0" stop-color="#F6DC9A" stop-opacity="0.14"/>'
    '<stop offset="0.55" stop-color="#E4B64A" stop-opacity="0.045"/>'
    '<stop offset="1" stop-color="#E4B64A" stop-opacity="0"/></radialGradient>'))
# pool of light on the track
base = Image.alpha_composite(base, layer(
    '<ellipse cx="960" cy="905" rx="600" ry="85" fill="#F6DFA0" opacity="0.28"/>'
    '<ellipse cx="960" cy="905" rx="320" ry="45" fill="#FFF3D0" opacity="0.25"/>', 30))
# floodlight lamp glow
lamp_glow = ""
for x in (86, W - 86):
    lamp_glow += f'<rect x="{x-70}" y="248" width="140" height="86" rx="30" fill="#FFF1C8" opacity="0.9"/>'
base = Image.alpha_composite(base, layer(lamp_glow, 28))
base = Image.alpha_composite(base, layer(lamp_glow.replace("0.9", "0.45"), 70))


# ---------------------------------------------------------------- string lights
def catenary(x0, y0, x1, y1, sag, n):
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t + sag * 4 * t * (1 - t)))
    return pts


strings = [catenary(-40, 18, 760, -8, 150, 200),
           catenary(1160, -8, 1960, 18, 150, 200),
           catenary(640, -24, 1280, -24, 78, 160)]

wire, bulbs, bulb_glow = [], [], []
for pts in strings:
    wire.append('<polyline points="%s" fill="none" stroke="#040806" stroke-width="3"/>' %
                " ".join(f"{x:.1f},{y:.1f}" for x, y in pts))
    for i in range(4, len(pts), 11):
        x, y = pts[i]
        if y < 4:
            continue
        col = [GOLD_L, "#FFF3D6", GOLD][i // 11 % 3]
        bulbs.append(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{x:.1f}" y2="{y+8:.1f}" stroke="#040806" stroke-width="3"/>'
                     f'<ellipse cx="{x:.1f}" cy="{y+15:.1f}" rx="6.5" ry="8.5" fill="{col}"/>'
                     f'<ellipse cx="{x-2:.1f}" cy="{y+12:.1f}" rx="2" ry="3" fill="#FFFFFF" opacity="0.8"/>')
        bulb_glow.append(f'<circle cx="{x:.1f}" cy="{y+15:.1f}" r="20" fill="{GOLD_L}" opacity="0.55"/>')

base = Image.alpha_composite(base, layer("".join(bulb_glow), 12))

# ---------------------------------------------------------------- bokeh
bok_soft, bok_sharp = [], []
n = 0
while n < 70:
    x, y = rng.uniform(0, W), rng.uniform(0, H * 0.75)
    if in_safe(x, y, 60):
        continue
    r = rng.uniform(6, 34)
    col = rng.choice([GOLD, GOLD, GOLD_L, "#F2A65A"])
    op = rng.uniform(0.08, 0.3)
    (bok_soft if r > 14 else bok_sharp).append(
        f'<circle cx="{x:.0f}" cy="{y:.0f}" r="{r:.1f}" fill="{col}" opacity="{op:.2f}"/>')
    n += 1
base = Image.alpha_composite(base, layer("".join(bok_soft), 6))
base = Image.alpha_composite(base, layer("".join(bok_sharp), 2.5))

# ---------------------------------------------------------------- streamers + confetti (sharp)
top = list(wire) + list(bulbs)
for (sx, length, amp, ph, side) in [
        (22, 110, 8, 0.0, 0), (190, 270, 16, 1.3, 0), (292, 200, 13, 2.1, 0), (392, 120, 9, 0.6, 0),
        (W - 22, 110, 8, 0.7, 1), (W - 190, 270, 16, 2.0, 1), (W - 292, 195, 13, 0.3, 1), (W - 392, 115, 9, 1.4, 1)]:
    pts_s = strings[side]
    # hang from nearest string point
    anchor = min(pts_s, key=lambda p: abs(p[0] - sx))
    ax, ay = anchor
    cl = []
    for i in range(90):
        t = i / 89
        cl.append((ax + amp * math.sin(t * 7.5 + ph) * (0.3 + t), ay + 4 + length * t))
    top.append(twisted_ribbon(cl, 16, 0.075, ph, GOLD_L, GOLD_D, GOLD_XD))

# gold swag ribbons across the upper corners
for side in (0, 1):
    pts_s = strings[side]
    cl = [(x, y + 26 + 10 * math.sin(i / 14)) for i, (x, y) in enumerate(pts_s[::2])]
    top.append(twisted_ribbon(cl, 10, 0.05, side * 1.1, GOLD, GOLD_XD, "#6E4C12"))

conf_cols = [(GOLD, GOLD_D)] * 4 + [(RED, RED_D), (BLUE, BLUE_D), (GRN, GRN_D), (CREAM, CREAM_D)]
n = 0
while n < 60:
    x, y = rng.uniform(10, W - 10), rng.uniform(40, H * 0.72)
    if in_safe(x, y, 70):
        continue
    c1, c2 = rng.choice(conf_cols)
    w_, h_ = rng.uniform(8, 16), rng.uniform(4, 8)
    rot = rng.uniform(0, 180)
    top.append(f'<g transform="translate({x:.0f} {y:.0f}) rotate({rot:.0f})">'
               f'<rect x="{-w_/2:.1f}" y="{-h_/2:.1f}" width="{w_:.1f}" height="{h_:.1f}" fill="{c1}"/>'
               f'<rect x="{w_/6:.1f}" y="{-h_/2:.1f}" width="{w_/3:.1f}" height="{h_:.1f}" fill="{c2}"/></g>')
    n += 1

base = Image.alpha_composite(base, layer("".join(top), 0))

# ---------------------------------------------------------------- vignette + grain
arr = np.asarray(base.convert("RGB")).astype(np.float32)
yy, xx = np.mgrid[0:H, 0:W]
d = np.sqrt(((xx - 960) / 1100) ** 2 + ((yy - 520) / 760) ** 2)
vig = np.clip(1 - 0.5 * np.clip(d - 0.55, 0, None) ** 1.4, 0.45, 1)[..., None]
arr *= vig
nrs = np.random.default_rng(3)
arr += nrs.normal(0, 2.6, (H, W, 1))
arr = np.clip(arr, 0, 255).astype(np.uint8)
out = Image.fromarray(arr, "RGB")
print(save(out, "result_bg.png"), out.size, out.mode)
