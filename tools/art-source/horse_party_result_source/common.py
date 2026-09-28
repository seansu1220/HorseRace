"""Shared helpers: palette, SVG rendering, glow/shadow compositing."""
import io, math
import cairosvg
import numpy as np
from PIL import Image, ImageFilter, ImageChops

OUT_DIR = "/home/claude/party_assets/out"

# ---- palette -------------------------------------------------------------
BG = "#0E1813"
GREEN_1 = "#13251B"
GREEN_2 = "#1B3326"
GOLD = "#E4B64A"
GOLD_L = "#F6D77E"
GOLD_XL = "#FBE9B0"
GOLD_D = "#B8862C"
GOLD_XD = "#8A6118"
CREAM = "#F3EAD3"
CREAM_D = "#D8CBA6"
RED = "#D23A3A"
RED_D = "#8E1F26"
BLUE = "#3C7FD0"
BLUE_D = "#22508E"
GRN = "#45B06A"
GRN_D = "#27744A"
OUTLINE = "#23180A"


def svg(w, h, body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}"><defs>{defs}</defs>{body}</svg>')


def render(svg_str, w, h, ss=1):
    """Render SVG to RGBA PIL image at w*ss x h*ss, then downsample to w x h."""
    png = cairosvg.svg2png(bytestring=svg_str.encode("utf-8"),
                           output_width=w * ss, output_height=h * ss)
    im = Image.open(io.BytesIO(png)).convert("RGBA")
    if ss != 1:
        im = im.resize((w, h), Image.LANCZOS)
    return im


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def glow_from_alpha(im, color, radius, strength=1.0, spread=0):
    """Soft colored glow built from an image's alpha channel."""
    a = im.split()[3]
    if spread:
        a = a.filter(ImageFilter.MaxFilter(spread * 2 + 1))
    a = a.filter(ImageFilter.GaussianBlur(radius))
    a = a.point(lambda v: min(255, int(v * strength)))
    g = Image.new("RGBA", im.size, hex_rgb(color) + (0,))
    g.putalpha(a)
    return g


def with_glow(im, color=GOLD, radius=8, strength=0.55, spread=2,
              shadow=True):
    """Icon finishing: soft drop shadow + gold halo behind the artwork."""
    base = Image.new("RGBA", im.size, (0, 0, 0, 0))
    if shadow:
        sh = glow_from_alpha(im, "#000000", radius * 0.6, 0.55, spread=1)
        off = Image.new("RGBA", im.size, (0, 0, 0, 0))
        off.paste(sh, (0, max(2, im.size[1] // 64)))
        base = Image.alpha_composite(base, off)
    base = Image.alpha_composite(base, glow_from_alpha(im, color, radius, strength, spread))
    return Image.alpha_composite(base, im)


# ---- shape helpers --------------------------------------------------------
def star5(cx, cy, r, inner=0.45, rot=-90):
    pts = []
    for i in range(10):
        rr = r if i % 2 == 0 else r * inner
        a = math.radians(rot + i * 36)
        pts.append(f"{cx + rr * math.cos(a):.2f},{cy + rr * math.sin(a):.2f}")
    return "M" + " L".join(pts) + " Z"


def star4(cx, cy, r, pinch=0.18):
    """Four-point sparkle."""
    p = r * pinch
    return (f"M{cx},{cy - r} Q{cx + p},{cy - p} {cx + r},{cy} "
            f"Q{cx + p},{cy + p} {cx},{cy + r} Q{cx - p},{cy + p} {cx - r},{cy} "
            f"Q{cx - p},{cy - p} {cx},{cy - r} Z")


def twisted_ribbon(pts, width, twist_freq, phase, front_l, front_d, back,
                   seam=0.8):
    """Draw a twisting paper ribbon along a centerline (list of (x,y)).
    Apparent width follows |cos| of the twist; colour flips front/back."""
    n = len(pts)
    out = []
    # normals
    norms = []
    for i in range(n):
        x0, y0 = pts[max(0, i - 1)]
        x1, y1 = pts[min(n - 1, i + 1)]
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy) or 1
        norms.append((-dy / L, dx / L))
    # cumulative length
    s = [0.0]
    for i in range(1, n):
        s.append(s[-1] + math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]))
    def hw(i):
        c = math.cos(s[i] * twist_freq + phase)
        return width / 2 * max(0.12, abs(c)), c
    fl, fd, bk = hex_rgb(front_l), hex_rgb(front_d), hex_rgb(back)
    for i in range(n - 1):
        w0, c0 = hw(i)
        w1, c1 = hw(i + 1)
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        (nx0, ny0), (nx1, ny1) = norms[i], norms[i + 1]
        c = (c0 + c1) / 2
        if c >= 0:
            t = min(1, abs(c))
            col = tuple(int(fd[k] + (fl[k] - fd[k]) * t) for k in range(3))
        else:
            t = min(1, abs(c))
            col = tuple(int(bk[k] * (0.75 + 0.25 * t)) for k in range(3))
        hexc = "#%02x%02x%02x" % col
        poly = (f"{ax + nx0 * w0:.2f},{ay + ny0 * w0:.2f} {bx + nx1 * w1:.2f},{by + ny1 * w1:.2f} "
                f"{bx - nx1 * w1:.2f},{by - ny1 * w1:.2f} {ax - nx0 * w0:.2f},{ay - ny0 * w0:.2f}")
        out.append(f'<polygon points="{poly}" fill="{hexc}" stroke="{hexc}" '
                   f'stroke-width="{seam}" stroke-linejoin="round"/>')
    return "".join(out)


def save(im, name):
    path = f"{OUT_DIR}/{name}"
    im.save(path, optimize=True)
    return path
