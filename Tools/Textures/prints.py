"""Prints: patterns printed or woven onto a garment's main fabric, in the pattern colour (decision 0008's catalog
"patterns"). Each function takes the tile size in pixels and a seed and returns (colour, ink), floats in [0, 1]:
``colour`` is how much of the pattern colour shows (0.5 a half blend with the cloth, for a second tone), ``ink`` how
dark the print's drawn lines and shading are. Some return a third, ``accent``: how much of the print's second colour
shows (a flower's leaves, a daisy's heart, a cone's wafer), which follows the first by the rule in ACCENT. Tiles
repeat seamlessly; the catalog sets how many per metre.

PRINTS lists each print with its catalog entry: name, style tags, tiles per metre (min, max) and weight."""

import numpy as np

import texture_kit as tk


def _band(t, centre, width, soft=0.01):
    """1 inside a band of ``width`` round ``centre`` on a repeating coordinate ``t`` in [0, 1)."""
    d = np.abs(((t - centre + 0.5) % 1.0) - 0.5)
    return np.clip((width / 2 - d) / soft + 0.5, 0, 1)


def _outline(mask, width=1):
    """The edge of a mask: where it differs from its blur (a drawn outline)."""
    return np.clip(np.abs(mask - tk.blur_small(mask, width)) * 4.0, 0, 1)


def _zero(size):
    return np.zeros((size, size))


def _scatter_shapes(size, shapes, count, seed, gap, rotate=True, scale=(1.0, 1.0)):
    """Shapes (list of (colour, ink) small arrays, or (colour, ink, accent) with parts in the second colour) stamped at
    spread-out points, rotated at random. Returns (colour, ink), and the accent too if a shape has one; then each
    shape covers what it lands on (a flower over a leaf)."""
    colour, ink, accent = _zero(size), _zero(size), _zero(size)
    accented = any(len(shape) > 2 for shape in shapes)
    rng = np.random.default_rng(seed)
    for i, (cx, cy) in enumerate(tk.scatter(size, count, seed, gap)):
        c, k, a = (list(shapes[i % len(shapes)]) + [None])[:3]
        a = np.zeros_like(c) if a is None else a
        angle = rng.random() * 360 if rotate else 0.0
        if rotate:
            c, k, a = tk.rotate_shape(c, angle), tk.rotate_shape(k, angle), tk.rotate_shape(a, angle)
        if accented:
            cover = np.clip(np.maximum(c, a), 0, 1)
            _stamp_over(colour, c, cover, cx, cy)
            _stamp_over(accent, a, cover, cx, cy)
        else:
            tk.stamp(colour, c, cx, cy, mode="max")
        tk.stamp(ink, k, cx, cy, mode="max")
    return (colour, ink, accent) if accented else (colour, ink)


def _stamp_over(layer, shape, cover, cx, cy):
    """Stamps ``shape`` onto ``layer`` at (cx, cy), wrapping, replacing what's under its ``cover``."""
    size = layer.shape[0]
    h, w = shape.shape
    ys = (np.arange(h) + int(round(cy)) - h // 2) % size
    xs = (np.arange(w) + int(round(cx)) - w // 2) % size
    layer[np.ix_(ys, xs)] = layer[np.ix_(ys, xs)] * (1 - cover) + shape


def _shape(size_px, fn):
    """A small square array of ``size_px`` with fn(x, y) in [-1, 1] coordinates."""
    r = size_px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    return np.clip(fn(x, y), 0, 1)


def _soft(d, px):
    """A signed distance (inside negative), in shape units, as anti-aliased coverage."""
    return np.clip(0.5 - d * px, 0, 1)


# -------------------------------------------------------------------------------------------- stripes and checks

def stripes(size, seed):
    x, y = tk.grid(size)
    return _band(y, 0.25, 0.5), _zero(size)


def breton(size, seed):
    x, y = tk.grid(size)
    return _band(y * 2, 0.5, 0.28), _zero(size)


def pinstripe(size, seed):
    x, y = tk.grid(size)
    return _band(x * 2, 0.5, 0.05), _zero(size)


def chalk_stripe(size, seed):
    x, y = tk.grid(size)
    chalk = _band(x, 0.5, 0.04, 0.015) * (0.8 + 0.2 * tk.value_noise(size, 30, seed, 1))
    return chalk, _zero(size)


def awning(size, seed):
    x, y = tk.grid(size)
    return np.maximum(_band(x, 0.25, 0.5), 0.45 * _band(x, 0.75, 0.12)), _zero(size)


def candy_stripe(size, seed):
    x, y = tk.grid(size)
    return np.maximum(_band(x * 2, 0.25, 0.22), 0.5 * _band(x * 2, 0.72, 0.08)), _zero(size)


def diagonal_stripes(size, seed):
    x, y = tk.grid(size)
    return _band((x + y) % 1.0, 0.25, 0.4), _zero(size)


def gingham(size, seed):
    x, y = tk.grid(size)
    a, b = _band(x * 2, 0.25, 0.5, 0.004), _band(y * 2, 0.25, 0.5, 0.004)
    return np.clip(0.5 * a + 0.5 * b, 0, 1), _zero(size)


def check(size, seed):
    x, y = tk.grid(size)
    return ((np.floor(x * 2) + np.floor(y * 2)) % 2 == 0).astype(float), _zero(size)


def buffalo_check(size, seed):
    x, y = tk.grid(size)
    a, b = _band(x, 0.25, 0.5, 0.003), _band(y, 0.25, 0.5, 0.003)
    colour = np.clip(0.45 * a + 0.45 * b, 0, 1)
    ink = a * b * 0.6
    return colour, ink


def tartan(size, seed):
    x, y = tk.grid(size)
    wide = 0.4 * _band(x, 0.25, 0.32) + 0.4 * _band(y, 0.25, 0.32)
    mid = 0.25 * (_band(x, 0.72, 0.12) + _band(y, 0.72, 0.12))
    lines = np.maximum(_band(x, 0.62, 0.02, 0.004), _band(y, 0.62, 0.02, 0.004))
    over = np.clip(_band(x, 0.88, 0.03, 0.004) + _band(y, 0.88, 0.03, 0.004), 0, 1)
    twill = 0.5 + 0.5 * np.sin(2 * np.pi * 96 * (x * 0.5 + y))
    colour = np.clip(wide + mid, 0, 1) * (1 - over)
    ink = np.clip(0.55 * lines + 0.25 * wide * wide * twill, 0, 1)
    return colour, ink, over


def windowpane(size, seed):
    x, y = tk.grid(size)
    return np.maximum(_band(x, 0.5, 0.03, 0.004), _band(y, 0.5, 0.03, 0.004)), _zero(size)


def houndstooth(size, seed):
    x, y = tk.grid(size)
    n = 2
    u, v = (x * n) % 1.0, (y * n) % 1.0
    cell = (u < 0.5) & (v < 0.5)
    tooth = ((u >= 0.5) & (v < 0.5) & (u - 0.5 < v)) | ((u < 0.5) & (v >= 0.5) & (v - 0.5 < u))
    step = ((u >= 0.5) & (v >= 0.5) & (np.abs(u - v) < 0.18))
    return (cell | tooth | step).astype(float), _zero(size)


def argyle(size, seed):
    x, y = tk.grid(size)
    diamond = (np.abs(x - 0.5) + np.abs(y - 0.5) < 0.5).astype(float)
    lines = np.maximum(_band((x + y) % 1.0, 0.5, 0.03, 0.004), _band((x - y) % 1.0, 0.5, 0.03, 0.004))
    return np.clip(diamond * 0.9, 0, 1) * (1 - lines), lines * 0.2, lines


def harlequin(size, seed):
    x, y = tk.grid(size)
    u, v = (x * 2) % 1.0, (y * 1) % 1.0
    diamond = np.abs(u - 0.5) * 2 + np.abs(v - 0.5) * 2 < 1.0
    return diamond.astype(float), _outline(diamond.astype(float)) * 0.5


def chevron(size, seed):
    x, y = tk.grid(size)
    return _band((y * 2 + np.abs((x * 2) % 1.0 - 0.5)) % 1.0, 0.5, 0.5, 0.008), _zero(size)


def zigzag(size, seed):
    x, y = tk.grid(size)
    t = (y * 3 + 0.6 * np.abs((x * 4) % 1.0 - 0.5)) % 1.0
    return _band(t, 0.5, 0.18, 0.01), _zero(size)


def triangles(size, seed):
    x, y = tk.grid(size)
    n = 4
    u, v = (x * n) % 1.0, (y * n) % 1.0
    up = (v > 1 - 2 * np.abs(u - 0.5)).astype(float)
    row = np.floor(y * n) % 2
    colour = np.where(row == 0, up, 0.5 * up)
    return colour, _outline(up) * 0.4


def hexagons(size, seed):
    """Honeycomb: hexagonal cells with drawn edges (4 x 7 cells, so the tile repeats, the cells 1% squat)."""
    x, y = tk.grid(size)
    cols, rows = 4, 7
    width, height = cols * 3.0, rows * np.sqrt(3)
    u, v = x * width, y * height
    best = np.full((size, size), 99.0)
    second = np.full((size, size), 99.0)
    for i in range(cols):
        for j in range(rows):
            for cx, cy in ((i * 3.0, j * np.sqrt(3)), (i * 3.0 + 1.5, j * np.sqrt(3) + np.sqrt(3) / 2)):
                du = (u - cx + width / 2) % width - width / 2
                dv = (v - cy + height / 2) % height - height / 2
                d = np.hypot(du, dv)
                closer = d < best
                second = np.where(closer, best, np.minimum(second, d))
                best = np.where(closer, d, best)
    edges = 1 - tk.smoothstep(0.08, 0.2, second - best)
    return np.clip(0.35 * (1 - edges) + edges, 0, 1), edges * 0.3


def waves(size, seed):
    x, y = tk.grid(size)
    t = (y * 4 + 0.08 * np.sin(2 * np.pi * x * 2)) % 1.0
    return _band(t, 0.5, 0.3, 0.012), _zero(size)


def scales(size, seed):
    """Mermaid and fish scales: overlapping arcs, each scale shaded towards its rim."""
    x, y = tk.grid(size)
    n = 6
    u = x * n
    v = y * n
    row = np.floor(v)
    u2 = u + np.where(row % 2 == 0, 0.0, 0.5)
    cu, cv = u2 % 1.0 - 0.5, v % 1.0
    d = np.hypot(cu, cv * 1.1)
    scale = (d < 0.62).astype(float)
    rim = tk.smoothstep(0.38, 0.6, d) * scale
    return 0.35 + 0.65 * rim, np.clip(_outline(scale, 1) * 0.6, 0, 1)


def lattice(size, seed):
    x, y = tk.grid(size)
    lines = np.maximum(_band((x + y) % 1.0 * 2 % 1.0, 0.5, 0.06, 0.006), _band((x - y) % 1.0 * 2 % 1.0, 0.5, 0.06, 0.006))
    return lines, _outline(lines) * 0.4


def circles(size, seed):
    x, y = tk.grid(size)
    u, v = (x * 2) % 1.0 - 0.5, (y * 2) % 1.0 - 0.5
    r = np.hypot(u, v)
    rings = _band((r * 5) % 1.0, 0.5, 0.45, 0.04) * (r < 0.48)
    return rings, _zero(size)


# ---------------------------------------------------------------------------------------------------------- dots

def dots(size, seed):
    x, y = tk.grid(size)
    u, v = (x * 2) % 1.0 - 0.5, (y * 2) % 1.0 - 0.5
    d = np.hypot(u, v)
    offset = np.hypot(((x * 2 + 0.5) % 1.0) - 0.5, ((y * 2 + 0.5) % 1.0) - 0.5)
    return np.maximum(_soft(d - 0.18, 60), _soft(offset - 0.18, 60)), _zero(size)


def big_dots(size, seed):
    x, y = tk.grid(size)
    d = np.hypot(x - 0.5, y - 0.5)
    return _soft(d - 0.3, 80), _zero(size)


def confetti(size, seed):
    """Confetti: dots and tumbling paper slips in two colours."""
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(8):
        if k % 2:
            px = int(size / 14) | 1
            h = px // 2
            yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float) / max(h, 1)
            piece = ((np.abs(xx) < 0.9) & (np.abs(yy) < 0.32)).astype(float)
        else:
            piece = tk.disc(size / 40 * (0.8 + 0.6 * rng.random()), 1.0)
        none = np.zeros_like(piece)
        shapes.append((piece, none, none) if k % 4 < 2 else (none, none, piece))
    return _scatter_shapes(size, shapes, 46, seed, size / 10)


# ------------------------------------------------------------------------------------------------- florals, botanicals

def _petal_flower(px, petals, roundness, centre_ink=True):
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float)
    a = np.arctan2(y, x)
    rad = np.hypot(x, y) / max(r, 1)
    edge = roundness + (1 - roundness) * np.abs(np.cos(petals * a / 2))
    body = np.clip((edge - rad) * r / 1.2 + 0.5, 0, 1)
    centre = np.clip((0.2 - rad) * r / 1.2 + 0.5, 0, 1)
    veins = np.exp(-(np.sin(petals * a / 2 + np.pi / 2) / 0.12) ** 2) * (rad > 0.25) * body * 0.5
    ink = np.clip(_outline(body, 1) + (centre if centre_ink else 0) + veins, 0, 1)
    return body, ink


def _leaf_shape(px, vein=True):
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    width = 0.42 * np.sqrt(np.clip(1 - y * y, 0, 1)) * (1 - 0.3 * y)
    body = np.clip((width - np.abs(x)) * r / 1.2 + 0.5, 0, 1)
    ink = _outline(body, 1)
    if vein:
        ink = np.maximum(ink, np.exp(-(x / 0.04) ** 2) * body * 0.8)
        for k in (-0.5, -0.1, 0.3):
            ink = np.maximum(ink, np.exp(-((y - k - np.abs(x) * 0.8) / 0.04) ** 2) * body * 0.5)
    return body, np.clip(ink, 0, 1)


def ditsy_floral(size, seed):
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(4):
        b, i = _petal_flower(int(size / 9 * (0.8 + 0.4 * rng.random())), 5, 0.45)
        shapes.append((b, i))
    lb, li = _leaf_shape(int(size / 12))
    shapes.append((np.zeros_like(lb), li, lb))
    return _scatter_shapes(size, shapes, 26, seed, size / 7)


def big_floral(size, seed):
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(2):
        b, i = _petal_flower(int(size / 3.2), 6 + k, 0.3)
        b2, i2 = _petal_flower(int(size / 5.5), 6 + k, 0.4)
        pad = (b.shape[0] - b2.shape[0]) // 2
        inner = np.pad(b2, pad)[:b.shape[0], :b.shape[0]]
        inner_ink = np.pad(i2, pad)[:b.shape[0], :b.shape[0]]
        shapes.append((np.clip(b * 0.6 + inner * 0.4, 0, 1), np.clip(np.maximum(i, inner_ink), 0, 1)))
    lb, li = _leaf_shape(int(size / 4))
    shapes += [(np.zeros_like(lb), li, lb), (np.zeros_like(lb), li, lb)]
    return _scatter_shapes(size, shapes, 8, seed, size / 3.6)


def daisies(size, seed):
    b, i = _petal_flower(int(size / 5), 12, 0.15)
    r = b.shape[0] // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    centre = np.clip((r * 0.22 - np.hypot(x, y)) / 1.2 + 0.5, 0, 1)
    colour = np.clip(b - centre, 0, 1)
    ink = np.clip(i * 0.7 + centre * 0.15, 0, 1)
    return _scatter_shapes(size, [(colour, ink, centre)], 10, seed, size / 3.6)


def sakura(size, seed):
    b, i = _petal_flower(int(size / 6), 5, 0.65)
    r = b.shape[0] // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float)
    a = np.arctan2(y, x)
    notch = np.exp(-(np.cos(5 * a / 2) / 0.12) ** 2) * (np.hypot(x, y) > r * 0.7)
    petals = np.clip(b - notch, 0, 1)
    small = tk.disc(size / 60, 1.0)
    return _scatter_shapes(size, [(petals, i), (petals * 0.6, i), (small * 0.5, small * 0)], 18, seed, size / 6)


def hibiscus(size, seed):
    b, i = _petal_flower(int(size / 3.6), 5, 0.55)
    r = b.shape[0] // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float)
    stamen = np.exp(-(((x - y) / (r * 0.08)) ** 2)) * (x > 0) * (np.hypot(x, y) < r * 0.6)
    lb, li = _leaf_shape(int(size / 3.2))
    mb, mi = _monstera(int(size / 3))
    return _scatter_shapes(size, [(b, np.clip(i + stamen, 0, 1)), (np.zeros_like(lb), li, lb), (np.zeros_like(mb), mi, mb)], 7, seed, size / 3.6)


def _monstera(px):
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    body = (x * x + (y * 0.9) ** 2 < 0.8).astype(float)
    a = np.arctan2(y, x)
    slits = (np.abs(np.sin(a * 5)) < 0.12) & (np.hypot(x, y) > 0.45)
    body = np.where(slits, 0.0, body)
    body = tk.blur_small(body, 1)
    ink = np.clip(_outline(body) + np.exp(-(x / 0.03) ** 2) * body * 0.7, 0, 1)
    return body, ink


def tropical_leaves(size, seed):
    mb, mi = _monstera(int(size / 2.6))
    lb, li = _leaf_shape(int(size / 2.8))
    return _scatter_shapes(size, [(mb, mi), (lb, li), (mb * 0.55, mi)], 7, seed, size / 3.3)


def leaves(size, seed):
    lb, li = _leaf_shape(int(size / 5))
    return _scatter_shapes(size, [(lb, li), (lb * 0.55, li)], 18, seed, size / 6)


def vine(size, seed):
    x, y = tk.grid(size)
    colour, ink = _zero(size), _zero(size)
    t = x * 2 * np.pi
    stem_y = 0.5 + 0.12 * np.sin(t * 2)
    stem = _band(((y - stem_y) % 1.0), 0.0, 0.012, 0.004)
    ink = np.maximum(ink, stem * 0.8)
    lb, li = _leaf_shape(int(size / 9))
    for k in range(8):
        px = k / 8 + 0.03
        py = 0.5 + 0.12 * np.sin(px * 2 * np.pi * 2) + (0.04 if k % 2 else -0.04)
        angle = 40 if k % 2 else 140
        tk.stamp(colour, tk.rotate_shape(lb, angle), px * size, py * size)
        tk.stamp(ink, tk.rotate_shape(li, angle), px * size, py * size)
    fb, fi = _petal_flower(int(size / 10), 5, 0.5)
    for k in range(3):
        px = k / 3 + 0.17
        tk.stamp(colour, fb, px * size, (0.5 + 0.12 * np.sin(px * 4 * np.pi)) * size - size / 12)
        tk.stamp(ink, fi, px * size, (0.5 + 0.12 * np.sin(px * 4 * np.pi)) * size - size / 12)
    return colour, ink


def _paisley_shape(px):
    """A paisley (boteh): a round head tapering into a curled tail, with an inner outline, a dotted border and a
    little flower at its heart."""
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    # The head: a circle low in the frame; the tail: the region between two offset circles curling up to the right.
    head = np.hypot(x + 0.05, y - 0.25) - 0.55
    outer = np.hypot(x - 0.35, y + 0.05) - 0.85
    inner = np.hypot(x - 0.62, y + 0.15) - 0.62
    tail = np.maximum(outer, -inner)
    tail = np.where(y < 0.25, tail, 1.0)
    body = np.minimum(head, tail)
    shape = _soft(body, r)
    inset = _soft(body + 0.14, r)
    border = np.clip(_soft(np.abs(body + 0.07) - 0.025, r) * (np.sin(np.arctan2(y - 0.25, x + 0.05) * 18) > 0.2), 0, 1)
    heart_flower = _soft(np.hypot(x + 0.05, y - 0.25) - (0.18 + 0.06 * np.cos(6 * np.arctan2(y - 0.25, x + 0.05))), r)
    colour = np.clip(shape * 0.55 + inset * 0.45 - heart_flower * 0.6, 0, 1)
    ink = np.clip(_outline(shape, 1) + _outline(inset, 1) * 0.8 + border * 0.7 + _outline(heart_flower, 1), 0, 1)
    return colour, ink


def paisley(size, seed):
    big = _paisley_shape(int(size / 2.6))
    small = _paisley_shape(int(size / 4))
    return _scatter_shapes(size, [big, small, small], 9, seed, size / 3.6)


def hearts(size, seed):
    def heart(px):
        return _shape(px, lambda x, y: _soft(((x * 1.2) ** 2 + (-y * 1.2 + 0.25) ** 2 - 1) ** 3 - (x * 1.2) ** 2 * (-y * 1.2 + 0.25) ** 3, 8))
    h = heart(int(size / 5))
    return _scatter_shapes(size, [(h, _outline(h) * 0.3), (heart(int(size / 8)) * 0.6, _zero(int(size / 8) // 2 * 2 + 1))], 14, seed, size / 5, rotate=False)


def stars(size, seed):
    def star(px, points=5):
        def fn(x, y):
            a = np.arctan2(y, x) + np.pi / 2
            r = np.hypot(x, y)
            sector = 2 * np.pi / points
            phi = np.abs(((a % sector) / sector) - 0.5) * 2
            edge = 0.4 + 0.55 * phi ** 1.3
            return _soft(r - edge, px / 2)
        return _shape(px, fn)
    s1 = star(int(size / 5))
    s2 = star(int(size / 9))
    return _scatter_shapes(size, [(s1, _outline(s1) * 0.3), (s2 * 0.6, _zero(s2.shape[0]))], 14, seed, size / 5)


def moons_and_stars(size, seed):
    moon = _shape(int(size / 4.5), lambda x, y: np.minimum(_soft(np.hypot(x, y) - 0.8, 20), 1 - _soft(np.hypot(x - 0.35, y - 0.2) - 0.7, 20)))
    st = stars(size, seed)[0]
    colour, ink = _scatter_shapes(size, [(moon, _outline(moon) * 0.4)], 5, seed + 3, size / 2.5)
    return np.maximum(colour, st * 0.6), ink


def clouds(size, seed):
    def cloud(px):
        def fn(x, y):
            parts = [np.hypot(x + 0.4, y - 0.15) - 0.38, np.hypot(x, y + 0.1) - 0.5, np.hypot(x - 0.42, y - 0.12) - 0.36]
            d = np.minimum.reduce(parts)
            d = np.maximum(d, y - 0.4)
            return _soft(d, 30)
        return _shape(px, fn)
    c = cloud(int(size / 4))
    return _scatter_shapes(size, [(c, _outline(c) * 0.5), (cloud(int(size / 6)) * 0.6, _zero(int(size / 6) // 2 * 2 + 1))], 8, seed, size / 3.5, rotate=False)


def cherries(size, seed):
    px = int(size / 5)
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    fruit = np.maximum(_soft(np.hypot(x + 0.35, y - 0.45) - 0.33, 25), _soft(np.hypot(x - 0.35, y - 0.4) - 0.33, 25))
    stems = (np.abs(x + 0.35 - (y - 0.1) * -0.7 * 0.5) < 0.04) & (y < 0.15) & (y > -0.7)
    stems |= (np.abs(x - 0.35 + (y - 0.1) * 0.5 * 0.7) < 0.04) & (y < 0.1) & (y > -0.7)
    shine = np.maximum(_soft(np.hypot(x + 0.45, y - 0.32) - 0.07, 25), _soft(np.hypot(x - 0.25, y - 0.27) - 0.07, 25))
    colour = np.clip(fruit - shine * 0.8, 0, 1)
    ink = np.clip(_outline(fruit) + stems.astype(float) * 0.9, 0, 1)
    return _scatter_shapes(size, [(colour, ink)], 10, seed, size / 3.8)


def bows(size, seed):
    def bow(px):
        def fn(x, y):
            loop_l = np.hypot((x + 0.45) * 1.0, y * 1.6) - 0.42
            loop_r = np.hypot((x - 0.45) * 1.0, y * 1.6) - 0.42
            knot = np.hypot(x, y) - 0.16
            tails = np.where((y > 0.0) & (np.abs(np.abs(x) - y * 0.6) < 0.1) & (y < 0.8), -1.0, 1.0)
            return _soft(np.minimum.reduce([loop_l, loop_r, knot, tails]), 25)
        return _shape(px, fn)
    b = bow(int(size / 4.5))
    return _scatter_shapes(size, [(b, _outline(b) * 0.5)], 9, seed, size / 3.5, rotate=False)


def butterflies(size, seed):
    def fly(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        ax = np.abs(x)
        upper = np.hypot((ax - 0.42) / 0.5, (y + 0.22) / 0.42) < 1.0
        lower = np.hypot((ax - 0.3) / 0.33, (y - 0.38) / 0.32) < 1.0
        wings = (upper | lower) & (ax > 0.06)
        body = (ax < 0.07) & (np.abs(y) < 0.55)
        antennae = (np.abs(ax - (-y - 0.5) * 0.6) < 0.035) & (y < -0.5) & (y > -0.85)
        spots = (np.hypot(ax - 0.52, y + 0.3) < 0.12) | (np.hypot(ax - 0.3, y - 0.42) < 0.08)
        shape = tk.blur_small(wings.astype(float), 0.8)
        colour = np.clip(shape - spots * 0.55, 0, 1)
        ink = np.clip(_outline(shape, 1) + body + antennae + spots * 0.4, 0, 1)
        return colour, ink
    return _scatter_shapes(size, [fly(int(size / 4)), fly(int(size / 6))], 10, seed, size / 3.6)


def paw_prints(size, seed):
    px = int(size / 5)
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    pad = _soft(np.hypot(x * 1.1, (y - 0.3) * 1.3) - 0.38, 25)
    toes = sum(_soft(np.hypot(x - tx, y - ty) - 0.17, 25) for tx, ty in ((-0.55, -0.15), (-0.2, -0.5), (0.2, -0.5), (0.55, -0.15)))
    paw = np.clip(pad + toes, 0, 1)
    return _scatter_shapes(size, [(paw, _zero(paw.shape[0]))], 12, seed, size / 4.2)


def strawberries(size, seed):
    def berry(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        # A rounded heart-ish berry, widest near the top, pointed at the bottom.
        half_width = np.where(y < 0.75, 0.62 * np.sqrt(np.clip(1 - ((y + 0.05) / 0.85) ** 2, 0, 1)) * (1 - 0.25 * (y + 0.05)), 0)
        body = (np.abs(x) < half_width) & (y > -0.55)
        seeds = ((np.sin(x * 20) * np.sin((y + x * 0.5) * 20) > 0.92) & body)
        leaf_tops = (y < -0.42) & (y > -0.82) & (np.abs(x) < 0.55 * (1 - np.abs(np.sin((x + 0.55) * 5.5)) * 0.6) - (-0.42 - y) * 0.6)
        stem = (np.abs(x) < 0.05) & (y < -0.6) & (y > -0.95)
        shape = tk.blur_small(body.astype(float), 0.8)
        top = tk.blur_small((leaf_tops | stem).astype(float), 0.8)
        ink = np.clip(_outline(shape, 1) + seeds * 0.9 + _outline(top, 1) * 0.5, 0, 1)
        return np.clip(shape - top, 0, 1), ink, top
    return _scatter_shapes(size, [berry(int(size / 5)), berry(int(size / 7))], 12, seed, size / 4.2)


def skulls(size, seed):
    px = int(size / 5)
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    head = _soft(np.hypot(x, (y + 0.15) * 1.05) - 0.62, 25)
    jaw = _soft(np.maximum(np.abs(x) - 0.32, np.abs(y - 0.5) - 0.22), 25)
    skull = np.clip(head + jaw, 0, 1)
    eyes = np.maximum(_soft(np.hypot(x + 0.25, y + 0.05) - 0.16, 25), _soft(np.hypot(x - 0.25, y + 0.05) - 0.16, 25))
    nose = _soft(np.maximum(np.abs(x) * 1.6 + (y - 0.32) * -1.0 - 0.1, np.abs(y - 0.25) - 0.08), 25)
    teeth = ((np.abs(np.sin(x * 14)) < 0.25) & (np.abs(y - 0.55) < 0.12) & (np.abs(x) < 0.3)).astype(float)
    colour = np.clip(skull - eyes - nose, 0, 1)
    ink = np.clip(_outline(skull) + eyes + nose + teeth * 0.8, 0, 1)
    return _scatter_shapes(size, [(colour, ink)], 10, seed, size / 3.6, rotate=False)


def crosses(size, seed):
    px = int(size / 4.5)
    c = _shape(px, lambda x, y: _soft(np.minimum(np.maximum(np.abs(x) - 0.14, np.abs(y) - 0.8), np.maximum(np.abs(x) - 0.5, np.abs(y + 0.3) - 0.14)), 20))
    return _scatter_shapes(size, [(c, _outline(c) * 0.4)], 11, seed, size / 3.8, rotate=False)


def bats(size, seed):
    def bat(px):
        h = px / 2
        wing = [(0.0, -0.12), (0.18, -0.2), (0.45, -0.38), (0.75, -0.5), (0.98, -0.42), (0.86, -0.02), (0.68, 0.02),
                (0.56, 0.2), (0.4, 0.06), (0.26, 0.24), (0.12, 0.12), (0.0, 0.3)]
        outline = wing + [(-a, b) for a, b in reversed(wing[1:-1])]
        shape = tk.polygon_mask([(a * h, b * h) for a, b in outline], int(px) | 1)
        r = int(px) // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        head = (np.hypot(x, y + 0.18) < 0.16) | ((np.abs(x) > 0.04) & (np.abs(x) < 0.14) & (y < -0.24) & (y > -0.42 + np.abs(x)))
        shape = np.clip(shape + head, 0, 1)
        return shape
    b, small = bat(int(size / 3.2)), bat(int(size / 5))
    return _scatter_shapes(size, [(b, b * 0.2), (small * 0.7, small * 0.15)], 8, seed, size / 3.2, rotate=False)


def spiderweb(size, seed):
    x, y = tk.grid(size)
    u, v = x - 0.5, y - 0.5
    r = np.hypot(u, v)
    a = np.arctan2(v, u)
    spokes = np.exp(-(np.sin(a * 4) * r / 0.016) ** 2)
    rings = _band((r * 9 + 0.15 * np.cos(a * 8) * r * 4) % 1.0, 0.5, 0.13, 0.02)
    web = np.clip(spokes + rings, 0, 1) * (r < 0.5)
    return web, web * 0.3


def lightning(size, seed):
    px = int(size / 4)
    pts = [(-0.3, -0.9), (0.35, -0.9), (0.05, -0.15), (0.45, -0.15), (-0.35, 0.95), (-0.05, 0.1), (-0.45, 0.1)]
    bolt = tk.polygon_mask([(p[0] * px / 2, p[1] * px / 2) for p in pts], px | 1)
    return _scatter_shapes(size, [(bolt, _outline(bolt) * 0.5)], 9, seed, size / 3.5)


def flames(size, seed):
    """Flames: a row of licking tongues rising from a solid band, with a clear gap above them before the next row."""
    x, y = tk.grid(size)
    n = tk.value_noise(size, 6, seed, 3)
    tongues = 0.5 + 0.5 * np.sin(2 * np.pi * (x * 4 + n * 0.6))
    # The band is solid at the bottom of the tile (y near 1) and the tongues rise from it, reaching at most 85% of the
    # way up, so the top of the tile is clear and the next row's band below meets clear cloth, not more flame.
    rise = 1 - y
    height = 0.12 + 0.6 * tongues * (0.75 + 0.25 * tk.value_noise(size, 12, seed + 1, 2)) - rise
    body = tk.smoothstep(-0.02, 0.02, height)
    core = tk.smoothstep(-0.02, 0.02, height - 0.14)
    return np.clip(body * 0.55 + core * 0.45, 0, 1), _outline(body) * 0.6


def camo(size, seed):
    a = tk.value_noise(size, 4, seed, octaves=4, persistence=0.55)
    b = tk.value_noise(size, 4, seed + 7, octaves=4, persistence=0.55)
    blobs1 = (a > 0.55).astype(float)
    blobs2 = ((b > 0.58) & (a <= 0.55)).astype(float)
    blobs3 = ((a < 0.35) & (b < 0.45)).astype(float)
    colour = np.clip(blobs1 * 1.0 + blobs2 * 0.5, 0, 1)
    ink = blobs3 * 0.55
    return tk.blur_small(colour, 1), tk.blur_small(ink, 1)


def checkerboard_skate(size, seed):
    x, y = tk.grid(size)
    return ((np.floor(x * 4) + np.floor(y * 4)) % 2 == 0).astype(float), _zero(size)


# ------------------------------------------------------------------------------------------------------- animals

def leopard(size, seed):
    """Leopard: broken dark rosettes round a warmer centre, scattered over the cloth, with small solid spots between."""
    rng = np.random.default_rng(seed)
    colour, ink = _zero(size), _zero(size)
    for cx, cy in tk.scatter(size, 22, seed, size / 6.2):
        px = int(size / 7 * (0.8 + 0.4 * rng.random())) | 1
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        wobble = 1 + 0.18 * tk.value_noise(px, 3, int(rng.integers(1 << 30)), 2)[:px, :px] - 0.09
        d = np.hypot(x * (1 + 0.25 * rng.random()), y) * wobble
        ring = ((d > 0.55) & (d < 0.85)).astype(float)
        a = np.arctan2(y, x)
        gaps = np.sin(a * rng.integers(3, 6) + rng.random() * 6) > -0.35  # the ring breaks into 3-5 crescents
        centre = (d < 0.6).astype(float)
        angle = rng.random() * 180
        tk.stamp(colour, tk.rotate_shape(centre, angle) * 0.7, cx, cy)
        tk.stamp(ink, tk.rotate_shape(ring * gaps, angle), cx, cy)
    for cx, cy in tk.scatter(size, 30, seed + 1, size / 12):
        tk.stamp(ink, tk.disc(size / 90 * (1 + rng.random()), 1.0), cx, cy)
    return colour, np.clip(ink, 0, 1)


def zebra(size, seed):
    x, y = tk.grid(size)
    warp = tk.value_noise(size, 4, seed, octaves=3)
    t = (x * 5 + warp * 1.4 + 0.3 * np.sin(y * 2 * np.pi)) % 1.0
    stripe = _band(t, 0.5, 0.42, 0.02)
    return stripe * 0.0, stripe


def tiger(size, seed):
    x, y = tk.grid(size)
    warp = tk.value_noise(size, 5, seed, octaves=3)
    t = (y * 6 + warp * 0.9) % 1.0
    width = 0.12 + 0.18 * tk.value_noise(size, 6, seed + 2, 2)
    stripe = tk.smoothstep(0.0, 0.03, width - np.abs(t - 0.5)) * (tk.value_noise(size, 8, seed + 4, 2) > 0.38)
    return _zero(size) + 0.6, stripe


def cow(size, seed):
    a = tk.value_noise(size, 3, seed, octaves=4, persistence=0.5)
    spots = tk.smoothstep(0.58, 0.6, a)
    return _zero(size), spots


def snakeskin(size, seed):
    x, y = tk.grid(size)
    n = 8
    u, v = x * n, y * n * 1.6
    row = np.floor(v)
    u2 = u + np.where(row % 2 == 0, 0.0, 0.5)
    cu, cv = u2 % 1.0 - 0.5, v % 1.0 - 0.5
    scale = np.abs(cu) + np.abs(cv) * 0.8
    edge = tk.smoothstep(0.38, 0.46, scale)
    blotches = tk.value_noise(size, 4, seed, 3) > 0.55
    return np.where(blotches, 0.8, 0.3) * (1 - edge), edge * 0.7


# ---------------------------------------------------------------------------------------------------- abstract

def tie_dye(size, seed):
    x, y = tk.grid(size)
    u, v = x - 0.5, y - 0.5
    r = np.hypot(u, v)
    a = np.arctan2(v, u)
    n = tk.value_noise(size, 6, seed, 3)
    swirl = 0.5 + 0.5 * np.sin(a * 3 + r * 28 + n * 4)
    # Tileable: the swirl fades into an even bleed well before the tile's edges (the square's, not a circle's: its
    # corners reached the edge half-swirled), so neighbouring tiles meet on the same bleed.
    edge = np.maximum(np.abs(u), np.abs(v))
    fade = 1 - tk.smoothstep(0.3, 0.46, edge)
    bleed = 0.3 + 0.1 * tk.value_noise(size, 4, seed + 1, 2)
    return np.clip(swirl * fade + bleed * (1 - fade), 0, 1), _zero(size)


def marble(size, seed):
    x, y = tk.grid(size)
    n = tk.value_noise(size, 4, seed, octaves=5, persistence=0.55)
    veins = np.abs(np.sin(2 * np.pi * (x * 2 + n * 2.5)))
    return np.clip(1 - veins, 0, 1) ** 4, (veins < 0.04).astype(float) * 0.5


def brushstrokes(size, seed):
    """Brushstrokes: bold sweeps of paint in two colours, the bristles streaking each one and its end running dry."""
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(6):
        length = int(size * (0.34 + 0.12 * rng.random())) | 1
        h = length // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float) / h
        width = (0.2 + 0.06 * rng.random()) * (1 - np.clip(xx, 0, 1) ** 4) * (1 - np.clip(-xx - 0.85, 0, 1) * 6)
        streaks = np.sin(yy * 60 + 3 * np.sin(xx * 4) + rng.random() * 6)
        dry = np.clip((xx - 0.35) / 0.6, 0, 1)  # the end of the stroke, where the brush runs out
        body = np.abs(yy - 0.1 * np.sin(xx * 2.5)) < width
        stroke = tk.blur_small((body & (streaks > -1 + 1.6 * dry)).astype(float), 0.8)
        none = np.zeros_like(stroke)
        shapes.append((stroke, none, none) if k % 3 else (none, none, stroke))
    return _scatter_shapes(size, shapes, 9, seed, size / 3.6)


def terrazzo(size, seed):
    rng = np.random.default_rng(seed)
    colour, ink = _zero(size), _zero(size)
    for i, (cx, cy) in enumerate(tk.scatter(size, 60, seed, size / 14)):
        n = rng.integers(4, 7)
        rad = size / 40 * (0.6 + rng.random())
        pts = [(np.cos(a) * rad * (0.6 + 0.6 * rng.random()), np.sin(a) * rad * (0.6 + 0.6 * rng.random()))
               for a in np.sort(rng.random(n) * 2 * np.pi)]
        chip = tk.polygon_mask(pts, int(rad * 3) | 1)
        tone = (1.0, 0.5, 0.0)[i % 3]
        if tone > 0:
            tk.stamp(colour, chip * tone, cx, cy)
        else:
            tk.stamp(ink, chip * 0.6, cx, cy)
    return colour, ink


def squiggles(size, seed):
    rng = np.random.default_rng(seed)
    colour, ink = _zero(size), _zero(size)
    for k, (cx, cy) in enumerate(tk.scatter(size, 16, seed, size / 5)):
        px = int(size / 6) | 1
        h = px // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float) / h
        if k % 3 == 0:
            m = (np.abs(yy - 0.3 * np.sin(xx * 6)) < 0.12).astype(float) * (np.abs(xx) < 0.9)
        elif k % 3 == 1:
            m = (np.abs(np.hypot(xx, yy) - 0.5) < 0.14).astype(float)
        else:
            m = ((np.abs(xx) + np.abs(yy) < 0.75) & (np.abs(xx) + np.abs(yy) > 0.45)).astype(float)
        m = tk.rotate_shape(m, rng.random() * 180)
        tk.stamp(colour if k % 2 else ink, m * (1.0 if k % 2 else 0.6), cx, cy)
    return colour, ink


def raindrops(size, seed):
    def drop(px):
        return _shape(px, lambda x, y: _soft(np.where(y > -0.1, np.hypot(x, y - 0.25) - 0.5, np.abs(x) - (0.5 * (y + 0.85) / 0.75)), 20) * (y > -0.85))
    d = drop(int(size / 7))
    return _scatter_shapes(size, [(d, _outline(d) * 0.3)], 20, seed, size / 5.5, rotate=False)


def snowflakes(size, seed):
    def flake(px):
        def fn(x, y):
            a = np.arctan2(y, x)
            r = np.hypot(x, y)
            arm = np.abs(np.sin(a * 3)) * r < 0.09
            branch = (np.abs(np.sin(a * 3 + 0.5)) * r < 0.075) & (r > 0.4) & (r < 0.7)
            return ((arm | branch) & (r < 0.9)).astype(float)
        return _shape(px, fn)
    f = flake(int(size / 5))
    return _scatter_shapes(size, [(f, _zero(f.shape[0])), (flake(int(size / 8)) * 0.6, _zero(int(size / 8) // 2 * 2 + 1))], 12, seed, size / 4.5)


# ---------------------------------------------------------------------- second pass: more stripes, checks and dots

def ticking(size, seed):
    """Ticking: pairs of narrow stripes, like a mattress or a work apron."""
    x, y = tk.grid(size)
    return np.maximum(_band(x * 4, 0.3, 0.1, 0.01), _band(x * 4, 0.52, 0.1, 0.01)), _zero(size)


def rugby(size, seed):
    """Rugby hoops: wide bands across with a thin second-tone stripe between."""
    x, y = tk.grid(size)
    return np.maximum(_band(y, 0.25, 0.5, 0.004), 0.5 * _band(y, 0.75, 0.08, 0.004)), _zero(size)


def pyjama(size, seed):
    """Pyjama stripes: a broad stripe flanked by two fine ones in a softer tone."""
    x, y = tk.grid(size)
    t = x * 2
    return np.maximum.reduce([_band(t, 0.3, 0.16), 0.5 * _band(t, 0.5, 0.04), 0.5 * _band(t, 0.1, 0.04)]), _zero(size)


def glen_plaid(size, seed):
    """Glen plaid (Prince of Wales check): blocks of fine check and of hairline stripes, crossed by an overcheck."""
    x, y = tk.grid(size)
    n = 16
    fine = ((np.floor(x * n) + np.floor(y * n)) % 2).astype(float)
    hx, hy = np.floor(x * 2) % 2, np.floor(y * 2) % 2
    lines_x = _band(x * n / 2, 0.5, 0.2, 0.03)
    lines_y = _band(y * n / 2, 0.5, 0.2, 0.03)
    colour = np.where((hx == 0) & (hy == 0), fine * 0.85,
                      np.where((hx == 1) & (hy == 1), np.maximum(lines_x, lines_y) * 0.6, np.where(hx == 1, lines_x, lines_y) * 0.75))
    over = np.maximum(_band(x, 0.75, 0.014, 0.003), _band(y, 0.75, 0.014, 0.003))
    return np.clip(colour, 0, 1) * (1 - over), _zero(size), over


def madras(size, seed):
    """Madras: a summery plaid of uneven bands in several tones."""
    x, y = tk.grid(size)
    first = [(0.1, 0.16, 1.0), (0.45, 0.2, 0.7), (0.66, 0.04, 1.0)]
    second = [(0.3, 0.06, 1.0), (0.8, 0.12, 0.6)]

    def woven(bands):
        a = np.maximum.reduce([v * _band(x, c, w, 0.004) for c, w, v in bands])
        b = np.maximum.reduce([v * _band(y, c, w, 0.004) for c, w, v in bands])
        return a, b
    a, b = woven(first)
    c, d = woven(second)
    return np.clip(0.55 * a + 0.55 * b, 0, 1), np.clip(a * b + c * d, 0, 1) * 0.3, np.clip(0.55 * c + 0.55 * d, 0, 1)


def tattersall(size, seed):
    """Tattersall: a fine grid of lines in two alternating colours (country shirts, waistcoats)."""
    x, y = tk.grid(size)
    a = np.maximum(_band(x * 4, 0.25, 0.07, 0.01), _band(y * 4, 0.25, 0.07, 0.01))
    b = np.maximum(_band(x * 4, 0.75, 0.07, 0.01), _band(y * 4, 0.75, 0.07, 0.01))
    return a, b * 0.6


def graph_paper(size, seed):
    """Graph paper: a fine grid with a bolder one every five squares."""
    x, y = tk.grid(size)
    fine = np.maximum(_band(x * 10, 0.5, 0.06, 0.03), _band(y * 10, 0.5, 0.06, 0.03))
    bold = np.maximum(_band(x * 2, 0.5, 0.025, 0.01), _band(y * 2, 0.5, 0.025, 0.01))
    return np.maximum(0.55 * fine, bold), _zero(size)


def wavy_check(size, seed):
    """A warped checkerboard, the squares bulging and bending."""
    x, y = tk.grid(size)
    u = x + 0.03 * np.sin(2 * np.pi * 2 * y)
    v = y + 0.03 * np.sin(2 * np.pi * 2 * x)
    c = ((np.floor(u * 4) + np.floor(v * 4)) % 2).astype(float)
    return tk.blur_small(c, 1), _zero(size)


def _dot_grid(size, n, radius, offset=True):
    """Round dots, ``n`` per tile each way (rows offset by half when ``offset``), ``radius`` in cells; the cell centre
    of each pixel too, for dots that vary over the tile."""
    x, y = tk.grid(size)
    row = np.floor(y * n)
    shift = 0.5 * (row % 2) if offset else 0.0
    u = (x * n + shift) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    cx = (np.floor(x * n + shift) + 0.5 - shift) / n
    cy = (row + 0.5) / n
    return np.hypot(u, v), size / n, cx % 1.0, cy


def pin_dots(size, seed):
    """Pin dots: tiny dots, close together."""
    r, px, _, _ = _dot_grid(size, 14, 0)
    return np.clip((0.17 - r) * px + 0.5, 0, 1), _zero(size)


def halftone(size, seed):
    """Halftone: dots swelling and shrinking in diagonal waves, like a comic's print."""
    r, px, cx, cy = _dot_grid(size, 12, 0)
    radius = 0.08 + 0.36 * (0.5 + 0.5 * np.sin(2 * np.pi * (cx + cy)))
    return np.clip((radius - r) * px + 0.5, 0, 1), _zero(size)


def plus_signs(size, seed):
    """Little plus signs (Swiss crosses) in offset rows."""
    x, y = tk.grid(size)
    n = 6
    u = (x * n + 0.5 * (np.floor(y * n) % 2)) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    plus = ((np.abs(u) < 0.07) & (np.abs(v) < 0.22)) | ((np.abs(v) < 0.07) & (np.abs(u) < 0.22))
    return tk.blur_small(plus.astype(float), 0.8), _zero(size)


# -------------------------------------------------------------------------------------------------- geometric

def seigaiha(size, seed):
    """Seigaiha (Japanese waves): overlapping fans of concentric arcs, each row tucked under the next."""
    x, y = tk.grid(size)
    n = 4
    colour, ink = _zero(size), _zero(size)
    for row in range(2 * n + 1):
        cy = row / (2 * n)
        shift = 0.5 / n if row % 2 else 0.0
        u = ((x - shift) * n) % 1.0 - 0.5
        dy = ((y - cy + 0.5) % 1.0 - 0.5) * n
        d = np.hypot(u, dy) / 0.5
        fan = (d < 1.0) & (dy <= 0.0)
        rings = (np.floor(d * 4) % 2 == 0).astype(float)
        edge = np.abs((d * 4) % 1.0 - 0.5) > 0.42
        colour = np.where(fan, rings, colour)
        ink = np.where(fan, edge * 0.5, ink)
    return colour, ink


def quatrefoil(size, seed):
    """Quatrefoil: a lattice of four-lobed outlines (Moroccan tiles)."""
    x, y = tk.grid(size)
    n = 4
    u = (x * n) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    lobes = np.minimum.reduce([np.hypot(u - dx, v - dy) - 0.2 for dx, dy in ((0.17, 0), (-0.17, 0), (0, 0.17), (0, -0.17))])
    corner = np.hypot(np.abs(u) - 0.5, np.abs(v) - 0.5) - 0.12
    line = np.minimum(np.abs(lobes), np.abs(corner))
    return np.clip((0.035 - line) * size / n + 0.5, 0, 1), _zero(size)


def ikat(size, seed):
    """Ikat: nested diamonds whose edges feather sideways, as the dyed threads slip."""
    x, y = tk.grid(size)
    rng = np.random.default_rng(seed)
    jitter = rng.normal(0, 0.012, size)[(np.arange(size) // 3) % size][:, None]
    u = ((x + jitter) * 3) % 1.0 - 0.5
    v = (y * 2) % 1.0 - 0.5
    d = np.abs(u) * 2 + np.abs(v) * 2
    outer = (d < 0.95) & (d > 0.62)
    core = d < 0.32
    return np.clip(outer * 1.0 + core * 0.5, 0, 1), _zero(size)


def aztec(size, seed):
    """Aztec and tribal bands: rows of stepped triangles, diamonds and thin stripes."""
    x, y = tk.grid(size)
    v = (y * 2) % 1.0
    u = (x * 4) % 1.0
    steps = np.floor(v / 0.4 * 5) / 5
    tri = (np.abs(u - 0.5) * 2 < 1 - steps) & (v < 0.4)
    diamond = (np.abs(((x * 8) % 1.0) - 0.5) * 2 + np.abs(v - 0.62) / 0.08 < 1) & (np.abs(v - 0.62) < 0.08)
    stripes = _band(v, 0.48, 0.03, 0.005) + _band(v, 0.76, 0.03, 0.005)
    colour = np.clip(tri * 1.0 + diamond * 0.5, 0, 1)
    return tk.blur_small(colour, 0.7), np.clip(stripes, 0, 1) * 0.7


def op_art(size, seed):
    """Op art: concentric squares in alternating bands."""
    x, y = tk.grid(size)
    m = np.maximum(np.abs((x * 2) % 1.0 - 0.5), np.abs((y * 2) % 1.0 - 0.5))
    return (np.floor(m * 12) % 2).astype(float), _zero(size)


# ---------------------------------------------------------------------------------------------- florals and fruit

def _rose(px):
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    rad, ang = np.hypot(x, y), np.arctan2(y, x)
    head = _soft(rad - (0.5 + 0.05 * np.cos(5 * ang)), r)
    swirl = ((rad * 7 - ang / np.pi) % 1.0 < 0.18) & (rad < 0.46)
    leaves = 0.0
    for side in (-1, 1):
        lx, ly = x - side * 0.62, y - 0.42
        a = np.radians(30 * side)
        rx, ry = np.cos(a) * lx + np.sin(a) * ly, -np.sin(a) * lx + np.cos(a) * ly
        leaves = np.maximum(leaves, _soft(np.hypot(rx / 0.36, ry / 0.17) - 1, r * 0.2))
    leaves = leaves * (1 - head)
    ink = np.clip(_outline(head, 1) + swirl * 0.75 + _outline(leaves, 1) * 0.6, 0, 1)
    return np.clip(head, 0, 1), ink, np.clip(leaves, 0, 1)


def roses(size, seed):
    """Cabbage roses: swirled heads with a pair of leaves each (cottage florals)."""
    return _scatter_shapes(size, [_rose(int(size / 2.8)), _rose(int(size / 4.2))], 7, seed, size / 3.3)


def sunflowers(size, seed):
    """Sunflowers: rings of pointed petals round a dark seeded centre."""
    def flower(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y), np.arctan2(y, x)
        petals = _soft(rad - (0.45 + 0.5 * np.abs(np.cos(6 * ang)) ** 0.7), r)
        centre = _soft(rad - 0.36, r)
        seeds = (np.sin(x * 30) * np.sin(y * 30) > 0.3) * centre
        return np.clip(petals - centre, 0, 1), np.clip(centre * 0.75 + seeds * 0.25 + _outline(petals, 1) * 0.4, 0, 1)
    return _scatter_shapes(size, [flower(int(size / 3.6)), flower(int(size / 5.5))], 9, seed, size / 4)


def tulips(size, seed):
    """Tulips: three-pointed cups on stems with a long leaf."""
    def tulip(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        cup = (np.hypot(x / 0.36, (y + 0.3) / 0.36) < 1) & (y > -0.62 + 0.16 * np.abs(np.sin(np.pi * x / 0.24)) * (np.abs(x) < 0.36))
        cup = tk.blur_small(cup.astype(float), 0.8)
        stem = (np.abs(x - 0.04 * (y - 0.05)) < 0.035) & (y > 0.0) & (y < 0.95)
        leaf = _soft(np.hypot((x - 0.18 - (y - 0.6) * 0.3) / 0.12, (y - 0.55) / 0.36) - 1, r * 0.15) * (1 - cup)
        petal_lines = ((np.abs(x - 0.12) < 0.02) | (np.abs(x + 0.12) < 0.02)) & (y < -0.25) & (y > -0.55)
        green = np.clip(np.maximum(leaf, tk.blur_small(stem.astype(float), 0.8)) * (1 - cup), 0, 1)
        ink = np.clip(_outline(cup, 1) + petal_lines * 0.6 + _outline(green, 1) * 0.4, 0, 1)
        return cup, ink, green
    return _scatter_shapes(size, [tulip(int(size / 2.8))], 8, seed, size / 3.6, rotate=False)


def lemons(size, seed):
    """Lemons: pointed ovals with a leaf and a shine (summer dresses)."""
    def lemon(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.maximum(np.hypot(x, y - 0.3) - 0.75, np.hypot(x, y + 0.3) - 0.75)
        fruit = _soft(body, r)
        shine = _soft(np.hypot((x + 0.25) / 0.16, (y + 0.12) / 0.07) - 1, r * 0.1)
        leaf = _soft(np.hypot((x - 0.5) / 0.24, (y + 0.5) / 0.11) - 1, r * 0.1) * (1 - fruit)
        colour = np.clip(fruit - 0.7 * shine, 0, 1)
        return colour, np.clip(_outline(fruit, 1) * 0.6 + _outline(leaf, 1) * 0.5, 0, 1), leaf
    return _scatter_shapes(size, [lemon(int(size / 3.4)), lemon(int(size / 4.6))], 10, seed, size / 3.8)


def mushrooms(size, seed):
    """Toadstools: spotted caps on stout stems (cottagecore)."""
    def toadstool(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        cap = _soft(np.hypot(x / 0.8, (y + 0.02) / 0.62) - 1, r * 0.6) * (y < 0.05)
        stem = _soft(np.maximum(np.abs(x) - 0.24 - 0.06 * (y - 0.05), np.abs(y - 0.45) - 0.4), r) * (1 - cap)
        spots = sum(_soft(np.hypot(x - sx, y - sy) - sr, r) for sx, sy, sr in ((-0.35, -0.2, 0.12), (0.2, -0.35, 0.1), (0.45, -0.08, 0.08), (-0.05, -0.08, 0.07)))
        ink = np.clip(_outline(cap, 1) + _outline(stem, 1) * 0.7, 0, 1)
        return np.clip(cap - spots, 0, 1), ink, np.clip(stem + spots * cap, 0, 1)
    return _scatter_shapes(size, [toadstool(int(size / 4.5)), toadstool(int(size / 6.5))], 11, seed, size / 4.2, rotate=False)


def ferns(size, seed):
    """Fern fronds: curving stems of leaflets, smaller towards the tip (botanical prints)."""
    def frond(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        bend = 0.18 * (y + 1) ** 2 - 0.18
        xs = x - bend
        stem = (np.abs(xs) < 0.04) & (np.abs(y) < 0.95)
        leaflets = np.zeros_like(x)
        for k in range(9):
            t = -0.85 + k * 0.2
            w = 0.36 * (1 - (t + 0.95) / 2.2)
            for side in (-1, 1):
                cx = side * (w * 0.6)
                leaflets = np.maximum(leaflets, _soft(np.hypot((xs - cx) / max(w * 0.7, 0.06), (y - t - side * 0.03) / 0.09) - 1, r * 0.08))
        return np.clip(leaflets, 0, 1), np.clip(stem * 0.8 + _outline(leaflets, 1) * 0.3, 0, 1)
    return _scatter_shapes(size, [frond(int(size / 2.8)), frond(int(size / 4))], 7, seed, size / 3.4)


def holly(size, seed):
    """Holly: spiky leaves and clusters of berries (winter jumpers)."""
    def sprig(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        leaves = 0.0
        for side in (-1, 1):
            lx, ly = x - side * 0.42, y + 0.1
            a = np.radians(-25 * side)
            rx, ry = np.cos(a) * lx + np.sin(a) * ly, -np.sin(a) * lx + np.cos(a) * ly
            spiky = 0.2 - 0.05 * np.abs(np.sin(rx * 14))
            leaves = np.maximum(leaves, _soft(np.abs(ry) - spiky * np.sqrt(np.clip(1 - (rx / 0.48) ** 2, 0, 1)), r) * (np.abs(rx) < 0.48))
        berries = sum(_soft(np.hypot(x - bx, y - by) - 0.13, r) for bx, by in ((-0.1, 0.25), (0.12, 0.28), (0.0, 0.08)))
        berries = np.clip(berries, 0, 1)
        ink = np.clip(_outline(leaves, 1) * 0.6 + _outline(berries, 1) * 0.5, 0, 1)
        return berries, ink, np.clip(leaves * (1 - berries), 0, 1)
    return _scatter_shapes(size, [sprig(int(size / 3.8))], 9, seed, size / 3.8)


# ------------------------------------------------------------------------------------------------ novelty prints

def anchors(size, seed):
    """Anchors (nautical): ring, stock, shank and curved arms."""
    def anchor(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        ring = np.abs(np.hypot(x, y + 0.72) - 0.14) < 0.05
        shank = (np.abs(x) < 0.06) & (y > -0.6) & (y < 0.7)
        stock = (np.abs(y + 0.4) < 0.05) & (np.abs(x) < 0.38)
        arc = (np.abs(np.hypot(x, y - 0.12) - 0.62) < 0.07) & (y > 0.35)
        tips = (np.hypot(np.abs(x) - 0.6, y - 0.42) < 0.12)
        shape = tk.blur_small((ring | shank | stock | arc | tips).astype(float), 0.8)
        return shape, _zero(shape.shape[0])
    return _scatter_shapes(size, [anchor(int(size / 4.2)), anchor(int(size / 6))], 11, seed, size / 4.2, rotate=False)


def cats(size, seed):
    """Cat faces: round heads with pointed ears, whiskers and little noses."""
    def cat(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        head = np.hypot(x / 0.72, (y - 0.12) / 0.6) < 1
        ears = ((np.abs(x) > 0.18) & (np.abs(x) < 0.68) & (y < -0.3) & (y > -0.85) &
                (y > -0.85 + np.abs(np.abs(x) - 0.48) * 2.6))
        shape = tk.blur_small((head | ears).astype(float), 0.8)
        eyes = (np.hypot(np.abs(x) - 0.3, y - 0.05) < 0.08)
        nose = (np.abs(x) < 0.08 - (y - 0.24) * 0.6) & (y > 0.24) & (y < 0.33)
        whiskers = ((np.abs(y - 0.3 - (np.abs(x) - 0.4) * 0.15) < 0.018) | (np.abs(y - 0.38 + (np.abs(x) - 0.4) * 0.1) < 0.018)) & (np.abs(x) > 0.4) & (np.abs(x) < 0.95)
        return shape, np.clip(_outline(shape, 1) * 0.5 + eyes + nose + whiskers * 0.8, 0, 1)
    return _scatter_shapes(size, [cat(int(size / 4.5)), cat(int(size / 6.5))], 11, seed, size / 4.2, rotate=False)


def music_notes(size, seed):
    """Music notes: quavers and beamed pairs."""
    def note(px, pair):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        def head(cx):
            a = np.radians(-25)
            lx, ly = x - cx, y - 0.6
            return np.hypot((np.cos(a) * lx + np.sin(a) * ly) / 0.24, (-np.sin(a) * lx + np.cos(a) * ly) / 0.16) < 1
        if pair:
            shape = head(-0.45) | head(0.4) | ((np.abs(x + 0.25) < 0.04) & (y > -0.6) & (y < 0.6)) | \
                ((np.abs(x - 0.6) < 0.04) & (y > -0.7) & (y < 0.55)) | ((np.abs(y + 0.62 + (x + 0.25) * 0.12) < 0.08) & (x > -0.28) & (x < 0.64))
        else:
            shape = head(-0.1) | ((np.abs(x - 0.1) < 0.04) & (y > -0.75) & (y < 0.6)) | \
                ((np.abs(x - 0.1 - 0.35 * np.sin(np.clip((y + 0.75) / 0.6, 0, 1) * np.pi / 2)) < 0.05) & (y > -0.75) & (y < -0.15))
        shape = tk.blur_small(shape.astype(float), 0.8)
        return shape, _zero(shape.shape[0])
    return _scatter_shapes(size, [note(int(size / 5), False), note(int(size / 4.5), True)], 12, seed, size / 4.5, rotate=False)


def rainbows(size, seed):
    """Rainbows: arcs of bands in two tones with a cloud at each end."""
    def bow(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        d = np.hypot(x, y - 0.35)
        up = y < 0.35
        bands = (d < 0.9) & (d > 0.36) & up
        even = np.floor((d - 0.36) / 0.18) % 2 == 0
        clouds = sum(_soft(np.hypot(x - sx, y - 0.4) - 0.2, r) for sx in (-0.75, -0.55, 0.55, 0.75))
        clouds = np.clip(clouds, 0, 1)
        ink = np.clip(_outline(bands.astype(float), 1) * 0.4 + _outline(clouds, 1) * 0.6, 0, 1)
        return (tk.blur_small((bands & even) * (1 - clouds), 0.7), ink, tk.blur_small((bands & ~even) * (1 - clouds), 0.7))
    return _scatter_shapes(size, [bow(int(size / 3.6))], 8, seed, size / 3.4, rotate=False)


def lips(size, seed):
    """Lips: a scatter of kisses."""
    def kiss(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        outer = np.hypot(x / 0.85, y / 0.45) < 1
        bow = np.hypot(np.abs(x) - 0.0, y + 0.52) < 0.2
        shape = tk.blur_small((outer & ~bow).astype(float), 0.8)
        mouth = (np.abs(y - 0.03 - 0.12 * (x / 0.85) ** 2 + 0.1 * np.exp(-(x / 0.12) ** 2)) < 0.03) & (np.abs(x) < 0.82)
        return shape, np.clip(mouth * 0.8 + _outline(shape, 1) * 0.3, 0, 1)
    return _scatter_shapes(size, [kiss(int(size / 3.8))], 11, seed, size / 4)


def planets(size, seed):
    """Planets: ringed worlds among little stars (space prints)."""
    def planet(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        ball = np.hypot(x, y) < 0.56
        a = np.radians(-20)
        rx, ry = np.cos(a) * x + np.sin(a) * y, -np.sin(a) * x + np.cos(a) * y
        e = np.hypot(rx / 0.92, ry / 0.24)
        ring = (e < 1) & (e > 0.8) & ~(ball & (ry < 0))
        shape = tk.blur_small(ball.astype(float), 0.8)
        band = tk.blur_small(ring.astype(float), 0.8)
        return np.clip(shape - band, 0, 1), np.clip(_outline(shape, 1) * 0.5 + _outline(band, 1) * 0.5, 0, 1), band
    colour, ink, accent = _scatter_shapes(size, [planet(int(size / 3.5)), planet(int(size / 5.5))], 6, seed, size / 3)
    return np.maximum(colour, stars(size, seed + 5)[0] * 0.5 * (1 - accent)), ink, accent


def fish(size, seed):
    """Little fish swimming every which way."""
    def one(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.hypot((x + 0.1) / 0.6, y / 0.36) < 1
        tail = (x > 0.42) & (x < 0.9) & (np.abs(y) < (x - 0.42) * 0.9)
        shape = tk.blur_small((body | tail).astype(float), 0.8)
        eye = np.hypot(x + 0.42, y + 0.06) < 0.07
        gill = (np.abs(np.hypot(x + 0.05, y) - 0.3) < 0.025) & (x < -0.12) & (np.abs(y) < 0.25)
        return shape, np.clip(eye + gill * 0.7 + _outline(shape, 1) * 0.3, 0, 1)
    return _scatter_shapes(size, [one(int(size / 3.8)), one(int(size / 5.5))], 12, seed, size / 4.4)


def seagulls(size, seed):
    """Seagulls: little flying birds, two curved strokes each."""
    def gull(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        wing = np.abs(y - (0.5 * (np.abs(x) - 0.42) ** 2 - 0.25 - 0.3 * np.abs(x))) < 0.08 + 0.06 * (1 - np.abs(x))
        shape = tk.blur_small((wing & (np.abs(x) < 0.9)).astype(float), 0.8)
        return shape, _zero(shape.shape[0])
    return _scatter_shapes(size, [gull(int(size / 4.2)), gull(int(size / 6))], 14, seed, size / 5, rotate=False)


def bunnies(size, seed):
    """Bunny heads with long ears."""
    def bunny(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        head = np.hypot(x / 0.5, (y - 0.4) / 0.42) < 1
        ears = (np.hypot((np.abs(x) - 0.2) / 0.13, (y + 0.3) / 0.48) < 1)
        inner = (np.hypot((np.abs(x) - 0.2) / 0.06, (y + 0.3) / 0.36) < 1)
        shape = tk.blur_small((head | ears).astype(float), 0.8)
        eyes = np.hypot(np.abs(x) - 0.2, y - 0.35) < 0.06
        nose = np.hypot(x, y - 0.52) < 0.05
        return np.clip(shape - 0.5 * inner, 0, 1), np.clip(eyes + nose + _outline(shape, 1) * 0.4, 0, 1)
    return _scatter_shapes(size, [bunny(int(size / 4.5)), bunny(int(size / 6.5))], 11, seed, size / 4.2, rotate=False)


def pumpkins(size, seed):
    """Pumpkins: ribbed lobes and a curly stem (autumn)."""
    def pumpkin(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        lobes = [np.hypot((x - cx) / w, (y - 0.15) / 0.5) < 1 for cx, w in ((-0.4, 0.4), (0.4, 0.4), (-0.15, 0.38), (0.15, 0.38))]
        body = lobes[0] | lobes[1] | lobes[2] | lobes[3]
        shape = tk.blur_small(body.astype(float), 0.8)
        ribs = (np.abs(np.abs(x) - 0.3) < 0.025) & (np.abs(y - 0.15) < 0.38 * np.sqrt(np.clip(1 - ((np.abs(x) - 0.3) / 0.5) ** 2, 0, 1)))
        stem = (np.abs(x - 0.06 * (y + 0.4)) < 0.07) & (y < -0.28) & (y > -0.65)
        return shape, np.clip(_outline(shape, 1) * 0.6 + ribs * 0.6 + stem, 0, 1)
    return _scatter_shapes(size, [pumpkin(int(size / 4)), pumpkin(int(size / 6))], 10, seed, size / 4, rotate=False)


def eyes(size, seed):
    """Staring eyes: almond shapes with irises and lashes (gothic, surreal)."""
    def eye(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        almond = np.maximum(np.hypot(x, y - 0.55) - 0.8, np.hypot(x, y + 0.55) - 0.8)
        white = _soft(almond, r)
        iris = _soft(np.hypot(x, y) - 0.24, r) * white
        pupil = _soft(np.hypot(x, y) - 0.1, r)
        lashes = ((np.abs(np.sin(np.arctan2(y + 0.55, x) * 9)) < 0.25) & (np.hypot(x, y + 0.55) > 0.8) & (np.hypot(x, y + 0.55) < 0.95) & (y < -0.12))
        return np.clip(white - iris * 0.5, 0, 1), np.clip(_outline(white, 1) * 0.8 + pupil + lashes * 0.8, 0, 1)
    return _scatter_shapes(size, [eye(int(size / 4.5))], 11, seed, size / 4.2, rotate=False)


def bees(size, seed):
    """Bumblebees: striped bodies with round wings."""
    def bee(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.hypot(x / 0.55, (y - 0.15) / 0.36) < 1
        wings = (np.hypot(np.abs(x) - 0.22, y + 0.32) < 0.26) & ~body
        stripes = body & (np.abs(((x + 0.55) / 0.27) % 1.0 - 0.5) < 0.2) & (x > -0.3)
        sting = (x > 0.5) & (x < 0.7) & (np.abs(y - 0.15) < (0.7 - x) * 0.4)
        ink = np.clip(stripes * 0.85 + _outline(body.astype(float), 1) * 0.5 + _outline(wings.astype(float), 1) * 0.4 + sting, 0, 1)
        return tk.blur_small(body.astype(float), 0.8), ink, tk.blur_small(wings.astype(float), 0.8)
    return _scatter_shapes(size, [bee(int(size / 3.8)), bee(int(size / 5.5))], 11, seed, size / 4.2)


def ladybugs(size, seed):
    """Ladybirds: spotted domes with a dark head."""
    def bug(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        shell = np.hypot(x / 0.6, (y - 0.1) / 0.66) < 1
        head = (np.hypot(x, y + 0.55) < 0.3) & ~shell
        spots = shell & ((np.hypot(np.abs(x) - 0.28, y + 0.1) < 0.11) | (np.hypot(np.abs(x) - 0.3, y - 0.35) < 0.1) | (np.hypot(x, y - 0.65) < 0.08))
        split = shell & (np.abs(x) < 0.025) & (y > -0.5)
        shape = tk.blur_small(shell.astype(float), 0.8)
        return shape, np.clip(head + spots + split + _outline(shape, 1) * 0.5, 0, 1)
    return _scatter_shapes(size, [bug(int(size / 5.5)), bug(int(size / 8))], 14, seed, size / 5)


def shells(size, seed):
    """Scallop shells and starfish (beachwear)."""
    def shell(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y - 0.55), np.arctan2(x, -(y - 0.55))
        fan = (rad < 0.95 + 0.04 * np.cos(ang * 18)) & (np.abs(ang) < 1.15)
        hinge = (np.abs(x) < 0.25) & (y > 0.4) & (y < 0.62)
        shape = tk.blur_small((fan | hinge).astype(float), 0.8)
        ridges = fan & (np.abs(np.sin(ang * 9)) < 0.18) & (rad > 0.2)
        return shape, np.clip(ridges * 0.6 + _outline(shape, 1) * 0.5, 0, 1)

    def star(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y), np.arctan2(y, x) + np.pi / 2
        arms = rad < 0.35 + 0.6 * np.abs(np.cos(ang * 2.5)) ** 3
        dots = arms & (np.sin(x * 26) * np.sin(y * 26) > 0.6)
        shape = tk.blur_small(arms.astype(float), 0.8)
        return shape, np.clip(dots * 0.5 + _outline(shape, 1) * 0.4, 0, 1)
    return _scatter_shapes(size, [shell(int(size / 4.5)), star(int(size / 4.5)), shell(int(size / 6.5))], 11, seed, size / 4.2)


def smileys(size, seed):
    """Smiley faces (90s rave and skate prints)."""
    def face(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        disc = _soft(np.hypot(x, y) - 0.85, r)
        eyes = np.hypot((np.abs(x) - 0.28) / 0.08, (y + 0.22) / 0.17) < 1
        smile = (np.abs(np.hypot(x, y + 0.05) - 0.5) < 0.06) & (y > 0.12)
        return disc, np.clip(eyes + smile + _outline(disc, 1) * 0.6, 0, 1)
    return _scatter_shapes(size, [face(int(size / 5)), face(int(size / 7.5))], 13, seed, size / 4.8)


# ------------------------------------------------------------------------------------------- animal and artistic

def giraffe(size, seed):
    """Giraffe: irregular patches separated by narrow pale channels."""
    first, second, _ = tk.worley(size, 7, seed, jitter=0.9)
    patches = tk.smoothstep(0.1, 0.16, second - first)
    rough = tk.value_noise(size, 40, seed + 1, 1)
    return np.clip(patches * (0.85 + 0.15 * rough), 0, 1), _zero(size)


def dalmatian(size, seed):
    """Dalmatian: scattered blotchy spots of every size."""
    rng = np.random.default_rng(seed)
    colour = _zero(size)
    for cx, cy in tk.scatter(size, 26, seed, size / 7):
        rad = size / 24 * (0.5 + 1.0 * rng.random())
        px = int(rad * 3) | 1
        h = px // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float)
        ang = np.arctan2(yy, xx)
        wobble = 1 + 0.15 * np.sin(ang * 3 + rng.random() * 6) + 0.1 * np.sin(ang * 5 + rng.random() * 6)
        spot = np.clip((rad * wobble - np.hypot(xx, yy)) + 0.5, 0, 1)
        tk.stamp(colour, spot, cx, cy)
    return colour, _zero(size)


def cheetah(size, seed):
    """Cheetah: small solid spots packed close."""
    first, second, _ = tk.worley(size, 14, seed, jitter=0.8)
    rough = tk.value_noise(size, 60, seed + 1, 1)
    spots = 1 - tk.smoothstep(0.22, 0.28, first + 0.06 * rough)
    return spots, _zero(size)


def splatter(size, seed):
    """Paint splatter: blots and flicked droplets."""
    rng = np.random.default_rng(seed)
    colour, ink = _zero(size), _zero(size)
    for i, (cx, cy) in enumerate(tk.scatter(size, 7, seed, size / 3.2)):
        rad = size / 18 * (0.7 + 0.6 * rng.random())
        px = int(rad * 4) | 1
        h = px // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float)
        ang = np.arctan2(yy, xx)
        edge = rad * (1 + 0.25 * np.sin(ang * 7 + rng.random() * 6) + 0.12 * np.sin(ang * 13 + rng.random() * 6))
        blot = np.clip(edge - np.hypot(xx, yy) + 0.5, 0, 1)
        tk.stamp(colour if i % 3 else ink, blot * (1.0 if i % 3 else 0.6), cx, cy)
    for cx, cy in tk.scatter(size, 50, seed + 1, size / 20):
        drop = tk.disc(size / 160 * (1 + 2 * rng.random()), 1.0)
        tk.stamp(colour, drop, cx, cy)
    return colour, ink


def shibori(size, seed):
    """Shibori: resist-dyed rings, soft-edged and bleeding into the cloth."""
    x, y = tk.grid(size)
    n = 3
    u = (x * n + 0.5 * (np.floor(y * n) % 2)) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    warp = 0.04 * (tk.value_noise(size, 12, seed, 2) - 0.5)
    rad = np.hypot(u, v) + warp
    rings = 0.5 + 0.5 * np.cos(2 * np.pi * rad * 5)
    fade = tk.smoothstep(0.5, 0.25, rad)
    dyed = 1 - tk.smoothstep(0.3, 0.8, rings * fade)
    return tk.blur(np.clip(dyed, 0, 1), 3), _zero(size)


def batik(size, seed):
    """Batik: wax-resist blooms crazed with fine crackle lines."""
    first, second, _ = tk.worley(size, 4, seed, jitter=0.7)
    blooms = 1 - tk.smoothstep(0.3, 0.34, first)
    rings = np.exp(-((first - 0.2) / 0.02) ** 2) + np.exp(-((first - 0.09) / 0.02) ** 2)
    cf, cs, _ = tk.worley(size, 14, seed + 1)
    crackle = 1 - tk.smoothstep(0.0, 0.025, cs - cf)
    mottle = tk.value_noise(size, 6, seed + 2, 2)
    colour = np.clip(blooms * (1 - 0.5 * np.clip(rings, 0, 1)) + 0.4 * tk.smoothstep(0.6, 0.8, mottle) * (1 - blooms), 0, 1)
    return colour, np.clip(crackle * 0.45 + rings * blooms * 0.5, 0, 1)


def pixel_camo(size, seed):
    """Digital camouflage: blocky pixels in three tones."""
    blocks = 32
    n = tk.value_noise(blocks, 4, seed, octaves=3)
    levels = np.digitize(n, np.quantile(n, [0.4, 0.72])) / 2.0
    big = np.kron(levels, np.ones((size // blocks, size // blocks)))
    return big[:size, :size], _zero(size)


def watercolour(size, seed):
    """Watercolour washes: soft pools of colour with darker dried edges."""
    n = tk.value_noise(size, 4, seed, octaves=4, persistence=0.55)
    pool = tk.smoothstep(0.5, 0.56, n)
    edge = np.exp(-((n - 0.53) / 0.012) ** 2)
    second = tk.smoothstep(0.38, 0.42, tk.value_noise(size, 3, seed + 1, octaves=3))
    return np.clip(pool * (0.6 + 0.4 * tk.normalise(n)) + 0.4 * second * (1 - pool), 0, 1), edge * 0.35


# ------------------------------------------------------------------------------------------ knitted colourwork

def _chart(rows, size, width=24):
    """A knitting chart drawn stitch by stitch: ``rows`` of characters ('X' the pattern colour, 'o' half of it, '.' the
    ground), each repeated across to ``width`` stitches, the tile ``width`` stitches by ``len(rows)`` rows. Each stitch
    is a rounded block, a little shaded at its edges."""
    chart = np.array([[{"X": 1.0, "o": 0.5}.get(row[c % len(row)], 0.0) for c in range(width)] for row in rows])
    x, y = tk.grid(size)
    col = np.floor(x * width).astype(int) % width
    row = np.floor(y * len(rows)).astype(int) % len(rows)
    u, v = (x * width) % 1.0, (y * len(rows)) % 1.0
    stitch = np.clip(1 - ((u - 0.5) / 0.56) ** 4 - ((v - 0.5) / 0.62) ** 4, 0, 1) ** 0.3
    value = chart[row, col]
    return value * (0.8 + 0.2 * stitch), value * (1 - stitch) * 0.25


def fair_isle(size, seed):
    """Fair Isle: bands of little peerie motifs and a broad band of diamonds, knitted in."""
    rows = ["X", "X", ".", ".X..", "X.X.", ".X..", ".", "o", ".",
            ".....X......", "....XoX.....", "...XoooX....", "..XoXXXoX...", ".XoX...XoX..", "XoX..X..XoX.",
            ".XoX...XoX..", "..XoXXXoX...", "...XoooX....", "....XoX.....", ".....X......",
            ".", "o", ".", "..X.", ".XXX", "..X.", ".", "X", "X", ".", "X.....", "."]
    return _chart(rows, size)


def nordic(size, seed):
    """Nordic stars: eight-pointed snowflake stars between zigzag bands, as on a ski sweater."""
    rows = ["X", ".", "X...", ".X.X", "..X.", ".",
            ".....X......", "..X..X..X...", "...X.X.X....", "....XXX.....", ".X..XoX..X..", "XXXXXoXXXXX.",
            ".X..XoX..X..", "....XXX.....", "...X.X.X....", "..X..X..X...", ".....X......",
            ".", "..X.", ".X.X", "X...", ".", "X", ".", "...X........", "."]
    return _chart(rows, size)



def festive(size, seed):
    """A Christmas jumper: knitted fir trees and reindeer between snowflake bands."""
    tree = ["....X.......", "...XoX......", "..XoXoX.....", "...XoX......", "..XoXoX.....", ".XoXoXoX....",
            "XoXoXoXoX...", "....X.......", "....X.......", ".", "."]
    deer = ["X.X.........", "XXX.........", ".XX.........", ".XXX........", ".XXXXXXXX...", "..XXXXXXX...",
            "..XXXXXXX...", "..X.X..X.X..", "..X.X..X.X..", "..X.X..X.X..", "."]
    motif = [(t + "." * 12)[:12] + (d + "." * 12)[:12] for t, d in zip(tree, deer)]
    rows = ["X", ".", "..X...", ".XoX..", "..X...", ".", "o", "."] + motif + [".", "o", ".", "X.....", ".", "X"]
    return _chart(rows, size)


# -------------------------------------------------------------------------------------- ornaments and summer treats

def _mirror_shape(px, half):
    """A shape symmetric about its upright axis: ``half(ax, y)`` gives a signed distance (inside negative) for the
    right half, in [-1, 1] coordinates, with ax = |x|."""
    r = px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    return _soft(half(np.abs(x), y), r), r, x, y


def damask(size, seed):
    """Damask: mirrored ornaments of curling leaves round a central bud, framed by an ogee lattice (wallpaper florals)."""
    def ornament(px):
        def half(ax, y):
            bud = (np.hypot(ax / 0.2, (y + 0.08) / 0.38) - 1) * 0.2
            stem = np.maximum(ax - 0.05, np.abs(y - 0.45) - 0.28)
            leaf = (np.hypot((ax - 0.36) / 0.26, (y - 0.18) / 0.15) - 1) * 0.15
            curl = np.maximum(np.abs(np.hypot(ax - 0.44, y + 0.3) - 0.2) - 0.07, -(y + 0.3))
            tip = np.hypot(ax, y + 0.66) - 0.12
            return np.minimum.reduce([bud, stem, leaf, curl, tip])
        shape, r, x, y = _mirror_shape(px, half)
        vein = (np.abs(x) < 0.025) & (np.abs(y + 0.08) < 0.3)
        return shape, np.clip(_outline(shape, 1) * 0.5 + vein * 0.6, 0, 1)
    x, y = tk.grid(size)
    # The ogee lattice: two families of waving lines crossing into onion-domed cells round each ornament.
    a = np.abs(((x + 0.18 * np.sin(2 * np.pi * y) + 1.0) % 0.5) - 0.25)
    b = np.abs(((x - 0.18 * np.sin(2 * np.pi * y) + 1.0) % 0.5) - 0.25)
    lattice = np.clip((0.012 - np.minimum(a, b)) * size + 0.5, 0, 1)
    colour, ink = 0.5 * lattice, _zero(size)
    # One in each cell, large and small in turn.
    big, small = ornament(int(size / 2.8)), ornament(int(size / 4.2))
    for (cx, cy), (shape, lines) in zip(((0.25, 0.25), (0.75, 0.75), (0.75, 0.25), (0.25, 0.75)), (big, big, small, small)):
        tk.stamp(colour, shape, cx * size, cy * size)
        tk.stamp(ink, lines, cx * size, cy * size)
    return colour, ink


def fleur_de_lis(size, seed):
    """Fleurs-de-lis in offset rows (heraldic, preppy)."""
    def fleur(px):
        def half(ax, y):
            centre = np.maximum(np.hypot(ax / 0.14, (y + 0.15) / 0.55) - 1, y - 0.25) * 0.15
            side = np.abs(np.hypot(ax - 0.42, y + 0.05) - 0.3) - 0.08
            side = np.maximum(side, np.maximum(-ax + 0.12, y - 0.1))
            band = np.maximum(ax - 0.36, np.abs(y - 0.2) - 0.05)
            foot = np.maximum(np.hypot(ax - 0.18, y - 0.45) - 0.16, -(y - 0.3))
            return np.minimum.reduce([centre, side, band, foot])
        shape, r, x, y = _mirror_shape(px, half)
        return shape, _outline(shape, 1) * 0.4
    f = fleur(int(size / 4))
    x0 = _zero(size)
    colour, ink = x0.copy(), x0.copy()
    for row in range(3):
        for col in range(3):
            cx = (col + (0.5 if row % 2 else 0.0) + 0.25) / 3 * size
            cy = (row + 0.5) / 3 * size
            tk.stamp(colour, f[0], cx, cy)
            tk.stamp(ink, f[1], cx, cy)
    return colour, ink


def ice_creams(size, seed):
    """Ice-cream cones: a scoop or two on a criss-crossed cone."""
    def cone(px, scoops):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        wafer = (y > 0.0) & (np.abs(x) < 0.32 * (1 - y / 0.95)) & (y < 0.95)
        top = np.hypot(x, y + 0.05) < 0.34
        second = (np.hypot(x, y + 0.45) < 0.28) if scoops > 1 else np.zeros_like(top)
        drip = (np.abs(x + 0.12) < 0.05) & (y > 0.0) & (y < 0.15)
        scoop = (top | second | drip) & ~wafer
        grid = wafer & ((np.abs(np.sin((x + y) * 14)) < 0.25) | (np.abs(np.sin((x - y) * 14)) < 0.25))
        colour = tk.blur_small(scoop.astype(float), 0.8)
        cone_ = tk.blur_small(wafer.astype(float), 0.8)
        ink = np.clip(_outline(np.maximum(colour, cone_), 1) * 0.5 + grid * 0.5 + (top & second & (np.abs(y + 0.25) < 0.04)) * 0.6, 0, 1)
        return colour, ink, cone_
    return _scatter_shapes(size, [cone(int(size / 3.8), 1), cone(int(size / 3.4), 2)], 9, seed, size / 3.8, rotate=False)


def doughnuts(size, seed):
    """Iced doughnuts with sprinkles."""
    def ring(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        d = np.hypot(x, y)
        dough = (d < 0.85) & (d > 0.28)
        icing = (d < 0.72 + 0.06 * np.sin(np.arctan2(y, x) * 7)) & (d > 0.36)
        rng = np.random.default_rng(px)
        sprinkles = np.zeros_like(d, dtype=bool)
        for _ in range(14):
            a, rad, tilt = rng.random() * 2 * np.pi, 0.42 + 0.22 * rng.random(), rng.random() * np.pi
            sx, sy = np.cos(a) * rad, np.sin(a) * rad
            u = (x - sx) * np.cos(tilt) + (y - sy) * np.sin(tilt)
            v = -(x - sx) * np.sin(tilt) + (y - sy) * np.cos(tilt)
            sprinkles |= (np.abs(u) < 0.08) & (np.abs(v) < 0.025)
        ink = np.clip(_outline(dough.astype(float), 1) * 0.5 + sprinkles * icing * 0.8, 0, 1)
        return tk.blur_small(icing.astype(float), 0.7), ink, tk.blur_small((dough & ~icing).astype(float), 0.7)
    return _scatter_shapes(size, [ring(int(size / 4.2)), ring(int(size / 5.5))], 10, seed, size / 4)


def watermelons(size, seed):
    """Watermelon slices: red flesh, a rind and black seeds."""
    def slice_(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        d = np.hypot(x, y + 0.35)
        wedge = (d < 0.9) & (y + 0.35 > 0.0)
        flesh = wedge & (d < 0.74)
        seeds = flesh & (np.abs(np.sin(np.arctan2(y + 0.35, x) * 7)) < 0.18) & (np.abs(d - 0.45) < 0.06)
        ink = np.clip(seeds * 0.9 + _outline(wedge.astype(float), 1) * 0.5, 0, 1)
        return tk.blur_small(flesh.astype(float), 0.7), ink, tk.blur_small((wedge & ~flesh).astype(float), 0.7)
    return _scatter_shapes(size, [slice_(int(size / 3.8)), slice_(int(size / 5.2))], 10, seed, size / 4)



def shippo(size, seed):
    """Shippo (seven treasures): overlapping circles, where two overlap a pointed petal in the print colour."""
    x, y = tk.grid(size)
    n = 4
    u, v = x * n, y * n
    r = 1 / np.sqrt(2)
    inside = np.zeros((size, size))
    edge = np.zeros((size, size))
    for cx in (np.floor(u), np.floor(u) + 1):
        for cy in (np.floor(v), np.floor(v) + 1):
            d = np.hypot(u - cx, v - cy)
            inside += d < r
            edge = np.maximum(edge, np.clip((0.035 - np.abs(d - r)) * size / n + 0.5, 0, 1))
    petals = tk.blur_small((inside >= 2).astype(float), 0.8)
    return np.clip(petals * (1 - edge), 0, 1), edge * 0.5


def star_lattice(size, seed):
    """A star lattice (after asanoha, the hemp leaf): a triangular grid crossed by the lines through its corners and
    centres, a field of six-pointed stars. The triangles are 7 across and 8 rows to a tile (within 1% of equilateral),
    so every family of lines repeats with it."""
    x, y = tk.grid(size)
    cols, rows = 7, 8
    s_, t_ = x * cols - y * rows / 2, y * rows  # lattice coordinates: the corners are where both are whole
    lines = np.zeros((size, size))
    for c, (gx, gy) in ((t_, (0, rows)), (s_, (cols, -rows / 2)), (s_ + t_, (cols, rows / 2)),  # the grid
                        (s_ - t_, (cols, -1.5 * rows)), (s_ + 2 * t_, (cols, 1.5 * rows)), (2 * s_ + t_, (2 * cols, 0))):
        distance = np.abs(((c + 0.5) % 1.0) - 0.5) / np.hypot(gx, gy)  # in tiles
        lines = np.maximum(lines, np.clip((0.006 - distance) * size + 0.5, 0, 1))
    return lines, _zero(size)


def cacti(size, seed):
    """Potted cacti: green saguaros and round barrels in pots of the print colour."""
    def plant(px, barrel):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        pot = (np.abs(x) < 0.34 - 0.08 * (y - 0.55) / 0.4) & (y > 0.5) & (y < 0.92)
        rim = (np.abs(x) < 0.4) & (y > 0.44) & (y < 0.54)
        if barrel:
            body = np.hypot(x / 0.36, (y - 0.12) / 0.38) < 1
            ribs = body & (np.abs(np.sin(x * 14)) < 0.18)
        else:
            trunk = (np.abs(x) < 0.13) & (y > -0.8) & (y < 0.45) | (np.hypot(x, y + 0.8) < 0.13)
            arm_l = ((np.abs(x + 0.33) < 0.09) & (y > -0.45) & (y < 0.0)) | ((np.abs(y) < 0.09) & (x > -0.33) & (x < 0)) | (np.hypot(x + 0.33, y + 0.45) < 0.09)
            arm_r = ((np.abs(x - 0.3) < 0.09) & (y > -0.6) & (y < -0.15)) | ((np.abs(y + 0.15) < 0.09) & (x < 0.3) & (x > 0)) | (np.hypot(x - 0.3, y + 0.6) < 0.09)
            body = (trunk | arm_l | arm_r) & (y < 0.5)
            ribs = body & (np.abs(x) < 0.02) & (y < 0.4)
        pots = tk.blur_small((pot | rim).astype(float), 0.8)
        green = tk.blur_small((body & ~(pot | rim)).astype(float), 0.8)
        ink = np.clip(_outline(np.maximum(pots, green), 1) * 0.5 + ribs * 0.4 + _outline(rim.astype(float), 1) * 0.4, 0, 1)
        return pots, ink, green
    return _scatter_shapes(size, [plant(int(size / 3.6), False), plant(int(size / 4.5), True)], 9, seed, size / 3.8, rotate=False)


def feathers(size, seed):
    """Feathers: long vanes with a quill and barbs, drifting every which way (boho)."""
    def feather(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        width = 0.26 * np.sqrt(np.clip(1 - ((y + 0.1) / 0.85) ** 2, 0, 1)) * (1 - 0.3 * y)
        vane = np.abs(x) < width
        notch = (np.abs(x - 0.18) < 0.03) & (np.abs(y + 0.2) < 0.12)
        vane = tk.blur_small((vane & ~notch).astype(float), 0.8)
        quill = (np.abs(x) < 0.025) & (y > -0.85) & (y < 0.95)
        barbs = (np.abs(np.sin((y + np.abs(x) * 0.8) * 30)) < 0.2) * vane
        return vane, np.clip(quill * 0.9 + barbs * 0.35 + _outline(vane, 1) * 0.4, 0, 1)
    return _scatter_shapes(size, [feather(int(size / 2.8)), feather(int(size / 3.8))], 8, seed, size / 3.4)


def medallions(size, seed):
    """Medallions: mandala rosettes of petals and rings in two colours, in offset rows (boho, elegant)."""
    def rosette(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y), np.arctan2(y, x)
        outer = rad < 0.62 + 0.3 * np.abs(np.cos(ang * 6)) ** 1.5
        ring = np.abs(rad - 0.55) < 0.05
        inner = rad < 0.3 + 0.12 * np.abs(np.cos(ang * 4))
        dots = (np.abs(rad - 0.75) < 0.05) & (np.abs(np.sin(ang * 12)) < 0.3)
        colour = tk.blur_small((outer & ~ring & ~inner).astype(float), 0.8)
        accent = tk.blur_small(inner.astype(float), 0.8)
        ink = np.clip(_outline(outer.astype(float), 1) * 0.5 + ring * 0.6 + dots * 0.6 + _outline(accent, 1) * 0.5, 0, 1)
        return colour, ink, accent
    m = rosette(int(size / 2.4))
    colour, ink, accent = _zero(size), _zero(size), _zero(size)
    for cx, cy in ((0.25, 0.25), (0.75, 0.75)):
        cover = np.clip(np.maximum(m[0], m[2]), 0, 1)
        _stamp_over(colour, m[0], cover, cx * size, cy * size)
        _stamp_over(accent, m[2], cover, cx * size, cy * size)
        tk.stamp(ink, m[1], cx * size, cy * size)
    return colour, ink, accent



def foulard(size, seed):
    """Foulard: small four-petal flowers on a diagonal lattice with a dot between, as on a silk tie or a pyjama shirt."""
    x, y = tk.grid(size)
    k = 6
    u, v = (x + y) * k, (x - y) * k
    fu, fv = u % 1.0 - 0.5, v % 1.0 - 0.5
    rad, ang = np.hypot(fu, fv), np.arctan2(fv, fu)
    petals = (rad < 0.36 * np.abs(np.cos(2 * ang)) ** 0.7 + 0.04).astype(float)
    centre = (rad < 0.08).astype(float)
    cu, cv = np.minimum(u % 1.0, 1 - u % 1.0), np.minimum(v % 1.0, 1 - v % 1.0)
    between = (np.hypot(cu, cv) < 0.07).astype(float)
    colour = tk.blur_small(np.clip(petals - centre, 0, 1), 0.7)
    accent = tk.blur_small(np.maximum(centre, between), 0.7)
    return colour, _outline(petals, 1) * 0.35, accent


def toile(size, seed):
    """Toile de Jouy: pastoral scenes in one colour's engraved line art, a cottage under a tree and a bird on a branch,
    shaded with hatching, on the ground (a classic for a dress, pyjamas or a shirt)."""
    def scenes(d, n, solid):
        w = max(4, n // 200)
        thin = max(3, w - 1)

        def tree(cx, cy, s):
            if solid:
                for ox, oy, r in ((0, -0.8, 0.27), (-0.26, -0.64, 0.21), (0.27, -0.66, 0.2), (-0.14, -0.98, 0.2),
                                  (0.17, -0.97, 0.19), (-0.36, -0.86, 0.15), (0.36, -0.88, 0.14)):
                    d.ellipse([cx + (ox - r) * s, cy + (oy - r) * s, cx + (ox + r) * s, cy + (oy + r) * s], fill=255)
                d.polygon([(cx - s * 0.06, cy), (cx - s * 0.04, cy - s * 0.55), (cx + s * 0.04, cy - s * 0.55),
                           (cx + s * 0.07, cy)], fill=255)
            else:
                d.line([(cx - s * 0.04, cy - s * 0.5), (cx - s * 0.14, cy - s * 0.66)], fill=255, width=thin)
                d.line([(cx + s * 0.03, cy - s * 0.45), (cx + s * 0.16, cy - s * 0.62)], fill=255, width=thin)

        def cottage(cx, cy, s):
            if solid:
                d.rectangle([cx - s * 0.3, cy - s * 0.4, cx + s * 0.3, cy], fill=255)
                d.polygon([(cx - s * 0.4, cy - s * 0.38), (cx, cy - s * 0.74), (cx + s * 0.4, cy - s * 0.38)], fill=255)
                d.rectangle([cx + s * 0.12, cy - s * 0.7, cx + s * 0.21, cy - s * 0.5], fill=255)
            else:
                d.line([(cx - s * 0.3, cy - s * 0.4), (cx + s * 0.3, cy - s * 0.4)], fill=255, width=thin)  # eaves
                d.rectangle([cx - s * 0.07, cy - s * 0.24, cx + s * 0.07, cy], outline=255, width=thin)
                for wx in (-0.2, 0.17):
                    d.rectangle([cx + (wx - 0.055) * s, cy - s * 0.32, cx + (wx + 0.055) * s, cy - s * 0.2], outline=255, width=thin)
                    d.line([(cx + wx * s, cy - s * 0.32), (cx + wx * s, cy - s * 0.2)], fill=255, width=max(2, thin - 1))

        def ground(cx, cy, s):
            if solid:
                return
            d.arc([cx - s * 0.95, cy - s * 0.1, cx + s * 0.95, cy + s * 0.16], 190, 350, fill=255, width=w)
            for i in range(11):
                gx = cx + (-0.8 + 0.16 * i) * s
                d.line([(gx, cy + s * 0.01), (gx - s * 0.04, cy - s * 0.08)], fill=255, width=thin)
                d.line([(gx, cy + s * 0.01), (gx + s * 0.03, cy - s * 0.07)], fill=255, width=thin)
            for i in range(4):  # a fence
                fx = cx + (0.45 + 0.1 * i) * s
                d.line([(fx, cy - s * 0.02), (fx, cy - s * 0.16)], fill=255, width=thin)
            d.line([(cx + s * 0.42, cy - s * 0.11), (cx + s * 0.78, cy - s * 0.11)], fill=255, width=thin)

        def branch(cx, cy, s):
            if solid:
                for lx, angle in ((-0.52, 2.4), (-0.38, -2.2), (-0.22, 2.0), (0.28, 1.0), (0.44, -0.8), (0.6, 0.7)):
                    px, py = cx + lx * s, cy + (0.02 + 0.06 * lx * lx) * s  # a pointed leaf from the twig
                    tx, ty = np.cos(angle) * s * 0.17, np.sin(angle) * s * 0.17
                    d.polygon([(px, py), (px + tx * 0.5 - ty * 0.3, py + ty * 0.5 + tx * 0.3), (px + tx, py + ty),
                               (px + tx * 0.5 + ty * 0.3, py + ty * 0.5 - tx * 0.3)], fill=255)
                bx, by = cx + s * 0.02, cy - s * 0.14
                d.ellipse([bx - s * 0.2, by - s * 0.12, bx + s * 0.12, by + s * 0.08], fill=255)
                d.ellipse([bx + s * 0.04, by - s * 0.22, bx + s * 0.2, by - s * 0.06], fill=255)
                d.polygon([(bx + s * 0.19, by - s * 0.16), (bx + s * 0.29, by - s * 0.13), (bx + s * 0.19, by - s * 0.1)], fill=255)
                d.polygon([(bx - s * 0.16, by - s * 0.04), (bx - s * 0.42, by + s * 0.06), (bx - s * 0.38, by + s * 0.12),
                           (bx - s * 0.12, by + s * 0.04)], fill=255)
            else:
                twig = [(cx + t * s, cy + (0.02 + 0.06 * t * t) * s) for t in np.linspace(-0.62, 0.66, 12)]
                d.line(twig, fill=255, width=w, joint="curve")
                d.line([(cx + s * 0.3, cy + s * 0.07), (cx + s * 0.42, cy - s * 0.06)], fill=255, width=thin)
                bx, by = cx + s * 0.02, cy - s * 0.14
                d.arc([bx - s * 0.13, by - s * 0.09, bx + s * 0.07, by + s * 0.05], 160, 330, fill=255, width=thin)

        def flying(cx, cy, s):
            if not solid:
                d.arc([cx - s * 0.2, cy - s * 0.06, cx, cy + s * 0.1], 200, 340, fill=255, width=thin)
                d.arc([cx, cy - s * 0.06, cx + s * 0.2, cy + s * 0.1], 200, 340, fill=255, width=thin)

        def sprig(cx, cy, s):
            if solid:
                for t in (0.25, 0.5, 0.75, 1.0):
                    px, py = cx + s * 0.08 * t, cy - s * 0.4 * t
                    d.ellipse([px - s * 0.11, py - s * 0.04, px - s * 0.01, py + s * 0.02], fill=255)
                    d.ellipse([px + s * 0.01, py - s * 0.07, px + s * 0.11, py - s * 0.01], fill=255)
            else:
                d.line([(cx, cy), (cx + s * 0.09, cy - s * 0.44)], fill=255, width=thin)

        for dx in (-n, 0, n):
            for dy in (-n, 0, n):
                tree(dx + n * 0.2, dy + n * 0.47, n * 0.42)
                cottage(dx + n * 0.42, dy + n * 0.47, n * 0.26)
                ground(dx + n * 0.31, dy + n * 0.48, n * 0.3)
                branch(dx + n * 0.74, dy + n * 0.88, n * 0.36)
                flying(dx + n * 0.66, dy + n * 0.16, n * 0.2)
                flying(dx + n * 0.8, dy + n * 0.24, n * 0.15)
                sprig(dx + n * 0.86, dy + n * 0.6, n * 0.3)
                sprig(dx + n * 0.18, dy + n * 0.9, n * 0.26)
                sprig(dx + n * 0.5, dy + n * 0.72, n * 0.2)
    solid = _drawn(size, lambda d, n: scenes(d, n, True))
    lines = _drawn(size, lambda d, n: scenes(d, n, False))
    x, y = tk.grid(size)
    hatch = ((x - y) * 48 % 1.0 < 0.4).astype(float)
    lit = np.roll(solid, (size // 64, size // 64), axis=(0, 1))  # the shape moved down-right: its lower right is in shade
    shade = np.clip(solid - lit, 0, 1) + 0.25 * solid
    ink = np.clip(_outline(solid, 1) * 1.6 + lines * 1.3 + hatch * np.clip(shade, 0, 1) * 0.9, 0, 1)
    return ink, _zero(size)


def azulejo(size, seed):
    """Azulejo: painted tiles, a flower in a ring with petals to the corners, where quarter circles meet their
    neighbours' in a second colour, on white between thin grout lines (summer dresses, swimwear)."""
    x, y = tk.grid(size)
    k = 4
    fx, fy = (x * k) % 1.0 - 0.5, (y * k) % 1.0 - 0.5
    rad, ang = np.hypot(fx, fy), np.arctan2(fy, fx)
    flower = rad < 0.13 + 0.06 * np.cos(8 * ang)
    heart = rad < 0.05
    ring = np.abs(rad - 0.25) < 0.022
    corner = np.hypot(0.5 - np.abs(fx), 0.5 - np.abs(fy))
    quarter = corner < 0.15
    arc = np.abs(corner - 0.2) < 0.02
    petal = (rad > 0.29) & (np.abs(np.cos(2 * ang)) < 0.5 * np.clip((0.5 - rad) / 0.2, 0, 1)) & (corner > 0.24)
    edge = np.minimum(np.hypot(np.abs(fx) - 0.5, fy), np.hypot(fx, np.abs(fy) - 0.5))
    bud = edge < 0.07
    grout = (np.abs(fx) > 0.493) | (np.abs(fy) > 0.493)
    colour = tk.blur_small((flower & ~heart | ring | arc | petal | bud).astype(float), 0.7)
    accent = tk.blur_small((quarter | heart).astype(float), 0.7)
    return colour, grout * 0.35, accent


def whales(size, seed):
    """Whales: round cartoon whales swimming either way, a spout over the head and a pale belly in the second colour
    (pyjamas, kids' clothes, swimwear)."""
    spots = tk.scatter(size * 4, 7, seed, size * 4 / 3.2)

    def each(d, n, part):
        for k, (cx, cy) in enumerate(spots):
            s, f = n * (0.2 if k % 3 else 0.16), (1 if k % 2 else -1)

            def p(px, py):
                return (cx + f * px * s, cy + py * s)
            for dx in (-n, 0, n):
                for dy in (-n, 0, n):
                    cx, cy = spots[k][0] + dx, spots[k][1] + dy
                    if part == "body":
                        box = [p(-0.35, -0.3), p(0.5, 0.24)]
                        d.ellipse([min(box[0][0], box[1][0]), box[0][1], max(box[0][0], box[1][0]), box[1][1]], fill=255)
                        d.polygon([p(-0.2, -0.22), p(-0.66, -0.05), p(-0.66, 0.03), p(-0.2, 0.18)], fill=255)
                        d.polygon([p(-0.62, -0.02), p(-0.86, -0.22), p(-0.76, -0.01), p(-0.88, 0.12), p(-0.62, 0.03)], fill=255)
                    elif part == "belly":
                        box = [p(-0.3, 0.02), p(0.48, 0.23)]
                        d.ellipse([min(box[0][0], box[1][0]), box[0][1], max(box[0][0], box[1][0]), box[1][1]], fill=255)
                        for sx in (-0.12, 0.0, 0.12):  # the spout
                            end = p(0.18 + sx * 1.6, -0.62 + abs(sx))
                            d.line([p(0.18, -0.3), end], fill=255, width=max(3, int(s * 0.05)))
                            d.ellipse([end[0] - s * 0.04, end[1] - s * 0.04, end[0] + s * 0.04, end[1] + s * 0.04], fill=255)
                    else:
                        ex, ey = p(0.3, -0.06)
                        d.ellipse([ex - s * 0.035, ey - s * 0.035, ex + s * 0.035, ey + s * 0.035], fill=255)
                        d.arc([min(p(0.25, -0.02)[0], p(0.47, 0.1)[0]), p(0.25, -0.02)[1], max(p(0.25, -0.02)[0], p(0.47, 0.1)[0]),
                               p(0.47, 0.1)[1]], 20, 160, fill=255, width=max(2, int(s * 0.03)))
    body = _drawn(size, lambda d, n: each(d, n, "body"))
    extra = _drawn(size, lambda d, n: each(d, n, "belly"))
    face = _drawn(size, lambda d, n: each(d, n, "face"))
    spout = extra * (1 - tk.blur_small(body, 1) > 0.5)
    belly = extra * body
    accent = np.clip(belly + spout, 0, 1)
    return np.clip(body - belly, 0, 1), np.clip(_outline(body, 1) * 0.5 + face, 0, 1), accent


def celestial(size, seed):
    """Celestial: rayed suns and crescent moons with stars and sparkles round them in the second colour (boho dresses,
    pyjamas)."""
    big = tk.scatter(size * 4, 10, seed, size * 4 / 3.6)
    small = tk.scatter(size * 4, 26, seed + 1, size * 4 / 9)

    def star(d, x0, y0, r, points=5):
        pts = []
        for j in range(points * 2):
            a = np.pi * j / points - np.pi / 2
            rr = r if j % 2 == 0 else r * (0.42 if points == 5 else 0.25)
            pts.append((x0 + rr * np.cos(a), y0 + rr * np.sin(a)))
        d.polygon(pts, fill=255)

    def draw(d, n, part):
        for k, (cx, cy) in enumerate(big):
            kind = k % 3
            for dx in (-n, 0, n):
                for dy in (-n, 0, n):
                    x0, y0 = cx + dx, cy + dy
                    if kind == 0 and part == "sun":
                        r = n * 0.055
                        d.ellipse([x0 - r, y0 - r, x0 + r, y0 + r], fill=255)
                        for j in range(12):
                            a, b = 2 * np.pi * j / 12, np.pi / 12
                            reach = r * (2.0 if j % 2 == 0 else 1.6)
                            d.polygon([(x0 + r * 1.2 * np.cos(a - b * 0.6), y0 + r * 1.2 * np.sin(a - b * 0.6)),
                                       (x0 + reach * np.cos(a), y0 + reach * np.sin(a)),
                                       (x0 + r * 1.2 * np.cos(a + b * 0.6), y0 + r * 1.2 * np.sin(a + b * 0.6))], fill=255)
                    elif kind == 1 and part == "moon":
                        r = n * 0.075
                        d.ellipse([x0 - r, y0 - r, x0 + r, y0 + r], fill=255)
                        d.ellipse([x0 - r * 0.45, y0 - r * 1.1, x0 + r * 1.35, y0 + r * 0.7], fill=0)
                    elif kind == 2 and part == "stars":
                        star(d, x0, y0, n * 0.05)
                        star(d, x0 + n * 0.06, y0 - n * 0.05, n * 0.022)
        if part == "stars":
            for cx, cy in small:
                for dx in (-n, 0, n):
                    for dy in (-n, 0, n):
                        star(d, cx + dx, cy + dy, n * 0.018, points=4)
    sun = _drawn(size, lambda d, n: draw(d, n, "sun"))
    moon = _drawn(size, lambda d, n: draw(d, n, "moon"))
    stars = _drawn(size, lambda d, n: draw(d, n, "stars"))
    colour = np.clip(sun + moon, 0, 1)
    accent = stars * (1 - colour)
    return colour, _outline(colour, 1) * 0.45, accent


def clovers(size, seed):
    """Clovers: lucky four-leaf clovers, each leaf a heart, on a curving stem, scattered every which way, with the odd
    three-leaf one among them."""
    def clover(px, leaves):
        def leaf_fn(x, y):
            cover = np.zeros_like(x)
            for j in range(leaves):
                a = 2 * np.pi * j / leaves + np.pi / 4
                ux, uy = np.cos(a), np.sin(a)
                along, across = x * ux + y * uy, -x * uy + y * ux
                # a heart pointing in: two lobes side by side, tapering to the middle
                lobe = np.minimum(np.hypot(along - 0.42, across - 0.1), np.hypot(along - 0.42, across + 0.1))
                taper = (np.abs(across) < along * 0.42) & (along > 0.03) & (along < 0.44)
                cover = np.maximum(cover, np.maximum(_soft(lobe - 0.12, px / 2), taper.astype(float)))
            return cover
        leaf = _shape(px, leaf_fn)
        stem = _shape(px, lambda x, y: _soft(np.abs(x - 0.12 * (y - 0.1) ** 2) - 0.035, px / 2) * (y > 0.05) * (y < 0.9))
        colour = np.clip(leaf + stem, 0, 1)
        return colour, _outline(colour, 1) * 0.55
    shapes = [clover(int(size / 4.5), 4), clover(int(size / 5.2), 4), clover(int(size / 5.5), 3)]
    return _scatter_shapes(size, shapes, 12, seed, size / 4.6)


def dragonflies(size, seed):
    """Dragonflies: slender bodies with two pairs of veined wings in the second colour, darting every which way."""
    def dragonfly(px):
        def ellipse(x, y, cx, cy, rx, ry):
            return _soft(np.sqrt(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2) - 1, px / 10)
        def body_fn(x, y):
            segment = _soft(np.abs(x) - 0.045, px / 2) * (y > -0.55) * (y < 0.88)
            thorax = ellipse(x, y, 0, -0.42, 0.09, 0.16)
            head = ellipse(x, y, 0, -0.64, 0.1, 0.08)
            return np.maximum(np.maximum(segment, thorax), head)
        def wing_fn(x, y):
            return np.maximum(np.maximum(ellipse(x, y, 0.4, -0.46, 0.4, 0.1), ellipse(x, y, -0.4, -0.46, 0.4, 0.1)),
                              np.maximum(ellipse(x, y, 0.36, -0.26, 0.36, 0.09), ellipse(x, y, -0.36, -0.26, 0.36, 0.09)))
        body, wings = _shape(px, body_fn), _shape(px, wing_fn)
        wings = wings * (1 - body)
        veins = _shape(px, lambda x, y: (np.abs(((y + 0.46) * 0.6 + np.abs(x) * 0.05) % 0.12 - 0.06) < 0.008).astype(float)) * wings
        return body, np.clip(_outline(wings, 1) * 0.6 + _outline(body, 1) * 0.5 + veins * 0.25, 0, 1), wings
    shapes = [dragonfly(int(size / 4.2)), dragonfly(int(size / 5))]
    return _scatter_shapes(size, shapes, 9, seed, size / 4)


def avocados(size, seed):
    """Avocados: halves with a dark skin round the flesh and the stone in the second colour, tossed every which way."""
    def avocado(px):
        def pear(x, y, grow):
            bottom = np.hypot(x, y - 0.2) - (0.5 + grow)
            top = np.hypot(x, y + 0.32) - (0.32 + grow)
            k = 0.25  # a smooth union: the top and bottom run into one pear
            h = np.clip(0.5 + 0.5 * (top - bottom) / k, 0, 1)
            return top * (1 - h) + bottom * h - k * h * (1 - h)
        whole = _shape(px, lambda x, y: _soft(pear(x, y, 0.0), px / 2))
        flesh = _shape(px, lambda x, y: _soft(pear(x, y, -0.08), px / 2))
        stone = _shape(px, lambda x, y: _soft(np.hypot(x, y - 0.22) - 0.22, px / 2))
        return np.clip(flesh - stone, 0, 1), np.clip(whole - flesh + _outline(stone, 1) * 0.4, 0, 1), stone
    shapes = [avocado(int(size / 4.2)), avocado(int(size / 5))]
    return _scatter_shapes(size, shapes, 10, seed, size / 4.2)


def lollies(size, seed):
    """Ice lollies: rounded lollies on sticks, the top dipped in the second colour and a bite out of some (summer)."""
    def lolly(px, bitten):
        def box(x, y):  # a rounded rectangle's distance
            r = 0.22
            qx, qy = np.abs(x) - 0.3 + r, np.abs(y + 0.12) - 0.5 + r
            return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
        def body_fn(x, y):
            d = box(x, y)
            if bitten:
                d = np.maximum(d, -(np.hypot(x - 0.3, y + 0.58) - 0.16))
            return _soft(d, px / 2)
        body = _shape(px, body_fn)
        dip = body * _shape(px, lambda x, y: _soft(y + 0.25 + 0.04 * np.sin(x * 14), px / 2))
        stick = _shape(px, lambda x, y: _soft(np.maximum(np.abs(x) - 0.06, np.abs(y - 0.55) - 0.2), px / 2)) * (1 - body)
        return np.clip(body - dip, 0, 1), np.clip(_outline(body, 1) * 0.55 + stick * 0.35 + _outline(stick, 1) * 0.4, 0, 1), dip
    shapes = [lolly(int(size / 4.5), True), lolly(int(size / 4.8), False)]
    return _scatter_shapes(size, shapes, 10, seed, size / 4.3)


def bananas(size, seed):
    """Bananas: curved crescents with dark tips, tossed every which way (a fun summer shirt)."""
    def banana(px):
        def fn(x, y):
            outer = np.hypot(x, y + 0.55) - 1.05
            inner = np.hypot(x, y + 1.02) - 1.1
            return _soft(np.maximum(np.maximum(outer, -inner), np.abs(x) - 0.82), px / 2)
        body = _shape(px, fn)
        tips = body * _shape(px, lambda x, y: (np.abs(x) > 0.7).astype(float))
        return np.clip(body - tips, 0, 1), np.clip(_outline(body, 1) * 0.55 + tips * 0.8, 0, 1)
    shapes = [banana(int(size / 4)), banana(int(size / 4.6))]
    return _scatter_shapes(size, shapes, 10, seed, size / 4.2)


# ------------------------------------------------------------------------------- placed prints: chest graphics

def _graphic(size, layers):
    """A single motif (colour, ink and maybe accent arrays) centred on a clear tile: a print placed once (a T-shirt's
    chest graphic), not repeated."""
    out = [_zero(size) for _ in layers]
    for canvas, layer in zip(out, layers):
        tk.stamp(canvas, layer, size / 2, size / 2, mode="max")
    return tuple(out)


def _motif(size, scale=0.84):
    """(x, y) in [-1, 1] over a motif ``scale`` of the tile across, and its radius in pixels."""
    r = int(size * scale) // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    return x, y, r


def graphic_heart(size, seed):
    """A big heart with a shine."""
    x, y, r = _motif(size)
    cx, cy, rad, tip = 0.33, -0.22, 0.42, 0.78
    lobes = np.minimum(np.hypot(x + cx, y - cy) - rad, np.hypot(x - cx, y - cy) - rad)
    # The sides run from the tip on tangent to the lobes: where the tangent touches the right lobe.
    d = np.hypot(cx, tip - cy)
    heading = np.arctan2(cy - tip, cx) + np.arcsin(rad / d)  # from the tip, past the lobe's centre to its outer side
    reach = np.sqrt(d * d - rad * rad)
    touch = (reach * np.cos(heading), tip + reach * np.sin(heading))
    sides = tk.polygon_mask([(0, tip * r), (touch[0] * r, touch[1] * r), (cx * r, cy * r), (-cx * r, cy * r),
                             (-touch[0] * r, touch[1] * r)], 2 * r + 1, 1.0)
    heart = np.maximum(_soft(lobes, r), sides[:x.shape[0], :x.shape[1]])
    shine = _soft(np.hypot((x + 0.35) / 0.13, (y + 0.3) / 0.07) - 1, r * 0.1) * heart
    return _graphic(size, (np.clip(heart - shine, 0, 1), _outline(heart, 2) * 0.8, shine))


def graphic_star(size, seed):
    """A big five-pointed star, outlined twice."""
    x, y, r = _motif(size)
    a = np.arctan2(y, x) + np.pi / 2
    phi = np.abs(((a % (2 * np.pi / 5)) / (2 * np.pi / 5)) - 0.5) * 2
    rad = np.hypot(x, y)
    star = _soft(rad - (0.42 + 0.56 * phi ** 1.3), r)
    inner = _soft(rad - (0.3 + 0.4 * phi ** 1.3), r)
    return _graphic(size, (np.clip(star - inner, 0, 1), _outline(star, 2) * 0.7, inner))


def graphic_smiley(size, seed):
    """A smiley face."""
    x, y, r = _motif(size)
    face = _soft(np.hypot(x, y) - 0.9, r)
    eyes = (np.hypot((np.abs(x) - 0.3) / 0.09, (y + 0.25) / 0.19) < 1).astype(float)
    smile = ((np.abs(np.hypot(x, y + 0.05) - 0.55) < 0.07) & (y > 0.15)).astype(float)
    return _graphic(size, (face, np.clip(eyes + smile + _outline(face, 2) * 0.7, 0, 1)))


def graphic_cherries(size, seed):
    """A pair of cherries on their stems, with a leaf."""
    x, y, r = _motif(size)
    fruit = np.maximum(_soft(np.hypot(x + 0.36, y - 0.4) - 0.36, r), _soft(np.hypot(x - 0.3, y - 0.46) - 0.36, r))
    shine = np.maximum(_soft(np.hypot(x + 0.48, y - 0.28) - 0.07, r), _soft(np.hypot(x - 0.18, y - 0.34) - 0.07, r))
    stems = ((np.abs(x + 0.3 - (y + 0.5) * 0.08) < 0.03) & (y > -0.75) & (y < 0.08)) | \
            ((np.abs(x - 0.25 + (y + 0.6) * 0.25) < 0.03) & (y > -0.75) & (y < 0.12))
    leaf = _soft(np.hypot((x - 0.28) / 0.32, (y + 0.72) / 0.13) - 1, r * 0.15)
    return _graphic(size, (np.clip(fruit - shine, 0, 1), np.clip(_outline(fruit, 2) * 0.7 + stems * 0.9 + _outline(leaf, 2) * 0.5, 0, 1), leaf))


def graphic_rose(size, seed):
    """A single rose with leaves."""
    return _graphic(size, _rose(int(size * 0.86)))


def graphic_sunset(size, seed):
    """A retro sunset: a sun, its lower half sliced by widening bands, over the sea."""
    x, y, r = _motif(size)
    sun = _soft(np.hypot(x, y) - 0.85, r)
    v = np.clip(y, 0, 1)
    gaps = (y > 0.05) & (((y - 0.05) * 6) % 1.0 < 0.15 + 0.5 * v)
    upper = sun * (y <= 0.05)
    lower = sun * (y > 0.05) * (1 - gaps)
    return _graphic(size, (lower, _outline(sun, 2) * 0.4, upper))


def graphic_lightning(size, seed):
    """A lightning bolt."""
    px = int(size * 0.84) | 1
    pts = [(-0.3, -0.92), (0.38, -0.92), (0.06, -0.14), (0.46, -0.14), (-0.36, 0.95), (-0.06, 0.1), (-0.46, 0.1)]
    bolt = tk.polygon_mask([(p[0] * px / 2, p[1] * px / 2) for p in pts], px, 1.5)
    return _graphic(size, (bolt, _outline(bolt, 2) * 0.8))


def graphic_skull(size, seed):
    """A skull."""
    x, y, r = _motif(size)
    head = _soft(np.hypot(x, (y + 0.15) * 1.05) - 0.66, r)
    jaw = _soft(np.maximum(np.abs(x) - 0.34, np.abs(y - 0.52) - 0.24), r)
    skull = np.clip(head + jaw, 0, 1)
    eyes = np.maximum(_soft(np.hypot(x + 0.27, y + 0.05) - 0.17, r), _soft(np.hypot(x - 0.27, y + 0.05) - 0.17, r))
    nose = _soft(np.maximum(np.abs(x) * 1.6 - (y - 0.32) * -1.0 - 0.1, np.abs(y - 0.26) - 0.08), r)
    teeth = ((np.abs(np.sin(x * 14)) < 0.25) & (np.abs(y - 0.58) < 0.13) & (np.abs(x) < 0.3)).astype(float)
    return _graphic(size, (np.clip(skull - eyes - nose, 0, 1), np.clip(_outline(skull, 2) * 0.8 + eyes + nose + teeth * 0.8, 0, 1)))


def graphic_cat(size, seed):
    """A cat's face, its ears pale inside."""
    x, y, r = _motif(size)
    head = np.hypot(x / 0.74, (y - 0.14) / 0.62) < 1
    ears = (np.abs(x) > 0.18) & (np.abs(x) < 0.7) & (y < -0.3) & (y > -0.9) & (y > -0.9 + np.abs(np.abs(x) - 0.48) * 2.7)
    inner = (np.abs(x) > 0.3) & (np.abs(x) < 0.6) & (y < -0.38) & (y > -0.72) & (y > -0.72 + np.abs(np.abs(x) - 0.47) * 3.2)
    shape = tk.blur_small((head | ears).astype(float), 1)
    eyes = np.hypot((np.abs(x) - 0.3) / 0.08, (y - 0.06) / 0.12) < 1
    nose = (np.abs(x) < 0.09 - (y - 0.26) * 0.7) & (y > 0.26) & (y < 0.36)
    whiskers = ((np.abs(y - 0.33 - (np.abs(x) - 0.42) * 0.15) < 0.02) | (np.abs(y - 0.42 + (np.abs(x) - 0.42) * 0.1) < 0.02)) & (np.abs(x) > 0.42) & (np.abs(x) < 0.98)
    pale = tk.blur_small(inner.astype(float), 1)
    return _graphic(size, (np.clip(shape - pale, 0, 1), np.clip(_outline(shape, 2) * 0.7 + eyes + nose + whiskers * 0.8, 0, 1), pale))


def graphic_rainbow(size, seed):
    """A rainbow over two clouds."""
    x, y, r = _motif(size)
    d = np.hypot(x, y - 0.3)
    bands = (d < 0.95) & (d > 0.35) & (y < 0.3)
    even = np.floor((d - 0.35) / 0.15) % 2 == 0
    clouds = np.clip(sum(_soft(np.hypot(x - sx, y - sy) - sr, r) for sx, sy, sr in
                         ((-0.8, 0.32, 0.2), (-0.58, 0.3, 0.24), (0.58, 0.3, 0.24), (0.8, 0.32, 0.2))), 0, 1)
    ink = np.clip(_outline(bands.astype(float), 2) * 0.4 + _outline(clouds, 2) * 0.7, 0, 1)
    return _graphic(size, ((bands & even) * (1 - clouds), ink, (bands & ~even) * (1 - clouds)))


def graphic_planet(size, seed):
    """A ringed planet and a few stars."""
    x, y, r = _motif(size)
    ball = np.hypot(x, y) < 0.5
    t = np.radians(-20)
    rx, ry = np.cos(t) * x + np.sin(t) * y, -np.sin(t) * x + np.cos(t) * y
    e = np.hypot(rx / 0.95, ry / 0.26)
    ring = (e < 1) & (e > 0.8) & ~(ball & (ry < 0))
    stars_ = sum((np.hypot(x - sx, y - sy) < 0.05) for sx, sy in ((-0.75, -0.7), (0.7, -0.6), (0.8, 0.65), (-0.6, 0.75)))
    body = tk.blur_small(ball.astype(float), 1)
    band = tk.blur_small(ring.astype(float), 1)
    return _graphic(size, (np.clip(body - band + 0.6 * stars_, 0, 1), np.clip(_outline(body, 2) * 0.6 + _outline(band, 2) * 0.6, 0, 1), band))


def graphic_daisy(size, seed):
    """One big daisy with a golden heart."""
    b, i = _petal_flower(int(size * 0.86), 12, 0.15)
    r = b.shape[0] // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    centre = np.clip((r * 0.24 - np.hypot(x, y)) / 1.2 + 0.5, 0, 1)
    return _graphic(size, (np.clip(b - centre, 0, 1), np.clip(i * 0.7 + _outline(centre, 2) * 0.5, 0, 1), centre))


def graphic_mountains(size, seed):
    """An outdoors badge: snow-capped mountains under the sun, in a ring."""
    x, y, r = _motif(size)
    ring = (np.abs(np.hypot(x, y) - 0.88) < 0.06).astype(float)
    inside = np.hypot(x, y) < 0.8
    peak1 = (y > -0.35 + np.abs(x + 0.2) * 1.3) & (y < 0.45)
    peak2 = (y > -0.1 + np.abs(x - 0.35) * 1.1) & (y < 0.45)
    hills = (peak1 | peak2) & inside
    snow = hills & (((y < -0.18) & peak1) | ((y < 0.05) & peak2 & ~peak1))
    sun = np.hypot(x - 0.38, y + 0.42) < 0.16
    colour = np.clip(ring + tk.blur_small((hills & ~snow).astype(float), 1) + sun * inside, 0, 1)
    ink = np.clip(_outline(tk.blur_small(hills.astype(float), 1), 2) * 0.6 + (np.abs(y - 0.45) < 0.02) * inside, 0, 1)
    return _graphic(size, (colour, ink, tk.blur_small(snow.astype(float), 1)))


def graphic_paw(size, seed):
    """A big paw print."""
    x, y, r = _motif(size)
    pad = _soft(np.hypot(x * 1.05, (y - 0.32) * 1.25) - 0.42, r)
    toes = sum(_soft(np.hypot(x - tx, y - ty) - 0.18, r) for tx, ty in ((-0.6, -0.15), (-0.22, -0.55), (0.22, -0.55), (0.6, -0.15)))
    paw = np.clip(pad + toes, 0, 1)
    return _graphic(size, (paw, _outline(paw, 2) * 0.6))



def graphic_butterfly(size, seed):
    """A butterfly, its wings spotted in the second colour."""
    x, y, r = _motif(size)
    ax = np.abs(x)
    upper = np.hypot((ax - 0.45) / 0.5, (y + 0.22) / 0.42) < 1
    lower = np.hypot((ax - 0.32) / 0.34, (y - 0.4) / 0.32) < 1
    wings = tk.blur_small(((upper | lower) & (ax > 0.07)).astype(float), 1)
    spots = tk.blur_small(((np.hypot(ax - 0.55, y + 0.3) < 0.14) | (np.hypot(ax - 0.33, y - 0.42) < 0.1)).astype(float), 1) * wings
    body = (ax < 0.07) & (np.abs(y) < 0.6)
    antennae = (np.abs(ax - (-y - 0.55) * 0.6) < 0.03) & (y < -0.55) & (y > -0.9)
    return _graphic(size, (np.clip(wings - spots, 0, 1), np.clip(_outline(wings, 2) * 0.7 + body + antennae, 0, 1), spots))


def graphic_bow(size, seed):
    """A big bow."""
    x, y, r = _motif(size)
    loops = np.minimum(np.hypot((x + 0.45), y * 1.6) - 0.44, np.hypot((x - 0.45), y * 1.6) - 0.44)
    knot = np.hypot(x, y) - 0.17
    tails = np.where((y > 0.0) & (np.abs(np.abs(x) - y * 0.6) < 0.12) & (y < 0.85), -1.0, 1.0)
    bow = _soft(np.minimum.reduce([loops, knot, tails]), r)
    inner = _soft(np.minimum(np.hypot((x + 0.5), y * 1.6) - 0.22, np.hypot((x - 0.5), y * 1.6) - 0.22), r) * bow
    return _graphic(size, (np.clip(bow - inner * 0.6, 0, 1), np.clip(_outline(bow, 2) * 0.7 + _outline(_soft(knot, r), 2) * 0.6, 0, 1)))


def graphic_moon(size, seed):
    """A crescent moon among golden stars."""
    x, y, r = _motif(size)
    moon = np.clip(_soft(np.hypot(x + 0.1, y) - 0.72, r) - _soft(np.hypot(x - 0.22, y - 0.15) - 0.62, r), 0, 1)
    stars_ = np.zeros_like(x)
    for sx, sy, ss in ((0.55, -0.55, 0.16), (0.7, 0.35, 0.1), (0.3, 0.7, 0.08)):
        a = np.arctan2(y - sy, x - sx) + np.pi / 2
        phi = np.abs(((a % (2 * np.pi / 5)) / (2 * np.pi / 5)) - 0.5) * 2
        stars_ = np.maximum(stars_, _soft(np.hypot(x - sx, y - sy) - ss * (0.45 + 0.55 * phi ** 1.3), r))
    return _graphic(size, (moon, _outline(moon, 2) * 0.6, stars_))


def graphic_crown(size, seed):
    """A crown set with jewels."""
    px = int(size * 0.84) | 1
    h = px / 2
    pts = [(-0.8, 0.45), (-0.85, -0.45), (-0.45, -0.05), (-0.22, -0.6), (0.0, -0.1), (0.22, -0.6), (0.45, -0.05),
           (0.85, -0.45), (0.8, 0.45)]
    crown = tk.polygon_mask([(a * h, b * h) for a, b in pts], px, 1.5)
    x, y, r = _motif(size)
    x, y = x[:crown.shape[0], :crown.shape[1]], y[:crown.shape[0], :crown.shape[1]]
    jewels = np.clip(sum(_soft(np.hypot(x - jx, y - jy) - js, r) for jx, jy, js in ((0, 0.22, 0.1), (-0.45, 0.25, 0.07), (0.45, 0.25, 0.07))), 0, 1)
    band = (np.abs(y - 0.25) < 0.17) & (np.abs(x) < 0.8)
    return _graphic(size, (np.clip(crown - jewels, 0, 1), np.clip(_outline(crown, 2) * 0.7 + _outline(band.astype(float), 1) * 0.4, 0, 1), jewels))


def graphic_ghost(size, seed):
    """A little ghost, blushing."""
    x, y, r = _motif(size)
    body = (np.hypot(x / 0.62, (y + 0.15) / 0.62) < 1) | ((np.abs(x) < 0.62) & (y > -0.15) & (y < 0.7 + 0.12 * np.cos(x * 12)))
    ghost = tk.blur_small(body.astype(float), 1)
    eyes = np.hypot((np.abs(x) - 0.22) / 0.08, (y + 0.15) / 0.12) < 1
    mouth = np.hypot(x / 0.1, (y - 0.12) / 0.08) < 1
    blush = tk.blur_small((np.hypot((np.abs(x) - 0.38) / 0.12, (y - 0.02) / 0.06) < 1).astype(float), 1)
    return _graphic(size, (np.clip(ghost - blush, 0, 1), np.clip(_outline(ghost, 2) * 0.7 + eyes + mouth, 0, 1), blush))


def graphic_palm(size, seed):
    """A palm tree on an island under the sun."""
    x, y, r = _motif(size)
    sun = _soft(np.hypot(x, y + 0.1) - 0.75, r)
    trunk = (np.abs(x - 0.15 * (y - 0.75) ** 2 + 0.05) < 0.05 + 0.02 * (y + 0.3)) & (y > -0.3) & (y < 0.7)
    fronds = np.zeros_like(x, dtype=bool)
    for k in range(5):
        a = np.radians(-160 + k * 35)
        cx, cy = 0.05 + 0.32 * np.cos(a), -0.32 + 0.32 * np.sin(a) + 0.12
        fronds |= np.hypot(((x - cx) * np.cos(a) + (y - cy) * np.sin(a)) / 0.34, (-(x - cx) * np.sin(a) + (y - cy) * np.cos(a)) / 0.08) < 1
    island = (np.hypot(x / 0.6, (y - 0.75) / 0.12) < 1)
    leaf = tk.blur_small(fronds.astype(float), 1)
    tree = tk.blur_small((trunk | island).astype(float), 1)
    return _graphic(size, (np.clip(sun - tree - leaf, 0, 1) * 0.5 + tree * (1 - leaf), np.clip(_outline(sun, 2) * 0.4 + _outline(leaf, 2) * 0.6, 0, 1), leaf))


def graphic_anchor(size, seed):
    """An anchor with a coiled rope."""
    x, y, r = _motif(size)
    ring = np.abs(np.hypot(x, y + 0.72) - 0.14) < 0.055
    shank = (np.abs(x) < 0.07) & (y > -0.6) & (y < 0.72)
    stock = (np.abs(y + 0.4) < 0.06) & (np.abs(x) < 0.4)
    arc = (np.abs(np.hypot(x, y - 0.1) - 0.62) < 0.08) & (y > 0.32)
    tips = np.hypot(np.abs(x) - 0.6, y - 0.4) < 0.13
    anchor = tk.blur_small((ring | shank | stock | arc | tips).astype(float), 1)
    rope = (np.abs(np.hypot(x * 1.2, y * 0.9) - 0.7 + 0.03 * np.sin(np.arctan2(y, x) * 18)) < 0.03) & (y < 0.2)
    return _graphic(size, (anchor, _outline(anchor, 2) * 0.6, tk.blur_small(rope.astype(float), 1) * (1 - anchor)))


def graphic_strawberry(size, seed):
    """A strawberry with a leafy top."""
    x, y, r = _motif(size)
    half_width = np.where(y < 0.82, 0.68 * np.sqrt(np.clip(1 - ((y + 0.05) / 0.9) ** 2, 0, 1)) * (1 - 0.25 * (y + 0.05)), 0)
    body = (np.abs(x) < half_width) & (y > -0.5)
    seeds = (np.sin(x * 14) * np.sin((y + x * 0.5) * 14) > 0.9) & body
    top = (np.abs(x) < 0.04) & (y < -0.45) & (y > -0.85)  # the stalk, and five pointed sepals round it
    for k in range(5):
        a = np.radians(200 + k * 35)
        cx, cy = 0.26 * np.cos(a), -0.48 + 0.2 * np.sin(a) + 0.12
        top |= np.hypot(((x - cx) * np.cos(a) + (y - cy) * np.sin(a)) / 0.26, (-(x - cx) * np.sin(a) + (y - cy) * np.cos(a)) / 0.08) < 1
    berry = tk.blur_small(body.astype(float), 1)
    leaf = tk.blur_small(top.astype(float), 1)
    return _graphic(size, (np.clip(berry - leaf, 0, 1), np.clip(_outline(berry, 2) * 0.6 + seeds * 0.9 + _outline(leaf, 2) * 0.5, 0, 1), leaf))


def graphic_toadstool(size, seed):
    """A spotted toadstool."""
    x, y, r = _motif(size)
    cap = _soft(np.hypot(x / 0.88, (y + 0.05) / 0.7) - 1, r * 0.7) * (y < 0.05)
    stem = _soft(np.maximum(np.abs(x) - 0.26 - 0.06 * (y - 0.05), np.abs(y - 0.45) - 0.4), r) * (1 - cap)
    spots = np.clip(sum(_soft(np.hypot(x - sx, y - sy) - sr, r) for sx, sy, sr in ((-0.4, -0.25, 0.13), (0.25, -0.4, 0.11), (0.5, -0.1, 0.09), (-0.05, -0.1, 0.08))), 0, 1) * cap
    return _graphic(size, (np.clip(cap - spots, 0, 1), np.clip(_outline(cap, 2) * 0.7 + _outline(stem, 2) * 0.6, 0, 1), np.clip(stem + spots, 0, 1)))


def graphic_flame(size, seed):
    """A flame with a golden heart."""
    x, y, r = _motif(size)
    def flame(scale, lean):
        u, v = x / scale, (y - 0.25) / scale
        width = 0.55 * np.sqrt(np.clip(1 - (v / 0.65) ** 2, 0, 1)) * (v > 0) + np.clip(0.55 + v * 0.55 + 0.08 * np.sin(v * 9 + lean), 0, 1) * (v <= 0)
        return (np.abs(u - lean * np.clip(-v, 0, 1) * 0.3) < width) & (v > -1.0) & (v < 0.65)
    outer = tk.blur_small(flame(1.0, 0.6).astype(float), 1)
    inner = tk.blur_small(flame(0.55, -0.4).astype(float), 1) * outer
    return _graphic(size, (np.clip(outer - inner, 0, 1), _outline(outer, 2) * 0.6, inner))


def graphic_lips(size, seed):
    """A big kiss."""
    x, y, r = _motif(size)
    outer = np.hypot(x / 0.88, y / 0.48) < 1
    bow = np.hypot(x, y + 0.55) < 0.2
    lips = tk.blur_small((outer & ~bow).astype(float), 1)
    mouth = (np.abs(y - 0.03 - 0.12 * (x / 0.88) ** 2 + 0.1 * np.exp(-(x / 0.12) ** 2)) < 0.035) & (np.abs(x) < 0.85)
    shine = tk.blur_small((np.hypot((x + 0.35) / 0.15, (y - 0.25) / 0.05) < 1).astype(float), 1) * lips
    return _graphic(size, (np.clip(lips - shine, 0, 1), np.clip(mouth * 0.9 + _outline(lips, 2) * 0.5, 0, 1), shine))


def graphic_notes(size, seed):
    """A pair of beamed quavers."""
    x, y, r = _motif(size)
    def head(cx, cy):
        t = np.radians(-25)
        lx, ly = x - cx, y - cy
        return np.hypot((np.cos(t) * lx + np.sin(t) * ly) / 0.24, (-np.sin(t) * lx + np.cos(t) * ly) / 0.16) < 1
    notes = head(-0.42, 0.55) | head(0.42, 0.42) | ((np.abs(x + 0.22) < 0.045) & (y > -0.58) & (y < 0.55)) | \
        ((np.abs(x - 0.62) < 0.045) & (y > -0.72) & (y < 0.42)) | ((np.abs(y + 0.62 + (x + 0.22) * 0.17) < 0.09) & (x > -0.26) & (x < 0.66))
    shape = tk.blur_small(notes.astype(float), 1)
    return _graphic(size, (shape, _outline(shape, 2) * 0.5))


def graphic_eye(size, seed):
    """An eye with lashes and a coloured iris."""
    x, y, r = _motif(size)
    almond = np.maximum(np.hypot(x, y - 0.5) - 0.82, np.hypot(x, y + 0.5) - 0.82)
    white = _soft(almond, r)
    iris = _soft(np.hypot(x, y) - 0.28, r) * white
    pupil = _soft(np.hypot(x, y) - 0.12, r)
    lashes = (np.abs(np.sin(np.arctan2(y + 0.5, x) * 9)) < 0.22) & (np.hypot(x, y + 0.5) > 0.82) & (np.hypot(x, y + 0.5) < 0.98) & (y < -0.1)
    return _graphic(size, (np.clip(white - iris, 0, 1), np.clip(_outline(white, 2) * 0.8 + pupil + lashes * 0.8, 0, 1), np.clip(iris - pupil, 0, 1)))



# ------------------------------------------------------------- dyes over the whole garment (PLACED "body")
# Drawn top to bottom over the garment's own height: row 0 is its top, the last row its hem. They vary only down the
# tile, which is stretched round every panel and sleeve.

def _down(size):
    """How far down the garment each row is, 0 at the top to 1 at the hem, flat for the first and last few rows (the
    tile's ends meet the garment's)."""
    y = np.clip((np.arange(size)[:, None] + 0.5) / size * 1.04 - 0.02, 0, 1)
    return np.repeat(y, size, axis=1)


def ombre(size, seed):
    """Ombre: the cloth's colour at the top fading to the print colour at the hem."""
    t = _down(size)
    return tk.smoothstep(0.1, 0.95, t), _zero(size)


def ombre_up(size, seed):
    """A reverse ombre: the print colour at the top fading to the cloth's own at the hem."""
    return 1 - tk.smoothstep(0.05, 0.9, _down(size)), _zero(size)


def dip_dye(size, seed):
    """Dip-dye: the lower third dipped in the print colour, a soft wicked edge above a darker tide line."""
    t = _down(size)
    dyed = tk.smoothstep(0.58, 0.68, t)
    tide = np.exp(-((t - 0.665) / 0.012) ** 2)
    return dyed, tide * 0.35


def sunset_fade(size, seed):
    """A sunset fade: the second colour at the top, through the cloth's own, to the print colour at the hem."""
    t = _down(size)
    return tk.smoothstep(0.45, 0.95, t), _zero(size), 1 - tk.smoothstep(0.05, 0.5, t)


def colour_block(size, seed):
    """Colour block: the lower half in the print colour, a band of the second colour where they meet."""
    t = _down(size)
    lower = tk.smoothstep(0.545, 0.555, t)
    band = tk.smoothstep(0.495, 0.505, t) * (1 - lower)
    return lower, _zero(size), band


def hem_stripes(size, seed):
    """Hem stripes: a broad stripe and a narrow one near the hem, as on a tennis dress or a sailor top."""
    t = _down(size)
    broad = tk.smoothstep(0.86, 0.865, t) * (1 - tk.smoothstep(0.915, 0.92, t))
    narrow = tk.smoothstep(0.94, 0.945, t) * (1 - tk.smoothstep(0.96, 0.965, t))
    return np.maximum(broad, narrow), _zero(size)



# ------------------------------------------------- borders: bands round the hem or under the neckline
# A band's tile repeats round the garment (across) and is placed once up it (down the tile: row 0 the band's top);
# its first and last rows stay clear, so it meets the cloth cleanly.

def _band_grid(size):
    x, y = tk.grid(size)
    return x, (y - 0.5) * 2  # y in -1 (the band's top) .. 1 (its bottom)


def hem_flowers(size, seed):
    """A border of flowers along a wavy vine, leaves between."""
    x, y = _band_grid(size)
    vine = np.abs(y - 0.25 * np.sin(2 * np.pi * x)) < 0.05
    colour, ink, accent = _zero(size), _zero(size), _zero(size)
    flower, flower_ink = _petal_flower(int(size * 0.42), 5, 0.5)
    leaf, leaf_ink = _leaf_shape(int(size * 0.26))
    tk.stamp(colour, flower, size * 0.25, size * (0.5 + 0.25 * 0.5 * np.sin(np.pi / 2)) - 0 * size)
    tk.stamp(ink, flower_ink, size * 0.25, size * 0.5 + size * 0.0625)
    tk.stamp(colour, flower * 0.6, size * 0.75, size * 0.5 - size * 0.0625)
    tk.stamp(ink, flower_ink, size * 0.75, size * 0.5 - size * 0.0625)
    for cx, angle in ((0.05, 60), (0.48, -60), (0.55, 120), (0.98, -120)):
        rotated, rotated_ink = tk.rotate_shape(leaf, angle), tk.rotate_shape(leaf_ink, angle)
        tk.stamp(accent, rotated, cx * size, size * (0.5 + 0.125 * np.sin(2 * np.pi * cx)))
        tk.stamp(ink, rotated_ink * 0.5, cx * size, size * (0.5 + 0.125 * np.sin(2 * np.pi * cx)))
    accent = np.clip(np.maximum(accent, tk.blur_small(vine.astype(float), 0.8)) * (1 - colour), 0, 1)
    return colour, ink, accent


def scallop_edge(size, seed):
    """A scalloped lace edge: a row of round scallops, each pierced with eyelets, above a fine line."""
    x, y = _band_grid(size)
    u = (x * 2) % 1.0 - 0.5
    # u and y are in the same units (half a tile across is one, the band's height is two).
    scallop = (np.hypot(u, y + 0.3) < 0.5) & (y > -0.3)
    top = (y > -0.5) & (y <= -0.3)
    holes = np.hypot(u, y + 0.02) < 0.1  # an eyelet in each scallop, a ring of pinholes round it
    rad, ang = np.hypot(u, y + 0.3), np.arctan2(y + 0.3, u)
    holes |= (np.abs(rad - 0.38) < 0.035) & (np.abs(np.sin(ang * 6)) < 0.3) & (y > -0.25)
    shape = tk.blur_small(((scallop | top) & ~holes).astype(float), 0.8)
    return shape, np.clip(_outline(shape, 1) * 0.5, 0, 1)


def greek_key(size, seed):
    """A Greek key (meander) between two rules."""
    x, y = _band_grid(size)
    n = 2
    u = (x * n) % 1.0  # one key per half tile, on a 6 x 6 grid of steps
    gx, gy = np.floor(u * 6), np.floor((y + 0.6) / 1.2 * 6)
    inside = (y > -0.6) & (y < 0.6)
    path = np.zeros_like(x, dtype=bool)
    cells = {(0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5), (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (1, 0), (2, 0), (3, 0),
             (4, 0), (4, 1), (4, 2), (4, 3), (3, 3), (2, 3), (2, 2)}
    for cx, cy in cells:
        path |= (gx == cx) & (gy == cy)
    rules = (np.abs(y + 0.8) < 0.07) | (np.abs(y - 0.8) < 0.07)
    return tk.blur_small(((path & inside) | rules).astype(float), 0.8), _zero(size)


def folk_band(size, seed):
    """Folk embroidery: a band of diamonds with dotted hearts between zigzags and rules, in two colours."""
    x, y = _band_grid(size)
    u = (x * 2) % 1.0 - 0.5
    rules = (np.abs(np.abs(y) - 0.88) < 0.04)
    zig = np.abs(np.abs(y) - (0.7 - 0.08 * np.abs(((x * 8) % 1.0) - 0.5) * 4)) < 0.035
    diamond = np.abs(u) * 2.2 + np.abs(y) * 1.4 < 0.75
    inner = np.abs(u) * 2.2 + np.abs(y) * 1.4 < 0.4
    dots = np.hypot(np.abs(u) - 0.5, y * 0.5) < 0.06
    stitches = (np.floor(x * 96) % 2 == 0)
    colour = tk.blur_small(((rules | zig) & stitches | (diamond & ~inner)).astype(float), 0.8)
    accent = tk.blur_small((inner | dots).astype(float), 0.8)
    return colour, _zero(size), accent


def ric_rac(size, seed):
    """Ric-rac trims: two wavy braids, the second in the second colour."""
    x, y = _band_grid(size)
    first = np.abs(y + 0.35 - 0.22 * np.sin(2 * np.pi * x * 4)) < 0.13
    second = np.abs(y - 0.35 - 0.22 * np.sin(2 * np.pi * x * 4 + np.pi)) < 0.13
    return tk.blur_small(first.astype(float), 0.8), _zero(size), tk.blur_small(second.astype(float), 0.8)


def yoke_flowers(size, seed):
    """Embroidered flowers under the neckline, as on a peasant blouse: a row of small blooms on stems, dots between."""
    x, y = _band_grid(size)
    colour, ink, accent = _zero(size), _zero(size), _zero(size)
    flower, flower_ink = _petal_flower(int(size * 0.32), 6, 0.4)
    for cx in (0.25, 0.75):
        tk.stamp(colour, flower, cx * size, size * 0.4)
        tk.stamp(ink, flower_ink * 0.6, cx * size, size * 0.4)
    stems = np.zeros_like(x, dtype=bool)
    for cx in (0.25, 0.75):
        stems |= (np.abs(x - cx) < 0.012) & (y > 0.1) & (y < 0.8)
        for side in (-1, 1):
            stems |= np.hypot((x - cx - side * 0.06) / 0.05, (y - 0.5) / 0.08) < 1
    dots = (np.hypot(((x * 4) % 1.0 - 0.5) / 0.04, (y - 0.0) / 0.06) < 1) & (np.abs(((x * 2) % 1.0) - 0.5) < 0.2)
    accent = tk.blur_small(stems.astype(float), 0.8) * (1 - colour)
    return np.clip(colour + tk.blur_small(dots.astype(float), 0.8), 0, 1), ink, accent



# ------------------------------------------------------------------------------------------- lettering, line art

def _letters(size, rows, cols, seed, height, gap, bold=False):
    """Rows of tiny 'letters' (little strokes and loops, a word at a time), as small print looks from arm's length:
    ``rows`` lines down the tile in ``cols`` columns, each line ``height`` of a row tall."""
    rng = np.random.default_rng(seed)
    canvas = np.zeros((size, size))
    row_h = size / rows
    col_w = size / cols
    for r in range(rows):
        for c in range(cols):
            x = c * col_w + gap
            end = (c + 1) * col_w - gap
            if rng.random() < 0.08:  # a short line ends a paragraph
                end = x + (end - x) * rng.uniform(0.3, 0.7)
            y0 = int(r * row_h + row_h * (1 - height) / 2)
            y1 = int(y0 + row_h * height)
            while x < end:
                word = rng.integers(2, 8)
                for _ in range(word):
                    w = rng.uniform(0.5, 0.9) * row_h * height
                    if x + w > end:
                        break
                    rise = rng.random() < 0.25  # an ascender
                    top = y0 - int(row_h * 0.15) if rise else y0 + int(row_h * height * 0.3)
                    canvas[max(top, 0):y1, int(x):int(x + w * (0.7 if not bold else 0.9))] = 1.0
                    x += w
                x += row_h * height * 0.8  # the space between words
    return tk.blur_small(canvas, 0.6)


def newsprint(size, seed):
    """Newsprint: columns of small print under bold headlines, as on a newspaper."""
    body = _letters(size, 30, 3, seed, 0.45, size / 40)
    heads = _letters(size, 6, 1, seed + 1, 0.6, size / 30, bold=True)
    y = np.arange(size)[:, None] / size
    headline_rows = ((y * 2) % 1.0 < 0.17)
    ink = np.where(np.repeat(headline_rows, size, axis=1), heads, body)
    rules = np.zeros((size, size))
    for k in (1, 2):
        rules[:, int(k * size / 3) - 1:int(k * size / 3) + 1] = 0.6
    return np.clip(ink + rules * ~np.repeat(headline_rows, size, axis=1), 0, 1), _zero(size)


def _drawn(size, draw_fn, scale=4):
    """A tile drawn with PIL at ``scale`` times its size (draw_fn(draw, size) draws white on black, its own wrapping),
    brought down to the tile's size: smooth lines for line art."""
    from PIL import Image, ImageDraw
    big = Image.new("L", (size * scale, size * scale), 0)
    draw_fn(ImageDraw.Draw(big), size * scale)
    return np.asarray(big.resize((size, size), Image.LANCZOS), dtype=float) / 255.0


def script(size, seed):
    """Script: lines of looping handwriting, as on a love letter, a word at a time."""
    rng = np.random.default_rng(seed)

    def draw(d, n):
        lines = 7
        for k in range(lines):
            base = (k + 0.65) / lines * n
            x = rng.random() * n * 0.1
            while x < n * 1.05:
                letters = rng.integers(3, 8)
                points = []
                for t in np.linspace(0, letters * 2 * np.pi, letters * 16):
                    tall = 1.0 + (0.9 if np.sin(t * 0.37 + k) > 0.8 else 0.0)  # now and then an l or a d
                    px = x + (t - 1.25 * np.sin(t)) * n * 0.0045
                    py = base - (1 - np.cos(t)) * n * 0.012 * tall
                    points.append((px, py))
                for dx in (-n, 0, n):  # wrapping round the tile
                    d.line([(px + dx, py) for px, py in points], fill=255, width=max(1, n // 150), joint="curve")
                x = points[-1][0] + n * 0.03
    return _drawn(size, draw), _zero(size)


def birds(size, seed):
    """Birds on branches: a toile of little birds perched on leafy sprigs, in the print colour."""
    rng = np.random.default_rng(seed)

    def sprig(d, cx, cy, s, flip):
        f = -1 if flip else 1
        stem = [(cx + f * (t - 0.5) * s * 1.6, cy + ((t - 0.5) ** 2) * s * 0.5) for t in np.linspace(0, 1, 20)]
        d.line(stem, fill=255, width=max(1, int(s * 0.03)))
        for t in np.linspace(0.1, 0.9, 6):
            lx, ly = cx + f * (t - 0.5) * s * 1.6, cy + ((t - 0.5) ** 2) * s * 0.5
            dy = -s * 0.1 if int(t * 10) % 2 else s * 0.1
            d.ellipse([lx - s * 0.08, ly + dy - s * 0.045, lx + s * 0.08, ly + dy + s * 0.045], fill=200)
        bx, by = cx, cy - s * 0.2
        d.ellipse([bx - s * 0.22, by - s * 0.13, bx + s * 0.22, by + s * 0.13], fill=255)  # the body
        hx = bx + f * s * 0.22
        d.ellipse([hx - s * 0.1, by - s * 0.24, hx + s * 0.1, by - s * 0.04], fill=255)  # the head
        d.polygon([(bx - f * s * 0.18, by - s * 0.02), (bx - f * s * 0.48, by - s * 0.14), (bx - f * s * 0.44, by + s * 0.04)], fill=255)
        d.polygon([(bx + f * s * 0.31, by - s * 0.15), (bx + f * s * 0.41, by - s * 0.12), (bx + f * s * 0.31, by - s * 0.09)], fill=255)

    def draw(d, n):
        for i, (cx, cy) in enumerate(tk.scatter(n, 7, seed, n / 3.2)):
            s = n / (2.6 if i % 2 else 3.4)
            flip = rng.random() < 0.5
            for dx in (-n, 0, n):  # wrapping round the tile
                for dy in (-n, 0, n):
                    sprig(d, cx + dx, cy + dy, s, flip)
    shape = _drawn(size, draw)
    return shape, _outline(np.clip(shape * 1.5, 0, 1), 1) * 0.5


# ----------------------------------------------------------------------------------------------- retro and kids

def groovy(size, seed):
    """Groovy: fat 70s waves in two colours, swelling and narrowing as they ripple across."""
    x, y = tk.grid(size)
    t = (y * 3 + 0.12 * np.sin(2 * np.pi * (x * 2 + y)) + 0.06 * np.sin(2 * np.pi * x * 4)) % 1.0
    first = (t > 0.0) & (t < 0.3)
    second = (t > 0.45) & (t < 0.7)
    return tk.blur_small(first.astype(float), 0.8), _zero(size), tk.blur_small(second.astype(float), 0.8)


def flower_power(size, seed):
    """Flower power: big round-petalled 60s daisies, their hearts in the second colour, overlapping."""
    def bloom(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y), np.arctan2(y, x)
        petals = _soft(rad - (0.62 + 0.3 * np.abs(np.cos(ang * 2.5))), r)
        heart = _soft(rad - 0.3, r)
        return np.clip(petals - heart, 0, 1), np.clip(_outline(petals, 1) * 0.5 + _outline(heart, 1) * 0.5, 0, 1), heart
    return _scatter_shapes(size, [bloom(int(size / 2.6)), bloom(int(size / 3.6)), bloom(int(size / 5))], 9, seed, size / 4)


def dinosaurs(size, seed):
    """Dinosaurs: little long-necked and spiky ones, their spines and spots in the second colour (kids' prints)."""
    def sauropod(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.hypot((x + 0.05) / 0.5, (y - 0.15) / 0.3) < 1
        neck = (np.abs(y + 0.25 - (x - 0.45) * -1.6) < 0.1) & (x > 0.3) & (x < 0.6) & (y < 0.15)
        head = np.hypot((x - 0.66) / 0.13, (y + 0.62) / 0.09) < 1
        tail = (np.abs(y - 0.2 - (x + 0.55) * -0.45) < 0.08 * np.clip(x + 1.0, 0, 1)) & (x < -0.4) & (x > -0.95)
        legs = ((np.abs(x + 0.3) < 0.08) | (np.abs(x - 0.2) < 0.08)) & (y > 0.3) & (y < 0.62)
        shape = tk.blur_small((body | neck | head | tail | legs).astype(float), 0.8)
        spots = tk.blur_small(((np.hypot(x + 0.15, y - 0.05) < 0.08) | (np.hypot(x + 0.35, y - 0.18) < 0.06) |
                               (np.hypot(x + 0.0, y - 0.25) < 0.06)).astype(float), 0.8) * shape
        eye = np.hypot(x - 0.7, y + 0.64) < 0.03
        return np.clip(shape - spots, 0, 1), np.clip(_outline(shape, 1) * 0.5 + eye, 0, 1), spots

    def stego(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = (np.hypot(x / 0.55, (y - 0.2) / 0.3) < 1) & (y < 0.35)
        belly = (np.abs(x) < 0.5) & (y > 0.1) & (y < 0.35)
        head = np.hypot((x - 0.62) / 0.14, (y - 0.25) / 0.1) < 1
        tail = (np.abs(y - 0.2 - (x + 0.55) * -0.2) < 0.06) & (x < -0.45) & (x > -0.9)
        legs = ((np.abs(x + 0.3) < 0.08) | (np.abs(x - 0.25) < 0.08)) & (y > 0.3) & (y < 0.58)
        plates = np.zeros_like(x, dtype=bool)
        for px_, height in ((-0.35, 0.2), (-0.12, 0.28), (0.12, 0.28), (0.33, 0.2)):
            top = 0.2 - 0.3 * np.sqrt(np.clip(1 - (px_ / 0.55) ** 2, 0, 1))
            plates |= (y < top) & (y > top - height) & (np.abs(x - px_) < 0.1 * (y - (top - height)) / height)
        shape = tk.blur_small((body | belly | head | tail | legs).astype(float), 0.8)
        spikes = tk.blur_small(plates.astype(float), 0.8) * (1 - shape)
        eye = np.hypot(x - 0.68, y - 0.21) < 0.03
        return shape, np.clip(_outline(shape, 1) * 0.5 + eye, 0, 1), spikes
    return _scatter_shapes(size, [sauropod(int(size / 2.6)), stego(int(size / 2.8))], 7, seed, size / 3.2, rotate=False)


def sailboats(size, seed):
    """Sailboats: little yachts with a white sail and a coloured one, over a wave (nautical)."""
    def boat(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        hull = (y > 0.3) & (y < 0.52) & (np.abs(x) < 0.6 - (y - 0.3) * 1.2)
        mast = (np.abs(x + 0.02) < 0.025) & (y > -0.8) & (y < 0.3)
        sail = (x > 0.04) & (y < 0.22) & (y > -0.75) & (x < 0.04 + (y + 0.75) * 0.5)
        jib = (x < -0.06) & (y < 0.22) & (y > -0.55) & (x > -0.06 - (y + 0.55) * 0.55)
        wave = np.abs(y - 0.68 - 0.05 * np.sin(x * 12)) < 0.035
        shape = tk.blur_small((hull | sail).astype(float), 0.8)
        second = tk.blur_small(jib.astype(float), 0.8)
        ink = np.clip(mast * 0.9 + _outline(shape, 1) * 0.4 + _outline(second, 1) * 0.4 + wave * 0.6, 0, 1)
        return shape, ink, second
    return _scatter_shapes(size, [boat(int(size / 4)), boat(int(size / 5.5))], 9, seed, size / 4, rotate=False)



def umbrellas(size, seed):
    """Umbrellas: little open brollies with scalloped edges and hooked handles, the panels in two colours."""
    def brolly(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        canopy = (np.hypot(x, (y + 0.05) * 1.2) < 0.8) & (y < 0.0 + 0.08 * np.cos(x * 10))
        panels = canopy & (np.floor((x + 0.8) / 0.4) % 2 == 0)
        handle = ((np.abs(x) < 0.03) & (y > -0.05) & (y < 0.6)) | ((np.abs(np.hypot(x - 0.1, y - 0.6) - 0.1) < 0.03) & (y > 0.6))
        tip = (np.abs(x) < 0.025) & (y < -0.78) & (y > -0.92)
        colour = tk.blur_small((canopy & ~panels).astype(float), 0.8)
        second = tk.blur_small(panels.astype(float), 0.8)
        ink = np.clip(_outline(np.maximum(colour, second), 1) * 0.5 + handle * 0.9 + tip * 0.9, 0, 1)
        return colour, ink, second
    return _scatter_shapes(size, [brolly(int(size / 3.6)), brolly(int(size / 5))], 10, seed, size / 4, rotate=False)



# A little 5 x 7 pixel font for slogan tees (only the letters the slogans use).
PIXEL_FONT = {
    "L": ["X....", "X....", "X....", "X....", "X....", "X....", "XXXXX"],
    "O": [".XXX.", "X...X", "X...X", "X...X", "X...X", "X...X", ".XXX."],
    "V": ["X...X", "X...X", "X...X", "X...X", ".X.X.", ".X.X.", "..X.."],
    "E": ["XXXXX", "X....", "X....", "XXXX.", "X....", "X....", "XXXXX"],
    "H": ["X...X", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
    "S": [".XXXX", "X....", "X....", ".XXX.", "....X", "....X", "XXXX."],
    "U": ["X...X", "X...X", "X...X", "X...X", "X...X", "X...X", ".XXX."],
    "N": ["X...X", "XX..X", "X.X.X", "X..XX", "X...X", "X...X", "X...X"],
    "Y": ["X...X", "X...X", ".X.X.", "..X..", "..X..", "..X..", "..X.."],
    "C": [".XXXX", "X....", "X....", "X....", "X....", "X....", ".XXXX"],
    "T": ["XXXXX", "..X..", "..X..", "..X..", "..X..", "..X..", "..X.."],
    "2": [".XXX.", "X...X", "....X", "...X.", "..X..", ".X...", "XXXXX"],
    "3": ["XXXX.", "....X", "....X", ".XXX.", "....X", "....X", "XXXX."],
    "!": ["..X..", "..X..", "..X..", "..X..", "..X..", ".....", "..X.."],
}


def _slogan(size, lines, outline=True):
    """A slogan in the pixel font, centred on a clear tile, each line as wide as fits; an outline in ink."""
    rows = len(lines)
    longest = max(len(line) for line in lines)
    cell = int(size * 0.84 / max(longest * 6 - 1, rows * 8 - 1))
    mask = np.zeros((size, size))
    top = (size - (rows * 8 - 1) * cell) // 2
    for r, line in enumerate(lines):
        left = (size - (len(line) * 6 - 1) * cell) // 2
        for k, ch in enumerate(line):
            glyph = PIXEL_FONT.get(ch)
            if glyph is None:
                continue
            for gy, row in enumerate(glyph):
                for gx, bit in enumerate(row):
                    if bit == "X":  # each pixel a little over its cell, so the strokes are bold
                        y0 = top + (r * 8 + gy) * cell - cell // 4
                        x0 = left + (k * 6 + gx) * cell - cell // 4
                        mask[max(y0, 0):y0 + cell + cell // 2, max(x0, 0):x0 + cell + cell // 2] = 1.0
    mask = tk.blur_small(mask, 0.8)
    return mask, (_outline(mask, 2) * 0.8 if outline else _zero(size))


def slogan_love(size, seed):
    """LOVE, in pixel letters, on two lines."""
    return _slogan(size, ["LO", "VE"])


def slogan_hello(size, seed):
    """HELLO!, in pixel letters, on two lines."""
    return _slogan(size, ["HEL", "LO!"])


def slogan_sunny(size, seed):
    """SUNNY, in pixel letters, on two lines."""
    return _slogan(size, ["SUN", "NY"])


def slogan_cute(size, seed):
    """CUTE!, in pixel letters, on two lines."""
    return _slogan(size, ["CU", "TE!"])


def varsity_23(size, seed):
    """A varsity number, 23, big and outlined."""
    return _slogan(size, ["23"])



def knitted_hearts(size, seed):
    """Knitted hearts: rows of little hearts between dotted bands, as on a Valentine's sweater."""
    rows = ["X", ".", "X.X.", ".", ".", "..XX.XX.....", ".XXXXXXX....", ".XXXXXXX....", "..XXXXX.....",
            "...XXX......", "....X.......", ".", ".", "X.X.", ".", "X", "."]
    return _chart(rows, size)


def knitted_diamonds(size, seed):
    """Knitted diamonds: a lattice of outlined diamonds with a dot in each (a classic jumper), in two tones."""
    rows = ["....X.......", "...X.X......", "..X...X.....", ".X..o..X....", "X..ooo..X...", ".X..o..X....",
            "..X...X.....", "...X.X......", "....X......."]
    return _chart(rows, size, 12)


def snowflake_yoke(size, seed):
    """A snowflake band, knitted: eight-pointed snowflakes in a row between rules (a yoke or a hem)."""
    rows = ["X", ".", ".....X......", "..X..X..X...", "...X.X.X....", "....XXX.....", "XXXXXoXXXXX.",
            "....XXX.....", "...X.X.X....", "..X..X..X...", ".....X......", ".", "X"]
    return _chart(rows, size)



def liberty(size, seed):
    """A dense small-scale floral, as in the classic lawn prints: tiny five-petalled blooms crowded together in two
    colours, little leaves filling the gaps."""
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(6):
        b, i = _petal_flower(int(size / (11 + 4 * rng.random())), 5, 0.5)
        none = np.zeros_like(b)
        shapes.append((b, i * 0.6, none) if k % 3 else (none, i * 0.6, b))
    lb, li = _leaf_shape(int(size / 16))
    shapes += [(lb * 0.45, li * 0.5, np.zeros_like(lb))] * 2
    return _scatter_shapes(size, shapes, 70, seed, size / 13)


def poppies(size, seed):
    """Poppies: wide four-petalled cups with a dark heart, on wiry stems with buds."""
    def poppy(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y + 0.2), np.arctan2(y + 0.2, x)
        cup = _soft(rad - (0.5 + 0.12 * np.cos(ang * 4)), r)
        heart = _soft(rad - 0.14, r)
        stem = (np.abs(x - 0.06 * np.sin((y - 0.3) * 4)) < 0.025) & (y > 0.25) & (y < 0.98)
        bud = np.hypot((x - 0.32) / 0.08, (y - 0.55) / 0.12) < 1
        return np.clip(cup - heart, 0, 1), np.clip(heart * 0.9 + stem * 0.9 + _outline(cup, 1) * 0.4 + bud * 0.7, 0, 1)
    return _scatter_shapes(size, [poppy(int(size / 3.2)), poppy(int(size / 4.4))], 9, seed, size / 3.6, rotate=False)


def lavender(size, seed):
    """Lavender sprigs: slender stems topped with spikes of little flowers, in the print colour, the stems green."""
    def sprig(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        stem = (np.abs(x + 0.05 * y) < 0.025) & (y > -0.2) & (y < 0.95)
        florets = np.zeros_like(x)
        for k in range(9):
            fy = -0.9 + k * 0.09
            for side in (-1, 1):
                fx = side * (0.07 - 0.03 * (k / 9))
                florets = np.maximum(florets, (np.hypot((x - fx + 0.05 * fy) / 0.06, (y - fy) / 0.045) < 1).astype(float))
        leaves = (np.hypot((x - 0.15) / 0.2, (y - 0.45) / 0.04) < 1) | (np.hypot((x + 0.17) / 0.2, (y - 0.6) / 0.04) < 1)
        green = tk.blur_small((stem | leaves).astype(float), 0.8)
        return tk.blur_small(florets, 0.8), _outline(florets, 1) * 0.3, green * (1 - florets)
    return _scatter_shapes(size, [sprig(int(size / 3.2)), sprig(int(size / 4))], 12, seed, size / 5.5, rotate=False)



def maple_leaves(size, seed):
    """Autumn leaves: five-pointed maple leaves tumbling about, in the print colour and the second."""
    def leaf(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        rad, ang = np.hypot(x, y + 0.1), np.arctan2(y + 0.1, x) + np.pi / 2
        lobes = 0.45 + 0.4 * np.abs(np.cos(ang * 2.5)) ** 0.6 + 0.08 * np.cos(ang * 15)
        body = _soft(rad - lobes * (y < 0.45), r * 0.8)
        stalk = ((np.abs(x) < 0.03) & (y > 0.3) & (y < 0.95)).astype(float)
        veins = (np.abs(np.sin(ang * 2.5)) < 0.08) & (rad < lobes * 0.9)
        shape = np.clip(body + stalk, 0, 1)
        return shape, np.clip(veins * 0.5 + _outline(shape, 1) * 0.4, 0, 1)
    a = leaf(int(size / 4))
    b = leaf(int(size / 5.5))
    none_a, none_b = np.zeros_like(a[0]), np.zeros_like(b[0])
    return _scatter_shapes(size, [a, (none_a, a[1], a[0]), b, (none_b, b[1], b[0])], 12, seed, size / 4.2)


def pineapples(size, seed):
    """Pineapples: criss-crossed fruit under a spiky green crown."""
    def fruit(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.hypot(x / 0.42, (y - 0.25) / 0.58) < 1
        grid = body & ((np.abs(np.sin((x + y) * 12)) < 0.18) | (np.abs(np.sin((x - y) * 12)) < 0.18))
        crown = np.zeros_like(x, dtype=bool)
        for k, a in enumerate(np.linspace(-0.7, 0.7, 5)):
            u, v = x - np.sin(a) * 0.1, y + 0.32
            t = np.radians(a * 40)
            ru, rv = np.cos(t) * u + np.sin(t) * v, -np.sin(t) * u + np.cos(t) * v
            crown |= (np.abs(ru) < 0.07 * (1 + rv / 0.6)) & (rv < 0) & (rv > -0.6 + 0.1 * abs(k - 2))
        colour = tk.blur_small(body.astype(float), 0.8)
        green = tk.blur_small(crown.astype(float), 0.8) * (1 - colour)
        return colour, np.clip(grid * 0.5 + _outline(colour, 1) * 0.5, 0, 1), green
    return _scatter_shapes(size, [fruit(int(size / 3.6)), fruit(int(size / 5))], 9, seed, size / 4, rotate=False)


def memphis(size, seed):
    """Memphis: an 80s jumble of squiggles, triangles, dots and half circles in two colours."""
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(8):
        px = int(size / 6) | 1
        h = px // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float) / h
        kind = k % 4
        if kind == 0:
            m = (np.abs(yy - 0.3 * np.sin(xx * 7)) < 0.12) & (np.abs(xx) < 0.9)
        elif kind == 1:
            m = (yy > -0.6) & (yy < 0.6) & (np.abs(xx) < (yy + 0.6) * 0.7)
        elif kind == 2:
            m = (np.hypot(xx, yy) < 0.7) & (yy < 0)
        else:
            m = (np.hypot(((xx * 2) % 1.0) - 0.5, ((yy * 2) % 1.0) - 0.5) < 0.18) & (np.abs(xx) < 0.9) & (np.abs(yy) < 0.9)
        m = tk.blur_small(m.astype(float), 0.8)
        none = np.zeros_like(m)
        shapes.append((m, _outline(m, 1) * 0.6, none) if k % 2 else (none, _outline(m, 1) * 0.6, m))
    return _scatter_shapes(size, shapes, 22, seed, size / 6.5)



def regimental(size, seed):
    """Regimental stripes: bold diagonal bands with a thin stripe of the second colour beside each (a club tie)."""
    x, y = tk.grid(size)
    t = (x + y) * 2 % 1.0
    return _band(t, 0.3, 0.32, 0.008), _zero(size), _band(t, 0.6, 0.06, 0.008)


def quilt_blocks(size, seed):
    """Quilt blocks: half-square triangles in two colours, row on row, with stitched seams."""
    x, y = tk.grid(size)
    n = 4
    u, v = (x * n) % 1.0, (y * n) % 1.0
    qu, qv = np.floor(u * 2), np.floor(v * 2)  # which quarter of the block
    lu, lv = (u * 2) % 1.0, (v * 2) % 1.0
    quarter = (qu + 2 * qv).astype(int)
    upper = np.where((quarter == 0) | (quarter == 3), lu > lv, lu + lv < 1.0)  # the triangles turn round the centre
    block = (np.floor(x * n) + np.floor(y * n)) % 2 == 0
    colour = (upper & block).astype(float)
    accent = (upper & ~block).astype(float)
    seams = np.minimum(np.abs(((x * n * 2) % 1.0) - 0.5), np.abs(((y * n * 2) % 1.0) - 0.5)) > 0.48
    stitch = seams & (np.floor((x + y) * 200) % 2 == 0)
    return tk.blur_small(colour, 0.7), stitch * 0.5, tk.blur_small(accent, 0.7)


def painted_spots(size, seed):
    """Painted spots: round dabs of paint, uneven in size and edge, as if done by hand."""
    rng = np.random.default_rng(seed)
    colour = _zero(size)
    for cx, cy in tk.scatter(size, 40, seed, size / 8):
        rad = size / 34 * (0.7 + 0.7 * rng.random())
        px = int(rad * 3) | 1
        h = px // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float)
        ang = np.arctan2(yy, xx)
        edge = rad * (1 + 0.1 * np.sin(ang * 3 + rng.random() * 6) + 0.06 * np.sin(ang * 7 + rng.random() * 6))
        dab = np.clip(edge - np.hypot(xx, yy) + 0.5, 0, 1) * (0.8 + 0.2 * rng.random())
        tk.stamp(colour, dab, cx, cy)
    return colour, _zero(size)



def graffiti(size, seed):
    """Graffiti: spray-painted tags, drips, sparkles, zigzag arrows and crowns in two colours, outlined in ink, with
    overspray round them, as on a skate wall."""
    def layer(which):
        rng = np.random.default_rng(seed + which)

        def draw(d, n):
            for k, (cx, cy) in enumerate(tk.scatter(n, 24, seed, n / 6.5)):
                if k % 2 != which:
                    continue
                kind = (k // 2) % 5
                for dx in (-n, 0, n):
                    for dy in (-n, 0, n):
                        x0, y0 = cx + dx, cy + dy
                        if kind == 0:  # a looping tag: a run of loops, like joined-up spray writing
                            pts = [(x0 + n * (0.022 * t - 0.045 * np.sin(t)), y0 - n * 0.045 * (1 - np.cos(t)))
                                   for t in np.linspace(-6, 6, 70)]
                            d.line(pts, fill=255, width=max(3, n // 40), joint="curve")
                        elif kind == 1:  # a blob with drips
                            r = n * (0.055 + 0.03 * rng.random())
                            d.ellipse([x0 - r, y0 - r, x0 + r, y0 + r], fill=255)
                            for j in range(3):
                                dxj = (j - 1) * r * 0.6
                                drip = y0 + r * (1.5 + 2 * rng.random())
                                d.line([(x0 + dxj, y0), (x0 + dxj, drip)], fill=255, width=max(4, n // 55))
                                d.ellipse([x0 + dxj - n * 0.008, drip - n * 0.008, x0 + dxj + n * 0.008, drip + n * 0.008], fill=255)
                        elif kind == 2:  # a four-pointed sparkle
                            r = n * 0.075
                            d.polygon([(x0, y0 - r), (x0 + r * 0.22, y0 - r * 0.22), (x0 + r, y0), (x0 + r * 0.22, y0 + r * 0.22),
                                       (x0, y0 + r), (x0 - r * 0.22, y0 + r * 0.22), (x0 - r, y0), (x0 - r * 0.22, y0 - r * 0.22)], fill=255)
                        elif kind == 3:  # a zigzag arrow
                            s = n * 0.05
                            pts = [(x0 - 2 * s, y0 + s), (x0 - s, y0 - s * 0.6), (x0, y0 + s * 0.6), (x0 + s, y0 - s)]
                            d.line(pts, fill=255, width=max(4, n // 38), joint="curve")
                            d.polygon([(x0 + s * 1.7, y0 - s * 1.6), (x0 + s * 0.4, y0 - s * 1.6), (x0 + s * 1.5, y0 - s * 0.3)], fill=255)
                        else:  # a three-pointed crown
                            s = n * 0.05
                            d.polygon([(x0 - s * 1.2, y0 + s * 0.7), (x0 - s * 1.3, y0 - s * 0.8), (x0 - s * 0.6, y0 - s * 0.1),
                                       (x0, y0 - s * 1.1), (x0 + s * 0.6, y0 - s * 0.1), (x0 + s * 1.3, y0 - s * 0.8),
                                       (x0 + s * 1.2, y0 + s * 0.7)], outline=255, width=max(4, n // 45))
                            for px in (-1.3, 0, 1.3):
                                py = -0.8 if px else -1.1
                                d.ellipse([x0 + (px - 0.18) * s, y0 + (py - 0.36) * s, x0 + (px + 0.18) * s, y0 + py * s], fill=255)
        paint = _drawn(size, draw)
        near = np.clip(tk.blur(paint, 4) * 3, 0, 1) * (1 - paint)  # overspray: fine specks round the paint
        specks = (rng.random((size, size)) < 0.25 * near).astype(float)
        return paint, specks
    (first, first_specks), (second, second_specks) = layer(0), layer(1)
    ink = _outline(np.clip(first + second, 0, 1), 1) * 0.6  # the paint outlined, not its overspray
    first = np.clip(first + first_specks * 0.8 * (1 - second), 0, 1)
    second = np.clip(second + second_specks * 0.8, 0, 1) * (1 - first)
    return first, ink, second


def swooshes(size, seed):
    """Speed swooshes: long tapering diagonal streaks in two colours, as on sportswear."""
    x, y = tk.grid(size)
    t = (x * 1 + y * 2) % 1.0
    s_ = (y * 3 - x) % 1.0
    taper = 0.5 + 0.5 * np.sin(2 * np.pi * s_)
    first = (np.abs(t - 0.3) < 0.085 * taper)
    second = (np.abs(t - 0.46) < 0.04 * taper)
    return tk.blur_small(first.astype(float), 0.8), _zero(size), tk.blur_small(second.astype(float), 0.8)


def thorns(size, seed):
    """Thorny vines: dark briars twisting across the cloth, sharp thorns along them and the odd rose."""
    rng = np.random.default_rng(seed)

    def draw(d, n):
        for k in range(3):
            phase, amp, y0 = rng.random() * 6, n * 0.06, (k + 0.5) / 3 * n
            pts = [(x, y0 + amp * np.sin(x / n * 2 * np.pi * 2 + phase)) for x in np.linspace(-n * 0.1, n * 1.1, 80)]
            for dy in (-n, 0, n):
                d.line([(px, py + dy) for px, py in pts], fill=255, width=max(3, n // 60), joint="curve")
                for j in range(4, 76, 6):
                    (ax, ay), (bx, by) = pts[j], pts[j + 1]
                    nx, ny = -(by - ay), bx - ax
                    ln = np.hypot(nx, ny) or 1
                    side = 1 if j % 12 else -1
                    tipx, tipy = ax + side * nx / ln * n * 0.04, ay + side * ny / ln * n * 0.04 + dy
                    d.polygon([(ax - (bx - ax) * 0.6, ay + dy), (ax + (bx - ax) * 0.6, ay + dy), (tipx, tipy)], fill=255)
    vines = _drawn(size, draw)
    roses, rose_ink, leaves = _scatter_shapes(size, [_rose(int(size / 3.8))], 4, seed + 3, size / 2.5)
    return np.clip(np.maximum(vines, roses) * (1 - leaves), 0, 1), np.clip(rose_ink, 0, 1), leaves



def chains(size, seed):
    """Chain links: interlocking oval links in diagonal runs, as on a silk scarf, outlined in ink."""
    def draw(d, n):
        # The chains run 2 across for 1 down, so each wraps after sqrt(5) tiles of length: 26 links to that, an even
        # number so the alternating links meet themselves.
        step = n * np.sqrt(5) / 26
        for chain in range(3):
            for k in range(0, 26):
                along = k * step
                cx = along * 0.894 + chain * n / 3  # the chains run at 26.6 degrees (2 across, 1 down), so they repeat
                cy = along * 0.447
                w, h = (step * 0.62, step * 0.34) if k % 2 == 0 else (step * 0.5, step * 0.2)
                for dx in (-n, 0, n):
                    for dy in (-n, 0, n):
                        x0, y0 = cx % n + dx, cy % n + dy
                        box = [(x0 + px * 0.894 - py * 0.447, y0 + px * 0.447 + py * 0.894) for px, py in
                               [(w * np.cos(t), h * np.sin(t)) for t in np.linspace(0, 2 * np.pi, 24)]]
                        d.line(box + [box[0]], fill=255, width=max(3, n // 60))
    links = _drawn(size, draw)
    return links, _outline(links, 1) * 0.5


def rosebuds(size, seed):
    """Rosebuds: tiny closed roses on short stems with a pair of leaves, scattered close (date dresses, lingerie)."""
    def bud(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        head = np.hypot(x / 0.3, (y + 0.3) / 0.38) < 1
        swirl = head & (np.abs(np.sin((x + 0.3 * y) * 14)) < 0.25)
        stem = (np.abs(x) < 0.04) & (y > 0.05) & (y < 0.9)
        leaves = (np.hypot((x - 0.18) / 0.18, (y - 0.4) / 0.08) < 1) | (np.hypot((x + 0.18) / 0.18, (y - 0.55) / 0.08) < 1)
        colour = tk.blur_small(head.astype(float), 0.7)
        green = tk.blur_small((stem | leaves).astype(float), 0.7) * (1 - colour)
        return colour, np.clip(swirl * 0.6 + _outline(colour, 1) * 0.4, 0, 1), green
    return _scatter_shapes(size, [bud(int(size / 6)), bud(int(size / 7.5))], 34, seed, size / 7.5)


def keys(size, seed):
    """Skeleton keys: old ornate keys with looped bows, tumbling about (gothic, bookish)."""
    def key(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        bow = np.abs(np.hypot(x, y + 0.6) - 0.22) < 0.07
        bow |= np.abs(np.hypot(x - 0.17, y + 0.75) - 0.08) < 0.04
        shaft = (np.abs(x) < 0.05) & (y > -0.4) & (y < 0.85)
        bit = (x > 0) & (x < 0.25) & (((y > 0.55) & (y < 0.65)) | ((y > 0.72) & (y < 0.85)))
        collar = (np.abs(x) < 0.1) & (np.abs(y + 0.3) < 0.04)
        shape = tk.blur_small((bow | shaft | bit | collar).astype(float), 0.7)
        return shape, _outline(shape, 1) * 0.5
    return _scatter_shapes(size, [key(int(size / 3.2)), key(int(size / 4.4))], 10, seed, size / 3.8)


def triple_stripes(size, seed):
    """Triple stripes: sets of three narrow stripes down the cloth, as on track wear, in two colours."""
    x, y = tk.grid(size)
    u = x * 2
    first = np.maximum(_band(u, 0.38, 0.04, 0.006), _band(u, 0.5, 0.04, 0.006))
    second = _band(u, 0.62, 0.04, 0.006)
    return first, _zero(size), second



def art_deco(size, seed):
    """Art deco fans: rows of scalloped fans, each ribbed with fine rays and rimmed in the second colour (a gala)."""
    x, y = tk.grid(size)
    n = 4
    rows = 4
    colour, accent, ink = _zero(size), _zero(size), _zero(size)
    for row in range(rows + 1):
        shift = 0.5 / n if row % 2 else 0.0
        u = ((x - shift) * n) % 1.0 - 0.5
        dv = ((y - row / rows + 0.5) % 1.0 - 0.5) * rows
        rad = np.hypot(u, dv * 0.5)
        fan = (rad < 0.5) & (dv <= 0)
        rays = fan & (np.abs(np.sin(np.arctan2(dv, u) * 9)) < 0.18) & (rad > 0.12)
        rim = fan & (rad > 0.42)
        colour = np.where(fan, (~rim).astype(float), colour)
        accent = np.where(fan, rim.astype(float), accent)
        ink = np.where(fan, rays * 0.6, ink)
    return colour, ink, accent


def bandana(size, seed):
    """Bandana: paisleys and little flowers set inside a border of rules and dots, square by square, as a bandana is printed."""
    x, y = tk.grid(size)
    edge = np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y))
    border = (_band(edge, 0.012, 0.02, 0.003) + _band(edge, 0.075, 0.012, 0.003)).clip(0, 1)
    along = np.where(np.minimum(x, 1 - x) < np.minimum(y, 1 - y), y, x)
    dots = (np.hypot(((along * 20) % 1.0) - 0.5, (edge - 0.045) * 20) < 0.28).astype(float)
    colour, ink = _zero(size), _zero(size)
    big, small = _paisley_shape(int(size / 3.6)), _paisley_shape(int(size / 5.5))
    flower = _petal_flower(int(size / 9), 6, 0.45)
    for (cx, cy), shape in (((0.32, 0.33), big), ((0.68, 0.67), big), ((0.7, 0.3), small), ((0.3, 0.7), small)):
        tk.stamp(colour, shape[0], cx * size, cy * size)
        tk.stamp(ink, shape[1], cx * size, cy * size)
    for cx, cy in ((0.5, 0.5), (0.5, 0.18), (0.5, 0.82), (0.18, 0.5), (0.82, 0.5)):
        tk.stamp(colour, flower[0], cx * size, cy * size)
        tk.stamp(ink, flower[1], cx * size, cy * size)
    return np.clip(np.maximum(colour, np.maximum(border, dots)), 0, 1), np.clip(ink, 0, 1)


def line_botanicals(size, seed):
    """Line-art botanicals: leafy sprigs drawn in fine lines, as in a botanical sketchbook."""
    rng = np.random.default_rng(seed)

    def draw(d, n):
        for k, (cx, cy) in enumerate(tk.scatter(n, 7, seed, n / 3.2)):
            angle = rng.random() * np.pi * 2
            length = n * (0.3 + 0.08 * rng.random())
            ca, sa = np.cos(angle), np.sin(angle)
            for dx in (-n, 0, n):
                for dy in (-n, 0, n):
                    x0, y0 = cx + dx, cy + dy
                    stem = [(x0 + ca * t * length + -sa * 0.08 * length * np.sin(t * 3), y0 + sa * t * length + ca * 0.08 * length * np.sin(t * 3))
                            for t in np.linspace(-0.5, 0.5, 20)]
                    d.line(stem, fill=255, width=max(3, n // 70))
                    for j in range(2, 18, 3):
                        px, py = stem[j]
                        side = 1 if j % 2 else -1
                        la = angle + side * 0.9
                        lx, ly = px + np.cos(la) * length * 0.18, py + np.sin(la) * length * 0.18
                        leaf = [(px + (lx - px) * t + side * -np.sin(la) * length * 0.05 * np.sin(np.pi * t),
                                 py + (ly - py) * t + side * np.cos(la) * length * 0.05 * np.sin(np.pi * t)) for t in np.linspace(0, 1, 8)]
                        leaf2 = [(px + (lx - px) * t - side * -np.sin(la) * length * 0.05 * np.sin(np.pi * t),
                                  py + (ly - py) * t - side * np.cos(la) * length * 0.05 * np.sin(np.pi * t)) for t in np.linspace(0, 1, 8)]
                        d.line(leaf + leaf2[::-1], fill=255, width=max(3, n // 85))
    return _drawn(size, draw), _zero(size)



def dogs(size, seed):
    """Dog faces: round heads with floppy ears in the second colour, a nose and a little tongue (pyjamas, kids)."""
    def dog(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        head = np.hypot(x / 0.6, (y - 0.05) / 0.58) < 1
        ears = (np.hypot((np.abs(x) - 0.58) / 0.2, (y + 0.05) / 0.42) < 1) & (y > -0.45)
        muzzle = np.hypot(x / 0.3, (y - 0.3) / 0.2) < 1
        eyes = np.hypot(np.abs(x) - 0.22, y + 0.08) < 0.07
        nose = np.hypot(x / 0.11, (y - 0.2) / 0.07) < 1
        tongue = np.hypot(x / 0.07, (y - 0.46) / 0.08) < 1
        colour = tk.blur_small((head & ~ears).astype(float), 0.7)
        accent = tk.blur_small(ears.astype(float), 0.7)
        ink = np.clip(eyes + nose + tongue * 0.5 + _outline(np.maximum(colour, accent), 1) * 0.5 + _outline(muzzle.astype(float), 1) * 0.4, 0, 1)
        return colour, ink, accent
    return _scatter_shapes(size, [dog(int(size / 4.5)), dog(int(size / 6))], 11, seed, size / 4.2, rotate=False)


def frogs(size, seed):
    """Frogs: little sitting frogs with big eyes and a wide smile, among lily pads in the second colour."""
    def frog(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        body = np.hypot(x / 0.62, (y - 0.15) / 0.45) < 1
        eyes = np.hypot(np.abs(x) - 0.32, y + 0.32) < 0.2
        pupils = np.hypot(np.abs(x) - 0.32, y + 0.32) < 0.08
        smile = (np.abs(np.hypot(x, y + 0.05) - 0.35) < 0.035) & (y > 0.12) & (np.abs(x) < 0.3)
        feet = (np.hypot((np.abs(x) - 0.45) / 0.18, (y - 0.55) / 0.08) < 1)
        shape = tk.blur_small((body | eyes | feet).astype(float), 0.7)
        return shape, np.clip(pupils + smile + _outline(shape, 1) * 0.5, 0, 1)

    def pad(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        disc = (np.hypot(x, y * 1.4) < 0.8) & ~((np.abs(np.arctan2(y, x) - 0.6) < 0.25))
        shape = tk.blur_small(disc.astype(float), 0.7)
        return np.zeros_like(shape), _outline(shape, 1) * 0.4, shape
    return _scatter_shapes(size, [frog(int(size / 4.5)), pad(int(size / 4)), frog(int(size / 6)), pad(int(size / 5.5))], 12, seed, size / 4.6, rotate=False)



def knitted_bunnies(size, seed):
    """Knitted bunnies: rows of little rabbits between bands of dots, as on a child's jumper."""
    bunny = ["..X.X.......", "..X.X.......", "..XXX.......", ".XXoXX......", ".XXXXX......", "..XXX.......",
             ".XXXXXX.....", "XXXXXXX.....", "XXXXXXX..X..", ".XXXXX......", ".X...X......", "."]
    rows = ["X", ".", "X...", ".", "."] + bunny + [".", ".", "X...", ".", "X", "."]
    return _chart(rows, size)


def knitted_stripes(size, seed):
    """Knitted stripes: uneven bands of two colours and a thin dotted row, as in a striped jumper."""
    rows = ["X", "X", "X", ".", ".", "o", "o", ".", ".", "X.X.", ".", ".", "X", "X", ".", ".", ".", "o", "."]
    return _chart(rows, size, 12)



def pearls(size, seed):
    """Strings of pearls: swags of round beads, each with a highlight, looping across the cloth (date, elegant)."""
    x, y = tk.grid(size)
    colour, ink = _zero(size), _zero(size)
    bead = tk.disc(size / 70, 1.0)
    shine = tk.disc(size / 220, 0.8)
    for row in range(3):
        for k in range(72):
            t = k / 72
            cx = t * size
            cy = (row + 0.35 + 0.18 * np.sin(2 * np.pi * t * 2)) / 3 * size
            tk.stamp(colour, bead, cx, cy)
            tk.stamp(ink, tk.ring(size / 70 + 0.6, size / 70 - 0.6) * 0.5, cx, cy)
            tk.stamp(colour, shine * 0.0, cx, cy)
    highlight = _zero(size)
    for row in range(3):
        for k in range(72):
            t = k / 72
            tk.stamp(highlight, shine, t * size - size / 200, (row + 0.35 + 0.18 * np.sin(2 * np.pi * t * 2)) / 3 * size - size / 200)
    return np.clip(colour - highlight * 0.6, 0, 1), np.clip(ink, 0, 1), np.clip(highlight, 0, 1)


def ravens(size, seed):
    """Ravens: black birds in flight and perched, wings spread (gothic)."""
    def draw_bird(d, x0, y0, s, perched):
        if perched:
            d.ellipse([x0 - s * 0.35, y0 - s * 0.2, x0 + s * 0.35, y0 + s * 0.25], fill=255)  # body
            d.ellipse([x0 + s * 0.2, y0 - s * 0.4, x0 + s * 0.48, y0 - s * 0.12], fill=255)  # head
            d.polygon([(x0 + s * 0.46, y0 - s * 0.3), (x0 + s * 0.68, y0 - s * 0.24), (x0 + s * 0.46, y0 - s * 0.2)], fill=255)
            d.polygon([(x0 - s * 0.3, y0), (x0 - s * 0.75, y0 + s * 0.3), (x0 - s * 0.6, y0 + s * 0.05)], fill=255)  # tail
            d.line([(x0 - s * 0.05, y0 + s * 0.22), (x0 - s * 0.05, y0 + s * 0.42)], fill=255, width=max(2, int(s * 0.05)))
            d.line([(x0 + s * 0.12, y0 + s * 0.22), (x0 + s * 0.12, y0 + s * 0.42)], fill=255, width=max(2, int(s * 0.05)))
        else:
            wing = [(x0, y0), (x0 - s * 0.3, y0 - s * 0.35), (x0 - s * 0.8, y0 - s * 0.3), (x0 - s * 0.55, y0 - s * 0.12),
                    (x0 - s * 0.75, y0 - s * 0.05), (x0 - s * 0.3, y0 + s * 0.05)]
            d.polygon(wing, fill=255)
            d.polygon([(2 * x0 - px, py) for px, py in wing], fill=255)
            d.ellipse([x0 - s * 0.12, y0 - s * 0.12, x0 + s * 0.12, y0 + s * 0.2], fill=255)

    def draw(d, n):
        for k, (cx, cy) in enumerate(tk.scatter(n, 9, seed, n / 3.6)):
            s = n * (0.14 if k % 2 else 0.11)
            for dx in (-n, 0, n):
                for dy in (-n, 0, n):
                    draw_bird(d, cx + dx, cy + dy, s, k % 3 == 0)
    birds = _drawn(size, draw)
    return birds, _outline(birds, 1) * 0.4


def swallows(size, seed):
    """Swallows: tattoo-style birds in flight, swept-back wings and a forked tail, the breast in the second colour."""
    rng = np.random.default_rng(seed)
    # The silhouette from above, facing right, in units of its length: crescent wings swept back to their tips, long
    # forked tail streamers; then the breast.
    outline = [(0.48, 0.0), (0.35, -0.07), (0.15, -0.08), (0.1, -0.35), (-0.05, -0.6), (-0.35, -0.8), (-0.15, -0.45),
               (-0.08, -0.12), (-0.25, -0.05), (-0.8, -0.26), (-0.45, 0.0), (-0.8, 0.26), (-0.25, 0.05), (-0.08, 0.12),
               (-0.15, 0.45), (-0.35, 0.8), (-0.05, 0.6), (0.1, 0.35), (0.15, 0.08), (0.35, 0.07)]
    breast = [(0.45, 0.02), (0.3, 0.07), (0.12, 0.08), (0.2, 0.0), (0.35, -0.02)]

    def layer(points):
        def draw(d, n):
            for k, (cx, cy) in enumerate(tk.scatter(n, 9, seed, n / 3.6)):
                s_ = n * (0.26 if k % 2 else 0.2)
                a = rng.random() * 0.8 - 0.4
                ca, sa = np.cos(a), np.sin(a)
                for dx in (-n, 0, n):
                    for dy in (-n, 0, n):
                        d.polygon([(cx + dx + s_ * (ca * px - sa * py), cy + dy + s_ * (sa * px + ca * py)) for px, py in points], fill=255)
        return _drawn(size, draw)
    rng_state = rng.bit_generator.state
    body = layer(outline)
    rng.bit_generator.state = rng_state  # the same angles for the breasts
    chest = layer(breast) * body
    return np.clip(body - chest, 0, 1), _outline(body, 1) * 0.7, chest

# name, tags, tiles per metre (min, max), weight
PRINTS = {
    # Patterns the stand-ins share (their catalog lists these ids).
    "stripes": (stripes, "Stripes", ["casual", "sporty", "street"], (6, 10), 1.2),
    "dots": (dots, "Polka dots", ["cute", "casual"], (6, 10), 1.2),
    "plaid": (tartan, "Tartan", ["bookish", "elegant", "gothic", "street"], (3, 6), 1.2),
    "check": (check, "Check", ["cute", "casual", "gothic"], (6, 10), 0.8),
    "hearts": (hearts, "Hearts", ["cute", "date"], (4, 7), 0.8),
    "stars": (stars, "Stars", ["cute", "street", "casual"], (4, 7), 0.8),
    "pinstripe": (pinstripe, "Pinstripe", ["elegant", "bookish", "gothic"], (10, 16), 1.0),
    "floral": (ditsy_floral, "Ditsy floral", ["cute", "elegant", "casual", "date"], (4, 7), 1.4),
    "denim": (diagonal_stripes, "Diagonal stripes", ["casual", "street"], (5, 9), 0.6),
    "knit": (zigzag, "Zigzag", ["cute", "bookish", "casual"], (4, 7), 0.6),
    "argyle": (argyle, "Argyle", ["bookish", "elegant", "casual"], (4, 7), 0.8),
    "chevron": (chevron, "Chevron", ["street", "sporty", "casual"], (5, 9), 0.7),
    # The rest of the library.
    "breton": (breton, "Breton stripes", ["casual", "elegant", "bookish"], (5, 8), 1.0),
    "chalkstripe": (chalk_stripe, "Chalk stripe", ["elegant", "bookish"], (7, 10), 0.6),
    "awning": (awning, "Awning stripes", ["casual", "cute", "street"], (3, 5), 0.6),
    "candystripe": (candy_stripe, "Candy stripes", ["cute", "casual", "bookish"], (6, 9), 0.7),
    "gingham": (gingham, "Gingham", ["cute", "casual", "date"], (6, 10), 1.2),
    "buffalo": (buffalo_check, "Buffalo check", ["street", "casual", "gothic"], (6, 9), 0.8),
    "windowpane": (windowpane, "Windowpane", ["elegant", "bookish"], (4, 7), 0.6),
    "houndstooth": (houndstooth, "Houndstooth", ["elegant", "bookish", "gothic"], (8, 14), 1.0),
    "harlequin": (harlequin, "Harlequin", ["gothic", "cute", "street"], (4, 7), 0.5),
    "triangles": (triangles, "Triangles", ["street", "sporty", "casual"], (3, 5), 0.5),
    "hexagons": (hexagons, "Honeycomb", ["street", "sporty"], (3, 5), 0.4),
    "waves": (waves, "Waves", ["casual", "sporty", "cute"], (3, 5), 0.6),
    "scales": (scales, "Mermaid scales", ["cute", "elegant", "street"], (4, 8), 0.5),
    "lattice": (lattice, "Lattice", ["elegant", "bookish"], (4, 7), 0.5),
    "circles": (circles, "Circles", ["street", "casual"], (2, 4), 0.4),
    "bigdots": (big_dots, "Big dots", ["cute", "casual", "street"], (3, 5), 0.6),
    "confetti": (confetti, "Confetti", ["cute", "casual", "street"], (3, 5), 0.6),
    "bigfloral": (big_floral, "Big florals", ["elegant", "date", "cute"], (2, 4), 1.0),
    "daisies": (daisies, "Daisies", ["cute", "casual"], (3, 6), 0.9),
    "sakura": (sakura, "Cherry blossom", ["cute", "elegant", "date"], (3, 6), 0.8),
    "hibiscus": (hibiscus, "Hibiscus", ["casual", "street", "sporty"], (2, 4), 0.8),
    "tropical": (tropical_leaves, "Tropical leaves", ["casual", "street", "sporty"], (2, 4), 0.8),
    "leaves": (leaves, "Leaves", ["casual", "bookish", "cute"], (3, 6), 0.7),
    "vine": (vine, "Trailing vine", ["elegant", "bookish", "cute"], (2, 3.5), 0.6),
    "paisley": (paisley, "Paisley", ["elegant", "bookish", "gothic", "street"], (3, 5), 0.8),
    "moons": (moons_and_stars, "Moons and stars", ["cute", "gothic"], (2, 4), 0.6),
    "clouds": (clouds, "Clouds", ["cute", "casual"], (2, 4), 0.6),
    "cherries": (cherries, "Cherries", ["cute", "date", "casual"], (2.5, 4), 0.7),
    "bows": (bows, "Bows", ["cute", "elegant"], (3, 6), 0.6),
    "butterflies": (butterflies, "Butterflies", ["cute", "casual", "date"], (3, 5), 0.6),
    "paws": (paw_prints, "Paw prints", ["cute", "casual"], (3, 6), 0.5),
    "strawberries": (strawberries, "Strawberries", ["cute", "casual"], (3, 6), 0.6),
    "raindrops": (raindrops, "Raindrops", ["cute", "casual"], (4, 7), 0.4),
    "snowflakes": (snowflakes, "Snowflakes", ["cute", "casual", "bookish"], (3, 6), 0.4),
    "skulls": (skulls, "Skulls", ["gothic", "street"], (3, 6), 0.7),
    "crosses": (crosses, "Crosses", ["gothic"], (4, 7), 1.0),
    "bats": (bats, "Bats", ["gothic", "cute"], (2, 4), 0.5),
    "spiderweb": (spiderweb, "Spiderweb", ["gothic"], (2, 4), 1.0),
    "lightning": (lightning, "Lightning bolts", ["street", "sporty"], (3, 6), 0.5),
    "flames": (flames, "Flames", ["street", "gothic"], (4, 6), 0.4),
    "camo": (camo, "Camouflage", ["street", "sporty"], (1.5, 3), 0.8),
    "skatecheck": (checkerboard_skate, "Checkerboard", ["street", "sporty"], (3, 5), 0.6),
    "leopard": (leopard, "Leopard", ["street", "gothic", "date"], (3, 5), 0.9),
    "zebra": (zebra, "Zebra", ["street", "gothic"], (2, 4), 0.6),
    "tiger": (tiger, "Tiger", ["street"], (2, 4), 0.4),
    "cow": (cow, "Cow print", ["cute", "street"], (2, 3), 0.4),
    "snakeskin": (snakeskin, "Snakeskin", ["street", "gothic", "elegant"], (5, 9), 0.6),
    "tiedye": (tie_dye, "Tie-dye", ["casual", "street", "sporty"], (1.5, 3), 0.6),
    "marble": (marble, "Marble", ["elegant", "street"], (2, 4), 0.4),
    "brushstrokes": (brushstrokes, "Brushstrokes", ["street", "casual"], (2, 4), 0.5),
    "terrazzo": (terrazzo, "Terrazzo", ["casual", "street", "cute"], (2, 4), 0.5),
    "squiggles": (squiggles, "Squiggles", ["street", "cute", "casual"], (2, 4), 0.5),
    # Added with the second pass over the library.
    "ticking": (ticking, "Ticking stripes", ["casual", "bookish"], (4, 7), 0.5),
    "rugby": (rugby, "Rugby hoops", ["sporty", "casual", "street"], (3, 5), 0.6),
    "pyjama": (pyjama, "Pyjama stripes", ["casual", "bookish", "cute"], (4, 7), 0.6),
    "glenplaid": (glen_plaid, "Glen plaid", ["elegant", "bookish"], (3, 5), 0.7),
    "madras": (madras, "Madras", ["casual", "bookish", "cute"], (2, 4), 0.6),
    "tattersall": (tattersall, "Tattersall", ["bookish", "casual"], (3, 5), 0.5),
    "graph": (graph_paper, "Graph paper", ["bookish", "street"], (2, 4), 0.4),
    "wavycheck": (wavy_check, "Wavy checkerboard", ["street", "cute", "casual"], (2, 4), 0.5),
    "pindots": (pin_dots, "Pin dots", ["elegant", "bookish", "cute"], (3, 5), 0.6),
    "halftone": (halftone, "Halftone", ["street", "sporty"], (2, 4), 0.4),
    "plus": (plus_signs, "Plus signs", ["street", "casual", "cute"], (3, 6), 0.4),
    "seigaiha": (seigaiha, "Seigaiha waves", ["elegant", "cute"], (3, 6), 0.5),
    "quatrefoil": (quatrefoil, "Quatrefoil", ["elegant", "bookish"], (3, 5), 0.5),
    "ikat": (ikat, "Ikat", ["casual", "elegant", "street"], (2, 4), 0.5),
    "aztec": (aztec, "Aztec bands", ["casual", "street", "sporty"], (2, 4), 0.5),
    "opart": (op_art, "Op art squares", ["street", "gothic"], (2, 4), 0.7),
    "roses": (roses, "Cabbage roses", ["elegant", "date", "cute", "gothic"], (2, 4), 0.9),
    "sunflowers": (sunflowers, "Sunflowers", ["cute", "casual"], (2, 4), 0.6),
    "tulips": (tulips, "Tulips", ["cute", "casual", "elegant"], (2, 4), 0.6),
    "lemons": (lemons, "Lemons", ["cute", "casual", "date"], (2, 4), 0.6),
    "mushrooms": (mushrooms, "Toadstools", ["cute", "bookish"], (2, 4), 0.5),
    "ferns": (ferns, "Ferns", ["bookish", "casual", "elegant"], (1.8, 3), 0.5),
    "holly": (holly, "Holly", ["cute", "casual"], (2, 4), 0.3),
    "anchors": (anchors, "Anchors", ["casual", "cute", "bookish"], (3, 5), 0.5),
    "cats": (cats, "Cats", ["cute", "casual"], (3, 5), 0.6),
    "notes": (music_notes, "Music notes", ["cute", "street", "casual"], (2, 3.5), 0.4),
    "rainbows": (rainbows, "Rainbows", ["cute", "casual"], (2, 4), 0.5),
    "lips": (lips, "Kisses", ["date", "street", "cute"], (3, 5), 0.4),
    "planets": (planets, "Planets", ["cute", "street", "bookish"], (2, 4), 0.5),
    "fish": (fish, "Fish", ["cute", "casual", "sporty"], (3, 5), 0.4),
    "seagulls": (seagulls, "Seagulls", ["casual", "cute"], (3, 5), 0.4),
    "bunnies": (bunnies, "Bunnies", ["cute"], (3, 5), 0.9),
    "pumpkins": (pumpkins, "Pumpkins", ["cute", "gothic", "casual"], (2, 4), 0.3),
    "eyes": (eyes, "Eyes", ["gothic", "street"], (2, 4), 0.8),
    "bees": (bees, "Bees", ["cute", "casual"], (3, 5), 0.5),
    "ladybugs": (ladybugs, "Ladybirds", ["cute"], (3, 5), 0.5),
    "shells": (shells, "Shells and starfish", ["casual", "cute", "sporty"], (2, 4), 0.5),
    "smileys": (smileys, "Smileys", ["street", "cute", "sporty"], (3, 5), 0.5),
    "giraffe": (giraffe, "Giraffe", ["street", "casual"], (2, 4), 0.4),
    "dalmatian": (dalmatian, "Dalmatian", ["street", "cute", "gothic"], (2, 4), 0.5),
    "cheetah": (cheetah, "Cheetah", ["street", "date"], (3, 5), 0.5),
    "splatter": (splatter, "Paint splatter", ["street", "casual"], (2, 4), 0.4),
    "shibori": (shibori, "Shibori", ["casual", "elegant"], (2, 4), 0.5),
    "batik": (batik, "Batik", ["casual", "elegant"], (2, 3), 0.4),
    "pixelcamo": (pixel_camo, "Digital camo", ["street", "sporty"], (2, 4), 0.4),
    "watercolour": (watercolour, "Watercolour", ["elegant", "cute", "date"], (1.5, 3), 0.5),
    "fairisle": (fair_isle, "Fair Isle", ["cute", "bookish", "casual"], (2.5, 3.5), 0.7),
    "nordic": (nordic, "Nordic stars", ["cute", "casual", "bookish"], (2.5, 3.5), 0.6),
    "festive": (festive, "Christmas jumper", ["cute", "casual"], (2.5, 3.5), 0.3),
    "knithearts": (knitted_hearts, "Knitted hearts", ["cute", "date", "casual"], (3, 4), 0.4),
    "knitdiamonds": (knitted_diamonds, "Knitted diamonds", ["bookish", "casual", "elegant"], (3, 4), 0.4),
    "knitflakes": (snowflake_yoke, "Snowflake band", ["cute", "casual", "bookish"], (7, 9), 0.4),
    "knitbunnies": (knitted_bunnies, "Knitted bunnies", ["cute"], (3, 4), 0.3),
    "knitstripes": (knitted_stripes, "Knitted stripes", ["casual", "cute", "bookish"], (3, 4.5), 0.5),
    "damask": (damask, "Damask", ["elegant", "gothic", "bookish"], (4, 6), 0.6),
    "fleurdelis": (fleur_de_lis, "Fleur-de-lis", ["elegant", "bookish"], (3, 5), 0.4),
    "icecream": (ice_creams, "Ice creams", ["cute", "casual"], (2.5, 4), 0.4),
    "doughnuts": (doughnuts, "Doughnuts", ["cute", "street", "casual"], (2.5, 4), 0.4),
    "watermelon": (watermelons, "Watermelons", ["cute", "casual", "sporty"], (2.5, 4), 0.4),
    "shippo": (shippo, "Shippo circles", ["elegant", "cute"], (3, 5), 0.4),
    "starlattice": (star_lattice, "Star lattice", ["elegant", "bookish"], (2, 3), 0.4),
    "cacti": (cacti, "Cacti", ["cute", "casual"], (2.5, 4), 0.4),
    "feathers": (feathers, "Feathers", ["casual", "elegant", "street"], (2, 3.5), 0.4),
    "medallions": (medallions, "Medallions", ["elegant", "casual", "date"], (2.5, 4), 0.5),
    # Placed once on the chest (PLACED), not repeated: graphic tees.
    "tee_heart": (graphic_heart, "Heart graphic", ["cute", "casual", "date"], (0.8, 1.25), 0.4),
    "tee_star": (graphic_star, "Star graphic", ["casual", "street", "sporty"], (0.8, 1.25), 0.4),
    "tee_smiley": (graphic_smiley, "Smiley graphic", ["street", "casual", "cute"], (0.8, 1.25), 0.4),
    "tee_cherries": (graphic_cherries, "Cherries graphic", ["cute", "casual"], (0.8, 1.25), 0.3),
    "tee_rose": (graphic_rose, "Rose graphic", ["gothic", "date", "elegant"], (0.8, 1.25), 0.3),
    "tee_sunset": (graphic_sunset, "Sunset graphic", ["casual", "street", "sporty"], (0.8, 1.25), 0.4),
    "tee_lightning": (graphic_lightning, "Lightning graphic", ["street", "sporty"], (0.8, 1.25), 0.3),
    "tee_skull": (graphic_skull, "Skull graphic", ["gothic", "street"], (0.8, 1.25), 0.4),
    "tee_cat": (graphic_cat, "Cat graphic", ["cute", "casual"], (0.8, 1.25), 0.4),
    "tee_rainbow": (graphic_rainbow, "Rainbow graphic", ["cute", "casual"], (0.8, 1.25), 0.3),
    "tee_planet": (graphic_planet, "Planet graphic", ["cute", "street", "bookish"], (0.8, 1.25), 0.3),
    "tee_daisy": (graphic_daisy, "Daisy graphic", ["cute", "casual"], (0.8, 1.25), 0.3),
    "tee_mountains": (graphic_mountains, "Mountains graphic", ["sporty", "casual", "bookish"], (0.8, 1.25), 0.3),
    "tee_paw": (graphic_paw, "Paw graphic", ["cute", "casual", "sporty"], (0.8, 1.25), 0.3),
    "tee_butterfly": (graphic_butterfly, "Butterfly graphic", ["cute", "casual", "date"], (0.8, 1.25), 0.3),
    "tee_bow": (graphic_bow, "Bow graphic", ["cute", "date"], (0.8, 1.25), 0.3),
    "tee_moon": (graphic_moon, "Moon graphic", ["cute", "gothic", "bookish"], (0.8, 1.25), 0.3),
    "tee_crown": (graphic_crown, "Crown graphic", ["street", "date", "cute"], (0.8, 1.25), 0.3),
    "tee_ghost": (graphic_ghost, "Ghost graphic", ["cute", "gothic"], (0.8, 1.25), 0.3),
    "tee_palm": (graphic_palm, "Palm tree graphic", ["casual", "sporty"], (0.8, 1.25), 0.3),
    "tee_anchor": (graphic_anchor, "Anchor graphic", ["casual", "bookish"], (0.8, 1.25), 0.3),
    "tee_strawberry": (graphic_strawberry, "Strawberry graphic", ["cute", "casual"], (0.8, 1.25), 0.3),
    "tee_toadstool": (graphic_toadstool, "Toadstool graphic", ["cute", "bookish"], (0.8, 1.25), 0.3),
    "tee_flame": (graphic_flame, "Flame graphic", ["street", "sporty", "gothic"], (0.8, 1.25), 0.3),
    "tee_lips": (graphic_lips, "Kiss graphic", ["date", "street"], (0.8, 1.25), 0.3),
    "tee_notes": (graphic_notes, "Music graphic", ["street", "casual", "cute"], (0.8, 1.25), 0.3),
    "tee_eye": (graphic_eye, "Eye graphic", ["gothic", "street"], (0.8, 1.25), 0.3),
    "tee_love": (slogan_love, "LOVE slogan", ["cute", "casual", "street"], (0.8, 1.25), 0.3),
    "tee_hello": (slogan_hello, "HELLO slogan", ["casual", "street", "cute"], (0.8, 1.25), 0.3),
    "tee_sunny": (slogan_sunny, "SUNNY slogan", ["casual", "sporty", "cute"], (0.8, 1.25), 0.3),
    "tee_cute": (slogan_cute, "CUTE! slogan", ["cute", "street"], (0.8, 1.25), 0.3),
    "tee_23": (varsity_23, "Varsity number", ["sporty", "street", "casual"], (0.8, 1.25), 0.4),
    "back_23": (varsity_23, "Number on the back", ["sporty", "street"], (0.8, 1.25), 0.3),
    # Small badges on the left chest, as embroidered on a polo or a shirt (PLACED "badge").
    "badge_crown": (graphic_crown, "Crown badge", ["elegant", "bookish", "sporty"], (0.8, 1.25), 0.3),
    "badge_anchor": (graphic_anchor, "Anchor badge", ["bookish", "casual", "sporty"], (0.8, 1.25), 0.3),
    "badge_heart": (graphic_heart, "Heart badge", ["cute", "casual", "date"], (0.8, 1.25), 0.3),
    "badge_star": (graphic_star, "Star badge", ["sporty", "street", "casual"], (0.8, 1.25), 0.3),
    "badge_cherries": (graphic_cherries, "Cherries badge", ["cute", "casual"], (0.8, 1.25), 0.2),
    "badge_paw": (graphic_paw, "Paw badge", ["cute", "sporty"], (0.8, 1.25), 0.2),
    "badge_bow": (graphic_bow, "Bow badge", ["cute", "elegant"], (0.8, 1.25), 0.2),
    "badge_moon": (graphic_moon, "Moon badge", ["gothic", "bookish", "cute"], (0.8, 1.25), 0.2),
    # Big graphics across the back of a tee, a hoodie or a jacket (PLACED "back").
    "back_skull": (graphic_skull, "Skull on the back", ["gothic", "street"], (0.8, 1.25), 0.3),
    "back_flame": (graphic_flame, "Flame on the back", ["street", "sporty", "gothic"], (0.8, 1.25), 0.3),
    "back_crown": (graphic_crown, "Crown on the back", ["street", "date"], (0.8, 1.25), 0.2),
    "back_star": (graphic_star, "Star on the back", ["street", "sporty", "casual"], (0.8, 1.25), 0.3),
    "back_lightning": (graphic_lightning, "Lightning on the back", ["street", "sporty"], (0.8, 1.25), 0.2),
    "back_sunset": (graphic_sunset, "Sunset on the back", ["casual", "street", "sporty"], (0.8, 1.25), 0.3),
    "back_mountains": (graphic_mountains, "Mountains on the back", ["sporty", "casual"], (0.8, 1.25), 0.2),
    "back_rose": (graphic_rose, "Rose on the back", ["gothic", "date", "street"], (0.8, 1.25), 0.2),
    "back_butterfly": (graphic_butterfly, "Butterfly on the back", ["cute", "street", "date"], (0.8, 1.25), 0.2),
    # Dyes over the whole garment (PLACED "body").
    "ombre": (ombre, "Ombre", ["elegant", "date", "casual", "cute"], (1, 1), 0.6),
    "ombreup": (ombre_up, "Reverse ombre", ["elegant", "date", "street"], (1, 1), 0.4),
    "dipdye": (dip_dye, "Dip-dye", ["casual", "street", "cute"], (1, 1), 0.5),
    "sunsetfade": (sunset_fade, "Sunset fade", ["date", "elegant", "casual"], (1, 1), 0.4),
    "colourblock": (colour_block, "Colour block", ["street", "sporty", "casual"], (1, 1), 0.5),
    "hemstripes": (hem_stripes, "Hem stripes", ["sporty", "cute", "bookish", "casual"], (1, 1), 0.5),
    # Bands round the hem or under the neckline (PLACED "hem", "yoke"): tiles per metre round the garment, the band as
    # tall as a tile is wide.
    "hemflowers": (hem_flowers, "Floral border", ["cute", "elegant", "date", "casual"], (6, 8), 0.5),
    "scallop": (scallop_edge, "Scalloped edge", ["cute", "elegant", "date"], (10, 14), 0.5),
    "greekkey": (greek_key, "Greek key border", ["elegant", "bookish"], (8, 11), 0.4),
    "folkband": (folk_band, "Folk embroidery hem", ["cute", "casual", "bookish"], (6, 9), 0.4),
    "ricrac": (ric_rac, "Ric-rac trim", ["cute", "casual"], (12, 18), 0.4),
    "yokeflowers": (yoke_flowers, "Embroidered yoke", ["cute", "casual", "date"], (7, 9), 0.4),
    "folkyoke": (folk_band, "Folk embroidery yoke", ["cute", "casual", "bookish"], (7, 10), 0.3),
    "newsprint": (newsprint, "Newsprint", ["street", "bookish", "casual"], (2, 3), 0.4),
    "script": (script, "Script", ["date", "elegant", "cute"], (2, 3), 0.4),
    "birds": (birds, "Birds on branches", ["elegant", "bookish", "cute"], (2.5, 4), 0.5),
    "groovy": (groovy, "Groovy waves", ["street", "casual", "cute"], (1.5, 3), 0.4),
    "flowerpower": (flower_power, "Flower power", ["cute", "casual", "street"], (2, 3.5), 0.5),
    "dinosaurs": (dinosaurs, "Dinosaurs", ["cute", "casual"], (2.5, 4), 0.3),
    "sailboats": (sailboats, "Sailboats", ["casual", "cute", "bookish"], (2.5, 4), 0.4),
    "umbrellas": (umbrellas, "Umbrellas", ["cute", "casual", "bookish"], (2.5, 4), 0.4),
    "liberty": (liberty, "Small floral", ["cute", "bookish", "elegant", "date"], (3, 5), 0.9),
    "poppies": (poppies, "Poppies", ["cute", "casual", "date"], (2, 3.5), 0.5),
    "lavender": (lavender, "Lavender", ["cute", "bookish", "elegant"], (1.6, 2.6), 0.5),
    "maple": (maple_leaves, "Autumn leaves", ["cute", "bookish", "casual"], (2.5, 4), 0.4),
    "pineapples": (pineapples, "Pineapples", ["cute", "casual", "sporty"], (2.5, 4), 0.4),
    "memphis": (memphis, "Memphis", ["street", "cute", "casual"], (2, 3.5), 0.4),
    "regimental": (regimental, "Regimental stripes", ["bookish", "elegant", "casual"], (4, 6), 0.5),
    "quilt": (quilt_blocks, "Quilt blocks", ["cute", "casual", "bookish"], (2, 3), 0.4),
    "spots": (painted_spots, "Painted spots", ["cute", "casual", "street"], (3, 5), 0.6),
    "graffiti": (graffiti, "Graffiti", ["street", "sporty"], (2, 3.5), 0.5),
    "swooshes": (swooshes, "Speed swooshes", ["sporty", "street"], (2, 3.5), 0.5),
    "thorns": (thorns, "Thorny vines", ["gothic", "elegant"], (2, 3.5), 0.5),
    "chains": (chains, "Chain links", ["date", "street", "elegant"], (2.5, 4), 0.5),
    "rosebuds": (rosebuds, "Rosebuds", ["date", "cute", "elegant"], (3, 5), 0.6),
    "keys": (keys, "Skeleton keys", ["gothic", "bookish"], (2.5, 4), 0.4),
    "triplestripes": (triple_stripes, "Triple stripes", ["sporty", "street"], (3, 5), 0.5),
    "artdeco": (art_deco, "Art deco fans", ["elegant", "date"], (3, 5), 0.9),
    "bandana": (bandana, "Bandana", ["street", "casual"], (2.5, 4), 0.5),
    "linebotanical": (line_botanicals, "Botanical sketches", ["bookish", "elegant", "casual"], (1.8, 3), 0.5),
    "dogs": (dogs, "Dogs", ["cute", "casual"], (3, 5), 0.5),
    "frogs": (frogs, "Frogs", ["cute", "casual"], (3, 5), 0.4),
    "pearls": (pearls, "Pearls", ["date", "elegant"], (2.5, 4), 0.5),
    "ravens": (ravens, "Ravens", ["gothic"], (2.5, 4), 0.5),
    "swallows": (swallows, "Swallows", ["street", "date", "gothic"], (2.5, 4), 0.5),
    "foulard": (foulard, "Foulard", ["elegant", "bookish"], (4, 6), 0.6),
    "toile": (toile, "Toile", ["elegant", "bookish", "cute", "date"], (2.2, 3.2), 0.6),
    "azulejo": (azulejo, "Azulejo tiles", ["elegant", "casual", "date"], (2, 3), 0.6),
    "whales": (whales, "Whales", ["cute", "casual"], (2.5, 4), 0.6),
    "celestial": (celestial, "Suns and moons", ["casual", "gothic", "date", "cute"], (2.5, 4), 0.7),
    "clovers": (clovers, "Clovers", ["cute", "casual"], (3, 5), 0.6),
    "dragonflies": (dragonflies, "Dragonflies", ["cute", "elegant", "casual", "date"], (2.5, 4), 0.6),
    "avocados": (avocados, "Avocados", ["cute", "casual", "street"], (2.2, 3.5), 0.5),
    "lollies": (lollies, "Ice lollies", ["cute", "casual"], (2.5, 4), 0.5),
    "bananas": (bananas, "Bananas", ["cute", "casual", "street"], (2.5, 4), 0.5),
}

# How each print's second colour (its accent: leaves, hearts, wafers) follows its first: "leaf" (a natural green),
# "gold", "light" (the first, paled) or "contrast" (the first's opposite hue).
ACCENT = {
    "floral": "leaf", "bigfloral": "leaf", "hibiscus": "leaf", "roses": "leaf", "tulips": "leaf", "lemons": "leaf",
    "holly": "leaf", "watermelon": "leaf", "strawberries": "leaf", "daisies": "gold", "icecream": "gold",
    "doughnuts": "gold", "mushrooms": "light", "bees": "light", "planets": "contrast", "rainbows": "contrast",
    "plaid": "contrast", "glenplaid": "contrast", "argyle": "contrast", "madras": "contrast",
    "cacti": "leaf", "medallions": "contrast", "groovy": "contrast", "flowerpower": "gold", "dinosaurs": "contrast",
    "sailboats": "contrast", "umbrellas": "contrast", "liberty": "contrast", "lavender": "leaf", "maple": "gold",
    "pineapples": "leaf", "memphis": "contrast", "regimental": "contrast", "quilt": "contrast", "graffiti": "contrast",
    "swooshes": "contrast", "thorns": "leaf", "rosebuds": "leaf", "triplestripes": "contrast", "artdeco": "gold", "dogs": "contrast", "frogs": "leaf", "pearls": "light", "swallows": "light", "foulard": "contrast", "azulejo": "light", "whales": "light", "celestial": "gold", "dragonflies": "light", "avocados": "contrast", "lollies": "contrast",
    "tee_heart": "light", "tee_star": "light", "tee_cherries": "leaf", "tee_rose": "leaf", "tee_sunset": "gold",
    "tee_cat": "light", "tee_rainbow": "contrast", "tee_planet": "contrast", "tee_daisy": "gold", "tee_mountains": "light",
    "tee_butterfly": "contrast", "tee_moon": "gold", "tee_crown": "contrast", "tee_ghost": "light", "tee_palm": "leaf",
    "tee_anchor": "gold", "tee_strawberry": "leaf", "tee_toadstool": "light", "tee_flame": "gold", "tee_lips": "light",
    "tee_eye": "contrast",
    "badge_crown": "contrast", "badge_heart": "light", "badge_star": "light", "badge_cherries": "leaf", "badge_anchor": "gold",
    "badge_moon": "gold",
    "back_star": "light", "back_sunset": "gold", "back_mountains": "light", "back_rose": "leaf", "back_flame": "gold",
    "back_crown": "contrast", "back_butterfly": "contrast",
    "sunsetfade": "contrast", "colourblock": "contrast", "confetti": "contrast", "brushstrokes": "contrast",
    "hemflowers": "leaf", "folkband": "contrast", "ricrac": "contrast", "yokeflowers": "leaf", "folkyoke": "contrast",
}

# Prints placed once rather than repeated, and where: "chest", a graphic on the front of a casual top or dress;
# "badge", a small one on the left chest of any top or dress; "back", a big one across the back of a casual top,
# dress or jacket; "body", over the whole garment from its top to its hem; "hem" and "yoke", a band repeating round the
# garment just above its hem or under its neckline.
PLACED = {pid: {"tee_": "chest", "badge_": "badge", "back_": "back"}[pid[:pid.index("_") + 1]]
          for pid in PRINTS if pid.startswith(("tee_", "badge_", "back_"))}
PLACED.update({pid: "body" for pid in ("ombre", "ombreup", "dipdye", "sunsetfade", "colourblock", "hemstripes")})
PLACED.update({pid: "hem" for pid in ("hemflowers", "scallop", "greekkey", "folkband", "ricrac")})
PLACED.update({pid: "yoke" for pid in ("yokeflowers", "folkyoke", "knitflakes")})

# Prints knitted into the cloth (colourwork, stripes, intarsia) rather than printed on it: the only ones a knit takes
# (fabrics.PRINT_TAGS). They carry the tag "knitted" in the library.
KNITTED = {"stripes", "breton", "argyle", "knit", "snowflakes", "rugby", "hearts", "stars", "fairisle", "nordic", "festive",
           "knithearts", "knitdiamonds", "knitflakes", "knitbunnies", "knitstripes"}

# The occasions prints suit, as extra tags the outfit contexts weight (sleep, swim, formal): pyjama prints, beach
# prints, gala prints.
CONTEXT_TAGS = {
    "sleep": ["celestial", "whales", "foulard", "toile", "clouds", "moons", "stars", "hearts", "dots", "pindots", "gingham", "check", "pyjama", "bunnies", "cats",
              "raindrops", "snowflakes", "plaid", "buffalo", "candystripe", "floral", "daisies", "cherries",
              "strawberries", "bows", "paws", "rainbows", "planets", "sakura", "ticking", "notes", "smileys", "seigaiha",
              "dinosaurs", "sailboats", "rosebuds", "dogs", "frogs"],
    "swim": ["lollies", "whales", "azulejo", "pineapples", "sailboats", "tropical", "hibiscus", "stripes", "breton", "waves", "scales", "shells", "fish", "seagulls", "anchors",
             "lemons", "watermelon", "floral", "bigfloral", "dots", "gingham", "leopard", "zebra", "cheetah", "tiedye",
             "cherries", "strawberries", "daisies", "sunsetfade", "ombre", "colourblock", "ricrac", "tee_palm", "icecream"],
    "rain": ["raindrops", "clouds", "umbrellas", "check", "gingham", "plaid", "buffalo", "houndstooth", "dots",
             "stripes", "breton", "anchors", "fish", "frogs"],
    "formal": ["foulard", "pearls", "artdeco", "chains", "rosebuds", "damask", "bigfloral", "roses", "ombre", "sunsetfade", "scallop", "greekkey", "medallions", "fleurdelis",
               "paisley", "seigaiha", "shippo", "quatrefoil", "lattice", "starlattice", "watercolour", "hemflowers"],
}

# Classic colourways: (how often an item with the print takes one, [(the cloth's colour, the print's)]).
COLOURWAYS = {
    "celestial": (0.4, [("#1d2147", "#f2dc9b"), ("#141414", "#e8d08a")]),
    "azulejo": (0.6, [("#f5f3ee", "#1f4e9c"), ("#f6f1e4", "#1c6b8a")]),
    "toile": (0.6, [("#f1ebdd", "#2a3f73"), ("#f2ebe0", "#9b2335"), ("#f4f1ea", "#222222")]),
    "leopard": (0.6, [("#c79a5b", "#2b1c12"), ("#d8c9b0", "#3a3a3a"), ("#b08850", "#1a1410")]),
    "cheetah": (0.6, [("#d1a666", "#24180f")]),
    "zebra": (0.6, [("#f2efe8", "#161616")]),
    "tiger": (0.6, [("#e08a2e", "#1c1410")]),
    "cow": (0.6, [("#f4f1ea", "#1d1d1d"), ("#f2ede3", "#6b3f22")]),
    "dalmatian": (0.6, [("#f6f4ef", "#141414")]),
    "giraffe": (0.6, [("#efe0c0", "#9a6232")]),
    "snakeskin": (0.5, [("#d7c6a5", "#5a4632"), ("#9a9a96", "#232323")]),
    "camo": (0.6, [("#5d6440", "#b3a47a"), ("#6b6f5a", "#3b3a2c")]),
    "pixelcamo": (0.5, [("#6f7766", "#3f4a3a"), ("#7d8aa0", "#3a4558")]),
    "breton": (0.6, [("#f4f1ea", "#22305a"), ("#f4f1ea", "#b0232a")]),
    "stripes": (0.3, [("#f4f1ea", "#22305a")]),
    "rugby": (0.4, [("#1f2c56", "#f2ede0"), ("#13472f", "#f2ede0")]),
    "gingham": (0.5, [("#f6f4ef", "#c22a33"), ("#f6f4ef", "#2e5ea8"), ("#f6f4ef", "#1c1c1c")]),
    "buffalo": (0.6, [("#b5232b", "#151515"), ("#1c1c1c", "#d8d8d8")]),
    "houndstooth": (0.5, [("#efece4", "#1a1a1a")]),
    "pinstripe": (0.5, [("#1f2a44", "#e8e8e8"), ("#3a3a3e", "#e8e8e8")]),
    "chalkstripe": (0.5, [("#2f3036", "#e8e6e0"), ("#1f2a44", "#e8e6e0")]),
    "glenplaid": (0.5, [("#d9d6cf", "#3a3a3d")]),
    "dots": (0.4, [("#1f2a44", "#f4f1ea"), ("#b0232a", "#f4f1ea"), ("#151515", "#f4f1ea")]),
    "pindots": (0.4, [("#1f2a44", "#f4f1ea"), ("#151515", "#f4f1ea")]),
    "seigaiha": (0.4, [("#22305a", "#f2efe8")]),
    "shippo": (0.4, [("#22305a", "#f2efe8"), ("#8a1f2b", "#e9c46a")]),
    "fairisle": (0.4, [("#efe7d6", "#8a1f2b"), ("#22305a", "#efe7d6")]),
    "nordic": (0.5, [("#9d1d26", "#f4f1ea"), ("#22305a", "#f4f1ea")]),
    "festive": (0.6, [("#a51f27", "#f4f1ea"), ("#1e5a36", "#f4f1ea")]),
    "anchors": (0.4, [("#f4f1ea", "#22305a"), ("#22305a", "#f4f1ea")]),
    "ticking": (0.5, [("#f1ece0", "#2a3f6e")]),
    "chains": (0.6, [("#f4efe4", "#b8902e"), ("#1c1c1f", "#c9a24a"), ("#22305a", "#c9a24a")]),
    "bandana": (0.6, [("#b5232b", "#f4f1ea"), ("#22305a", "#f4f1ea"), ("#151515", "#f4f1ea")]),
}

# Animal skins and hides: the only prints a leather takes (fabrics.PRINT_TAGS). They carry the tag "animal".
ANIMAL = {"leopard", "zebra", "tiger", "cow", "snakeskin", "giraffe", "dalmatian", "cheetah"}
