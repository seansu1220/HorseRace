"""Award icons (256), medals (128), confetti atlas (512)."""
import math, random
from PIL import Image
from common import *

O = OUTLINE
SW = 4  # outline width at 256 scale

GOLD_DEFS = f"""
<radialGradient id="coin" cx="0.38" cy="0.35" r="0.75">
  <stop offset="0" stop-color="{GOLD_XL}"/><stop offset="0.5" stop-color="{GOLD}"/>
  <stop offset="1" stop-color="{GOLD_D}"/>
</radialGradient>
"""


def sparkle(cx, cy, r, col=GOLD_XL):
    return f'<path d="{star4(cx, cy, r, 0.2)}" fill="{col}" stroke="{O}" stroke-width="2" stroke-linejoin="round"/>'


def finish(body, name, defs="", size=256, glow=GOLD, fit=1.0, dx=0, dy=0):
    c = size / 2
    body = (f'<g transform="translate({c+dx} {c+dy}) scale({fit}) translate({-c} {-c})">{body}</g>')
    im = render(svg(size, size, body, GOLD_DEFS + defs), size, size, ss=4)
    print("   art bbox", im.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox())
    im = with_glow(im, glow, radius=size / 28, strength=0.5, spread=1)
    print(save(im, name), im.size, im.mode)
    return im


# ------------------------------------------------------------------ tickets
def ticket_path(w, h, r=11, rr=9):
    a, b = w / 2, h / 2
    return (f"M{-a+rr},{-b} H{a-rr} A{rr},{rr} 0 0 1 {a},{-b+rr} V{-r} A{r},{r} 0 0 0 {a},{r} "
            f"V{b-rr} A{rr},{rr} 0 0 1 {a-rr},{b} H{-a+rr} A{rr},{rr} 0 0 1 {-a},{b-rr} "
            f"V{r} A{r},{r} 0 0 0 {-a},{-r} V{-b+rr} A{rr},{rr} 0 0 1 {-a+rr},{-b} Z")


def ticket(x, y, rot, fill, trim, emblem, w=150, h=80):
    a, b = w / 2, h / 2
    return (f'<g transform="translate({x} {y}) rotate({rot})">'
            f'<path d="{ticket_path(w, h)}" fill="{fill}" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>'
            f'<rect x="{-a+13}" y="{-b+10}" width="{w-26}" height="{h-20}" rx="4" fill="none" '
            f'stroke="{trim}" stroke-width="2.5" opacity="0.75"/>'
            f'<line x1="{a-42}" y1="{-b+6}" x2="{a-42}" y2="{b-6}" stroke="{trim}" stroke-width="3" '
            f'stroke-dasharray="5 5" opacity="0.9"/>'
            f'<path d="{star5(-16, 0, 20)}" fill="{emblem}" stroke="{O}" stroke-width="2.5" stroke-linejoin="round"/>'
            f'<circle cx="{a-22}" cy="0" r="7" fill="{emblem}" stroke="{O}" stroke-width="2.5"/>'
            f'</g>')


def coin_side(cx, cy, rx=36, ry=12, t=10):
    ridges = "".join(
        f'<line x1="{cx + rx*math.cos(a):.1f}" y1="{cy + ry*math.sin(a):.1f}" '
        f'x2="{cx + rx*math.cos(a):.1f}" y2="{cy + ry*math.sin(a) + t:.1f}" stroke="{GOLD_XD}" stroke-width="2"/>'
        for a in [math.radians(d) for d in range(20, 170, 22)])
    return (f'<path d="M{cx-rx},{cy} V{cy+t} A{rx},{ry} 0 0 0 {cx+rx},{cy+t} V{cy} Z" '
            f'fill="{GOLD_D}" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>' + ridges +
            f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{GOLD_L}" stroke="{O}" stroke-width="{SW}"/>'
            f'<ellipse cx="{cx}" cy="{cy}" rx="{rx*0.68}" ry="{ry*0.62}" fill="none" stroke="{GOLD_D}" stroke-width="2.5"/>')


def coin_face(cx, cy, r):
    return (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#coin)" stroke="{O}" stroke-width="{SW}"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r*0.74}" fill="none" stroke="{GOLD_D}" stroke-width="3"/>'
            f'<path d="{star5(cx, cy+1, r*0.46)}" fill="{GOLD_D}"/>'
            f'<path d="{star5(cx-1, cy, r*0.46)}" fill="{GOLD_L}" opacity="0.9"/>'
            f'<path d="M{cx-r*0.62},{cy-r*0.3} A{r*0.7},{r*0.7} 0 0 1 {cx-r*0.1},{cy-r*0.68}" '
            f'stroke="#FFFFFF" stroke-width="3" fill="none" stroke-linecap="round" opacity="0.7"/>')


def award_ticket_tycoon():
    b = [
        ticket(104, 92, -26, GRN, CREAM, GOLD_L),
        ticket(112, 108, -12, BLUE, CREAM, GOLD_L),
        ticket(120, 126, 2, RED, CREAM, GOLD_L),
        ticket(126, 146, 14, CREAM, RED, GOLD),
    ]
    # coin stack (bottom-right) + one face-on coin (bottom-left)
    for i in range(4):
        b.append(coin_side(190, 214 - i * 12))
    b.append(coin_face(66, 206, 32))
    b += [sparkle(218, 70, 15), sparkle(34, 128, 9), sparkle(158, 36, 8)]
    return finish("".join(b), "award_ticket_tycoon.png", fit=0.95, dy=-3)


# ------------------------------------------------------------------ cheerleader
def pompom(cx, cy, R, c1, c2, seed, handle_dir=(0.35, 1)):
    rng = random.Random(seed)
    hx, hy = handle_dir
    L = math.hypot(hx, hy)
    hx, hy = hx / L, hy / L
    g = [f'<line x1="{cx}" y1="{cy}" x2="{cx + hx*R*1.05:.1f}" y2="{cy + hy*R*1.05:.1f}" '
         f'stroke="{O}" stroke-width="16" stroke-linecap="round"/>',
         f'<line x1="{cx}" y1="{cy}" x2="{cx + hx*R*1.05:.1f}" y2="{cy + hy*R*1.05:.1f}" '
         f'stroke="#3B2A14" stroke-width="9" stroke-linecap="round"/>']
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{R*0.8:.1f}" fill="{c1}" stroke="{O}" stroke-width="3"/>')
    for layer, (scale, cols) in enumerate([(1.0, (c1, c2)), (0.8, (c2, c1)), (0.55, (c1, c2)), (0.3, (GOLD_XL, c1))]):
        n = [44, 38, 26, 14][layer]
        for k in range(n):
            a = math.radians(k * 360 / n + rng.uniform(-4, 4) + layer * 5)
            l = R * scale * rng.uniform(0.9, 1.03)
            wdt = rng.uniform(5.5, 7.5) * (1 - layer * 0.12)
            tx, ty = cx + l * math.cos(a), cy + l * math.sin(a)
            nx, ny = -math.sin(a) * wdt, math.cos(a) * wdt
            mx, my = cx + l * 0.55 * math.cos(a), cy + l * 0.55 * math.sin(a)
            col = cols[k % 2]
            g.append(f'<path d="M{cx:.1f},{cy:.1f} Q{mx+nx:.1f},{my+ny:.1f} {tx:.1f},{ty:.1f} '
                     f'Q{mx-nx:.1f},{my-ny:.1f} {cx:.1f},{cy:.1f} Z" fill="{col}" '
                     f'stroke="{O}" stroke-width="1.6" stroke-linejoin="round"/>')
    return "".join(g)


def megaphone():
    def hh(x):  # cone half-height along x
        return 17 + (x + 70) / 128 * 37
    g = [
        # grip
        f'<rect x="-44" y="10" width="16" height="44" rx="6" fill="#2F3A35" stroke="{O}" stroke-width="{SW}"/>',
        f'<rect x="-40" y="16" width="5" height="30" rx="2" fill="#56655D"/>',
        # mouthpiece
        f'<rect x="-94" y="-13" width="28" height="26" rx="5" fill="{GOLD}" stroke="{O}" stroke-width="{SW}"/>',
        # cone body
        f'<path d="M-70,-17 L58,-54 L58,54 L-70,17 Z" fill="{RED}" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>',
        f'<polygon points="-10,{-hh(-10):.1f} 16,{-hh(16):.1f} 16,{hh(16):.1f} -10,{hh(-10):.1f}" fill="{CREAM}"/>',
        f'<polygon points="-66,-13 54,-48 54,-30 -66,-4" fill="#FFFFFF" opacity="0.22"/>',
        f'<polygon points="-66,10 54,34 54,50 -66,15" fill="{RED_D}" opacity="0.45"/>',
        f'<path d="M-70,-17 L58,-54 L58,54 L-70,17 Z" fill="none" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>',
        # bell rim + opening
        f'<ellipse cx="60" cy="0" rx="15" ry="58" fill="{GOLD}" stroke="{O}" stroke-width="{SW}"/>',
        f'<ellipse cx="63" cy="0" rx="9" ry="47" fill="#5A1519"/>',
        f'<ellipse cx="55" cy="-30" rx="3" ry="14" fill="{GOLD_XL}" opacity="0.8"/>',
    ]
    # sound waves
    for i, rr in enumerate((70, 92)):
        a = math.radians(36 - i * 6)
        x0, y0 = 26 + rr * math.cos(-a), rr * math.sin(-a)
        x1, y1 = 26 + rr * math.cos(a), rr * math.sin(a)
        for col, wd in ((O, 12), (GOLD_L, 6)):
            g.append(f'<path d="M{x0:.1f},{y0:.1f} A{rr},{rr} 0 0 1 {x1:.1f},{y1:.1f}" '
                     f'fill="none" stroke="{col}" stroke-width="{wd}" stroke-linecap="round"/>')
    return "".join(g)


def award_top_cheerleader():
    b = [pompom(58, 186, 50, GOLD, CREAM, 1, (0.5, 1)),
         pompom(200, 190, 46, RED, GOLD, 2, (-0.4, 1)),
         f'<g transform="translate(110 104) rotate(-22) scale(0.88)">{megaphone()}</g>',
         sparkle(34, 60, 11), sparkle(226, 26, 9)]
    return finish("".join(b), "award_top_cheerleader.png", fit=0.85, dx=-2, dy=-1)


# ------------------------------------------------------------------ roadblocker
def award_roadblocker():
    boards = [(26, 104, 204, 44), (26, 162, 204, 40)]
    def rr(x, y, w, h, r=8):
        return (f"M{x+r},{y} H{x+w-r} A{r},{r} 0 0 1 {x+w},{y+r} V{y+h-r} A{r},{r} 0 0 1 {x+w-r},{y+h} "
                f"H{x+r} A{r},{r} 0 0 1 {x},{y+h-r} V{y+r} A{r},{r} 0 0 1 {x+r},{y} Z")
    clip = '<path d="' + " ".join(rr(*bd) for bd in boards) + '"/>'
    defs = (f'<clipPath id="boards">{clip}</clipPath>'
            f'<radialGradient id="lamp"><stop offset="0" stop-color="{GOLD_XL}" stop-opacity="0.95"/>'
            f'<stop offset="0.4" stop-color="{GOLD}" stop-opacity="0.45"/>'
            f'<stop offset="1" stop-color="{GOLD}" stop-opacity="0"/></radialGradient>')
    b = []
    # lamp glows (behind)
    for x in (66, 190):
        b.append(f'<circle cx="{x}" cy="72" r="44" fill="url(#lamp)"/>')
    # posts + feet
    for x in (58, 182):
        b.append(f'<rect x="{x}" y="84" width="16" height="140" rx="3" fill="#6C7A80" stroke="{O}" stroke-width="{SW}"/>')
        b.append(f'<rect x="{x+3}" y="88" width="4" height="130" fill="#A5B1B6" opacity="0.7"/>')
        b.append(f'<path d="M{x-24},{234} L{x-16},{216} L{x+32},{216} L{x+40},{234} Z" '
                 f'fill="#2E3438" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>')
    # boards: cream base + red diagonal stripes, clipped
    stripes = "".join(f'<polygon points="{x0},96 {x0+22},96 {x0+22+110},206 {x0+110},206" fill="{RED}"/>'
                      for x0 in range(-120, 260, 44))
    b.append(f'<g clip-path="url(#boards)"><rect x="0" y="90" width="256" height="120" fill="#F7F1E3"/>'
             f'{stripes}'
             f'<rect x="0" y="138" width="256" height="10" fill="#000" opacity="0.14"/>'
             f'<rect x="0" y="193" width="256" height="9" fill="#000" opacity="0.14"/>'
             f'<rect x="0" y="104" width="256" height="7" fill="#FFF" opacity="0.25"/>'
             f'<rect x="0" y="162" width="256" height="6" fill="#FFF" opacity="0.25"/></g>')
    b.append("".join(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="none" '
                     f'stroke="{O}" stroke-width="{SW}"/>' for x, y, w, h in boards))
    for x in (66, 190):
        for y in (126, 182):
            b.append(f'<circle cx="{x}" cy="{y}" r="4.5" fill="#B9C3C8" stroke="{O}" stroke-width="2"/>')
    # warning lamps
    for x in (66, 190):
        b.append(f'<rect x="{x-13}" y="76" width="26" height="12" rx="3" fill="#2E3438" stroke="{O}" stroke-width="3"/>')
        b.append(f'<path d="M{x-15},77 A15,15 0 0 1 {x+15},77 Z" fill="#F5B82E" stroke="{O}" stroke-width="3" stroke-linejoin="round"/>')
        b.append(f'<ellipse cx="{x-5}" cy="69" rx="3.5" ry="5" fill="#FFF6D6"/>')
        for a in (-150, -115, -65, -30):
            r0, r1 = 22, 32
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            b.append(f'<line x1="{x+r0*ca:.1f}" y1="{74+r0*sa:.1f}" x2="{x+r1*ca:.1f}" y2="{74+r1*sa:.1f}" '
                     f'stroke="{GOLD_L}" stroke-width="4.5" stroke-linecap="round"/>')
    return finish("".join(b), "award_roadblocker.png", defs, glow=RED, dy=-4)


# ------------------------------------------------------------------ big winner
def award_big_winner():
    defs = (f'<radialGradient id="bag" cx="96" cy="150" r="150" gradientUnits="userSpaceOnUse">'
            f'<stop offset="0" stop-color="{GOLD_XL}"/><stop offset="0.45" stop-color="#EDC057"/>'
            f'<stop offset="1" stop-color="#A8791F"/></radialGradient>'
            f'<clipPath id="bagc"><path id="bagp" d="M104,98 C60,114 34,160 40,196 C46,232 86,242 128,242 '
            f'C170,242 210,232 216,196 C222,160 196,114 152,98 Z"/></clipPath>')
    body = ("M104,98 C60,114 34,160 40,196 C46,232 86,242 128,242 "
            "C170,242 210,232 216,196 C222,160 196,114 152,98 Z")
    tuft = ("M108,100 C94,84 80,66 88,50 C100,56 110,62 116,72 C117,58 122,46 130,38 "
            "C138,46 142,58 141,72 C148,62 160,54 172,50 C178,66 164,86 148,100 Z")
    b = [
        f'<path d="{tuft}" fill="#D9A73E" stroke="{O}" stroke-width="{SW}" stroke-linejoin="round"/>',
        f'<path d="M116,72 C118,80 120,90 120,98 M141,72 C139,82 137,90 136,98" stroke="{GOLD_XD}" stroke-width="2.5" fill="none"/>',
        f'<path d="{body}" fill="url(#bag)"/>',
        f'<g clip-path="url(#bagc)"><ellipse cx="196" cy="200" rx="70" ry="90" fill="{GOLD_XD}" opacity="0.35"/>'
        f'<ellipse cx="80" cy="150" rx="14" ry="34" transform="rotate(20 80 150)" fill="#FFFFFF" opacity="0.35"/></g>',
        f'<path d="{body}" fill="none" stroke="{O}" stroke-width="{SW+1}" stroke-linejoin="round"/>',
        # tie
        f'<rect x="96" y="88" width="64" height="18" rx="9" fill="{RED}" stroke="{O}" stroke-width="{SW}"/>',
        f'<rect x="102" y="91" width="50" height="4" rx="2" fill="#FFFFFF" opacity="0.3"/>',
        f'<path d="M154,98 C170,96 180,104 178,120 C170,114 162,108 154,104 Z" fill="{RED}" stroke="{O}" stroke-width="3" stroke-linejoin="round"/>',
        f'<path d="M156,100 C170,108 172,120 164,132" fill="none" stroke="{O}" stroke-width="7" stroke-linecap="round"/>',
        f'<path d="M156,100 C170,108 172,120 164,132" fill="none" stroke="{RED}" stroke-width="3.5" stroke-linecap="round"/>',
        # emblem
        f'<circle cx="128" cy="178" r="32" fill="{GOLD_L}" stroke="{O}" stroke-width="{SW}"/>',
        f'<circle cx="128" cy="178" r="24" fill="none" stroke="{GOLD_D}" stroke-width="3"/>',
        f'<path d="{star5(128, 180, 17)}" fill="{RED}" stroke="{O}" stroke-width="2.5" stroke-linejoin="round"/>',
    ]
    # coins at the base
    b.append(coin_side(46, 226, 26, 9, 8))
    b.append(coin_side(46, 216, 26, 9, 8))
    b.append(coin_face(214, 222, 22))
    b += [sparkle(40, 64, 15), sparkle(222, 70, 11), sparkle(212, 148, 7)]
    return finish("".join(b), "award_big_winner.png", defs, fit=0.93, dy=-8)


# ------------------------------------------------------------------ medals
def medal(name, metal, ribbon):
    ml, mm, md = metal
    rm, rs, rd = ribbon
    defs = (f'<radialGradient id="face" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="{ml}"/>'
            f'<stop offset="0.55" stop-color="{mm}"/><stop offset="1" stop-color="{md}"/></radialGradient>'
            f'<linearGradient id="rim" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{ml}"/>'
            f'<stop offset="0.5" stop-color="{mm}"/><stop offset="1" stop-color="{md}"/></linearGradient>')
    o, sw = O, 3

    def strap(tx, rot, dark):
        return (f'<g transform="translate({tx} -6) rotate({rot})">'
                f'<rect x="-14" y="0" width="28" height="78" fill="{rd if dark else rm}" stroke="{o}" stroke-width="{sw}"/>'
                f'<rect x="-4.5" y="0" width="9" height="78" fill="{rs}"/>'
                f'<rect x="-14" y="0" width="28" height="78" fill="none" stroke="{o}" stroke-width="{sw}"/></g>')
    cx, cy = 64, 84
    beads = "".join(
        f'<circle cx="{cx + 37*math.cos(math.radians(a)):.2f}" cy="{cy + 37*math.sin(math.radians(a)):.2f}" r="4.2" '
        f'fill="url(#rim)" stroke="{o}" stroke-width="2"/>' for a in range(0, 360, 15))
    b = [
        strap(88, 20, True), strap(40, -20, False),
        beads,
        f'<circle cx="{cx}" cy="{cy}" r="36" fill="url(#rim)" stroke="{o}" stroke-width="{sw}"/>',
        f'<circle cx="{cx}" cy="{cy}" r="29" fill="{md}" stroke="{o}" stroke-width="1.5"/>',
        f'<circle cx="{cx}" cy="{cy}" r="27" fill="url(#face)"/>',
        f'<path d="M{cx-24},{cy-10} A26,26 0 0 1 {cx+6},{cy-26}" stroke="#FFFFFF" stroke-width="3.5" '
        f'fill="none" stroke-linecap="round" opacity="0.65"/>',
        f'<path d="M{cx-33},{cy+8} A34,34 0 0 0 {cx+8},{cy+33}" stroke="#000000" stroke-width="3" '
        f'fill="none" stroke-linecap="round" opacity="0.18"/>',
        # hanger loop
        f'<rect x="{cx-9}" y="{cy-44}" width="18" height="10" rx="4" fill="url(#rim)" stroke="{o}" stroke-width="2.5"/>',
    ]
    im = render(svg(128, 128, "".join(b), defs), 128, 128, ss=6)
    im = with_glow(im, mm, radius=4, strength=0.45, spread=1)
    print(save(im, name), im.size, im.mode)
    return im


# ------------------------------------------------------------------ confetti atlas
CONF = [(GOLD, GOLD_D, GOLD_XL), (RED, RED_D, "#F07A74"), (BLUE, BLUE_D, "#7FB0EA"), (GRN, GRN_D, "#86D6A0")]


def conf_shape(k, c):
    f, d, l = c
    if k == 0:   # bent strip
        return (f'<polygon points="-36,-9 16,-12 18,9 -34,12" fill="{f}"/>'
                f'<polygon points="16,-12 36,-3 36,16 18,9" fill="{d}"/>'
                f'<polygon points="-36,-9 16,-12 16,-6 -35,-3" fill="{l}" opacity="0.5"/>')
    if k == 1:   # twisted curl
        pts = [(-42 + 84 * t, 16 * math.sin(t * 2 * math.pi * 1.2)) for t in [i / 60 for i in range(61)]]
        return twisted_ribbon(pts, 18, 0.09, 0.3, l, f, d, seam=0.6)
    if k == 2:   # tilted disc
        return (f'<ellipse cx="0" cy="3" rx="24" ry="14" fill="{d}"/>'
                f'<ellipse cx="0" cy="0" rx="24" ry="14" fill="{f}"/>'
                f'<ellipse cx="-7" cy="-4" rx="9" ry="4" fill="{l}" opacity="0.6"/>')
    if k == 3:   # star
        return (f'<path d="{star5(0, 2, 28)}" fill="{f}"/>'
                f'<path d="M0,-26 L0,2 L26.6,-6.6 Z" fill="{l}" opacity="0.45"/>'
                f'<path d="M0,2 L16.5,24.7 L10,4 Z" fill="{d}" opacity="0.6"/>')
    if k == 4:   # folded triangle
        return (f'<polygon points="0,-28 26,18 -26,18" fill="{f}"/>'
                f'<polygon points="0,-28 26,18 6,6" fill="{d}"/>')
    if k == 5:   # diamond with shaded half
        return (f'<polygon points="0,-26 22,0 0,26 -22,0" fill="{f}"/>'
                f'<polygon points="0,-26 22,0 0,26" fill="{d}"/>'
                f'<polygon points="0,-26 -22,0 -8,-2" fill="{l}" opacity="0.5"/>')
    if k == 6:   # spiral
        pts = []
        for i in range(90):
            th = i / 89 * 3.1 * math.pi
            r = 5 + 3.3 * th
            pts.append((r * math.cos(th), r * math.sin(th) * 0.8))
        p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
        return (f'<polyline points="{p}" fill="none" stroke="{d}" stroke-width="10" stroke-linecap="round" '
                f'stroke-linejoin="round" transform="translate(1.5 2)"/>'
                f'<polyline points="{p}" fill="none" stroke="{f}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>')
    if k == 7:   # long wavy strip
        pts = [(-46 + 92 * t, 9 * math.sin(t * 2 * math.pi * 2)) for t in [i / 80 for i in range(81)]]
        return twisted_ribbon(pts, 13, 0.05, 1.2, l, f, d, seam=0.6)


def confetti():
    rng = random.Random(11)
    cells = []
    order_first = list(range(8))
    order_second = [1, 0, 3, 2, 5, 4, 7, 6]
    shapes = order_first + order_second
    colors = [(i + i // 4) % 4 for i in range(16)]
    # sanity: each shape appears twice with different colours; each colour 4 times
    for s in range(8):
        cs = [colors[i] for i in range(16) if shapes[i] == s]
        assert len(cs) == 2 and cs[0] != cs[1], (s, cs)
    for i in range(16):
        r, c = divmod(i, 4)
        cx, cy = c * 128 + 64, r * 128 + 64
        rot = rng.uniform(-60, 60)
        cells.append(f'<g transform="translate({cx} {cy}) rotate({rot:.0f}) scale(1.15)">{conf_shape(shapes[i], CONF[colors[i]])}</g>')
    im = render(svg(512, 512, "".join(cells)), 512, 512, ss=3)
    # confirm nothing crosses cell borders
    a = im.split()[3].load()
    for k in range(1, 4):
        for t in range(512):
            assert a[k * 128, t] == 0 and a[t, k * 128] == 0 and a[k*128-1, t] == 0 and a[t, k*128-1] == 0, ("bleed", k, t)
    print(save(im, "confetti.png"), im.size, im.mode,
          "shapes", shapes, "colors", ["gold red blue green".split()[x] for x in colors])


if __name__ == "__main__":
    award_ticket_tycoon()
    award_top_cheerleader()
    award_roadblocker()
    award_big_winner()
    medal("medal_1.png", (GOLD_XL, GOLD, "#9C6E1C"), (RED, GOLD_L, RED_D))
    medal("medal_2.png", ("#F7F9FB", "#C3CAD0", "#7C858D"), (BLUE, CREAM, BLUE_D))
    medal("medal_3.png", ("#F6C79C", "#C98147", "#7E4A22"), (GRN, CREAM, GRN_D))
    confetti()
