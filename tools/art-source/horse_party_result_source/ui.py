"""panel_frame.png (1024, 9-slice border 96) and ribbon.png (1200x220)."""
from PIL import Image
from common import *

# ============================================================ panel_frame
S = 1024
B = 96  # nine-slice border — every corner decoration stays inside 96x96


def notch_rect(i, r):
    """Rect inset i with concave (ticket-notch) corners of radius r."""
    a, z = i, S - i
    return (f"M{a+r},{a} H{z-r} A{r},{r} 0 0 0 {z},{a+r} V{z-r} "
            f"A{r},{r} 0 0 0 {z-r},{z} H{a+r} A{r},{r} 0 0 0 {a},{z-r} "
            f"V{a+r} A{r},{r} 0 0 0 {a+r},{a} Z")


def corner(tx, ty, sx, sy):
    """Corner ornament drawn for the top-left corner, mirrored into place."""
    g = [
        # sparkle in the ticket notch
        f'<path d="{star4(24, 24, 15, 0.2)}" fill="{GOLD_L}"/>',
        f'<circle cx="24" cy="24" r="3" fill="{GOLD_XL}"/>',
        # inner flourish (diamond + two ticks + dots) inside the inner line corner
        f'<path d="M62,50 L72,62 L62,74 L52,62 Z" fill="{GOLD}"/>',
        f'<path d="M62,55 L67,62 L62,69 L57,62 Z" fill="{GOLD_XL}"/>',
        f'<line x1="77" y1="62" x2="90" y2="62" stroke="{GOLD}" stroke-width="2.5" stroke-linecap="round"/>',
        f'<line x1="62" y1="77" x2="62" y2="90" stroke="{GOLD}" stroke-width="2.5" stroke-linecap="round"/>',
        f'<circle cx="84" cy="50" r="2.4" fill="{GOLD}" opacity="0.8"/>',
        f'<circle cx="50" cy="84" r="2.4" fill="{GOLD}" opacity="0.8"/>',
    ]
    return f'<g transform="translate({tx} {ty}) scale({sx} {sy})">{"".join(g)}</g>'


panel = [
    # body
    f'<rect x="3" y="3" width="{S-6}" height="{S-6}" rx="40" fill="{GREEN_1}"/>',
    # thin dark bevel just inside the outer edge
    f'<rect x="7" y="7" width="{S-14}" height="{S-14}" rx="36" fill="none" stroke="#0A140F" stroke-width="3"/>',
    # outer gold line (ticket notches at the corners)
    f'<path d="{notch_rect(22, 22)}" fill="none" stroke="{GOLD}" stroke-width="6"/>',
    # inner gold line
    f'<rect x="40" y="40" width="{S-80}" height="{S-80}" rx="10" fill="none" '
    f'stroke="{GOLD}" stroke-width="2.5" stroke-opacity="0.9"/>',
    # faint cream ticket-paper line
    f'<rect x="54" y="54" width="{S-108}" height="{S-108}" rx="4" fill="none" '
    f'stroke="{CREAM}" stroke-width="1.5" stroke-opacity="0.16"/>',
    corner(0, 0, 1, 1), corner(S, 0, -1, 1), corner(0, S, 1, -1), corner(S, S, -1, -1),
]
im = render(svg(S, S, "".join(panel)), S, S, ss=2)
print(save(im, "panel_frame.png"), im.size, im.mode)

# ============================================================ ribbon
RW, RH = 1200, 220
X1, X2, Y1, Y2 = 172, 1028, 30, 162   # front band
O, L, N = 42, 158, 44                  # tail drop, tail length, V-notch depth

rdefs = f"""
<linearGradient id="band" x1="0" y1="{Y1}" x2="0" y2="{Y2}" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="{GOLD_XL}"/>
  <stop offset="0.18" stop-color="{GOLD_L}"/>
  <stop offset="0.55" stop-color="{GOLD}"/>
  <stop offset="1" stop-color="#C39232"/>
</linearGradient>
<linearGradient id="tail" x1="0" y1="{Y1+O}" x2="0" y2="{Y2+O}" gradientUnits="userSpaceOnUse">
  <stop offset="0" stop-color="#D7A841"/>
  <stop offset="1" stop-color="#9C6E1C"/>
</linearGradient>
"""


def tail(mirror):
    def m(x):
        return RW - x if mirror else x
    xa, xb = X1 - L, X1
    ym = (Y1 + Y2) / 2 + O
    pts = [(xa, Y1 + O), (xb, Y1 + O), (xb, Y2 + O), (xa, Y2 + O), (xa + N, ym)]
    poly = " ".join(f"{m(x)},{y}" for x, y in pts)
    fold = " ".join(f"{m(x)},{y}" for x, y in [(X1, Y2), (X1 + O, Y2), (X1, Y2 + O)])
    stitch_top = f'M{m(xa+12)},{Y1+O+11} L{m(xb-4)},{Y1+O+11}'
    stitch_bot = f'M{m(xa+12)},{Y2+O-11} L{m(xb-4)},{Y2+O-11}'
    return (f'<polygon points="{poly}" fill="url(#tail)" stroke="{GOLD_XD}" stroke-width="3" stroke-linejoin="round"/>'
            f'<path d="{stitch_top} {stitch_bot}" stroke="{CREAM}" stroke-width="2" '
            f'stroke-dasharray="9 7" opacity="0.45"/>'
            f'<polygon points="{fold}" fill="#5E410E" stroke="{GOLD_XD}" stroke-width="3" stroke-linejoin="round"/>')


rb = [tail(False), tail(True),
      f'<rect x="{X1}" y="{Y1}" width="{X2-X1}" height="{Y2-Y1}" fill="url(#band)" '
      f'stroke="{GOLD_XD}" stroke-width="3"/>',
      # top sheen and bottom shade
      f'<rect x="{X1+2}" y="{Y1+2}" width="{X2-X1-4}" height="14" fill="#FFFFFF" opacity="0.18"/>',
      f'<rect x="{X1+2}" y="{Y2-16}" width="{X2-X1-4}" height="14" fill="#7A5716" opacity="0.18"/>',
      # darker inner rules + cream stitching
      f'<line x1="{X1}" y1="{Y1+9}" x2="{X2}" y2="{Y1+9}" stroke="{GOLD_D}" stroke-width="2"/>',
      f'<line x1="{X1}" y1="{Y2-9}" x2="{X2}" y2="{Y2-9}" stroke="{GOLD_D}" stroke-width="2"/>',
      f'<path d="M{X1+8},{Y1+17} H{X2-8} M{X1+8},{Y2-17} H{X2-8}" stroke="{CREAM}" '
      f'stroke-width="2" stroke-dasharray="10 8" opacity="0.6"/>',
      # small end ornaments (outside the centre text area)
      f'<path d="{star4(X1+34, (Y1+Y2)/2, 16, 0.2)}" fill="{GOLD_XD}" opacity="0.75"/>',
      f'<path d="{star4(X2-34, (Y1+Y2)/2, 16, 0.2)}" fill="{GOLD_XD}" opacity="0.75"/>',
      ]
rim = render(svg(RW, RH, "".join(rb), rdefs), RW, RH, ss=2)
# soft drop shadow underneath
sh = glow_from_alpha(rim, "#000000", 7, 0.55)
shadow = Image.new("RGBA", rim.size, (0, 0, 0, 0))
shadow.paste(sh, (0, 7))
rim = Image.alpha_composite(shadow, rim)
print(save(rim, "ribbon.png"), rim.size, rim.mode)
