"""Prints: patterns printed or woven onto a garment's main fabric, in the pattern colour (decision 0008's catalog
"patterns"). Each function takes the tile size in pixels and a seed and returns (colour, ink), floats in [0, 1]:
``colour`` is how much of the pattern colour shows (0.5 a half blend with the cloth, for a second tone), ``ink`` how
dark the print's drawn lines and shading are. Tiles repeat seamlessly; the catalog sets how many per metre.

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
    """Shapes (list of (colour, ink) small arrays) stamped at spread-out points, rotated at random."""
    colour, ink = _zero(size), _zero(size)
    rng = np.random.default_rng(seed)
    for i, (cx, cy) in enumerate(tk.scatter(size, count, seed, gap)):
        c, k = shapes[i % len(shapes)]
        angle = rng.random() * 360 if rotate else 0.0
        if rotate:
            c, k = tk.rotate_shape(c, angle), tk.rotate_shape(k, angle)
        tk.stamp(colour, c, cx, cy, mode="max")
        tk.stamp(ink, k, cx, cy, mode="max")
    return colour, ink


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
    chalk = _band(x, 0.5, 0.025, 0.015) * (0.7 + 0.3 * tk.value_noise(size, 30, seed, 1))
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
    over = _band(x, 0.88, 0.03, 0.004) + _band(y, 0.88, 0.03, 0.004)
    twill = 0.5 + 0.5 * np.sin(2 * np.pi * 96 * (x * 0.5 + y))
    colour = np.clip(wide + mid + 0.6 * np.clip(over, 0, 1), 0, 1)
    ink = np.clip(0.55 * lines + 0.25 * wide * wide * twill, 0, 1)
    return colour, ink


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
    alt = (np.abs(((x + 0.5) % 1) - 0.5) + np.abs(((y + 0.5) % 1) - 0.5) < 0.5).astype(float)
    lines = np.maximum(_band((x + y) % 1.0, 0.5, 0.02, 0.004), _band((x - y) % 1.0, 0.5, 0.02, 0.004))
    return np.clip(diamond * 0.9 + alt * 0.0, 0, 1) * (1 - lines) + 0.0, lines * 0.8


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
    edges = 1 - tk.smoothstep(0.0, 0.12, second - best)
    return 0.55 * (1 - edges), edges * 0.85


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
    rng = np.random.default_rng(seed)
    shapes = []
    for k in range(6):
        dot = tk.disc(size / 40 * (0.7 + 0.6 * rng.random()), 1.0) * (0.5 if k % 2 else 1.0)
        shapes.append((dot, _zero(dot.shape[0])))
    colour, ink = _scatter_shapes(size, shapes, 50, seed, size / 10, rotate=False)
    return colour, ink


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
    shapes.append((lb * 0.5, li))
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
    shapes += [(lb * 0.45, li), (lb * 0.45, li)]
    return _scatter_shapes(size, shapes, 8, seed, size / 3.6)


def daisies(size, seed):
    b, i = _petal_flower(int(size / 5), 12, 0.15)
    r = b.shape[0] // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    centre = np.clip((r * 0.22 - np.hypot(x, y)) / 1.2 + 0.5, 0, 1)
    colour = np.clip(b - centre * 0.5, 0, 1)
    ink = np.clip(i * 0.7 + centre * 0.4, 0, 1)
    return _scatter_shapes(size, [(colour, ink)], 10, seed, size / 3.6)


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
    return _scatter_shapes(size, [(b, np.clip(i + stamen, 0, 1)), (lb * 0.5, li), (mb * 0.5, mi)], 7, seed, size / 3.6)


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
        ink = np.clip(_outline(shape, 1) + seeds * 0.9 + (leaf_tops | stem) * 0.85, 0, 1)
        return shape, ink
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
    px = int(size / 6)
    c = _shape(px, lambda x, y: _soft(np.minimum(np.maximum(np.abs(x) - 0.14, np.abs(y) - 0.8), np.maximum(np.abs(x) - 0.5, np.abs(y + 0.3) - 0.14)), 20))
    return _scatter_shapes(size, [(c, _outline(c) * 0.4)], 14, seed, size / 4.5, rotate=False)


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
    spokes = np.exp(-(np.sin(a * 4) * r / 0.006) ** 2)
    rings = _band((r * 9 + 0.15 * np.cos(a * 8) * r * 4) % 1.0, 0.5, 0.05, 0.02)
    web = np.clip(spokes + rings, 0, 1) * (r < 0.5)
    return web, web * 0.3


def lightning(size, seed):
    px = int(size / 4)
    pts = [(-0.3, -0.9), (0.35, -0.9), (0.05, -0.15), (0.45, -0.15), (-0.35, 0.95), (-0.05, 0.1), (-0.45, 0.1)]
    bolt = tk.polygon_mask([(p[0] * px / 2, p[1] * px / 2) for p in pts], px | 1)
    return _scatter_shapes(size, [(bolt, _outline(bolt) * 0.5)], 9, seed, size / 3.5)


def flames(size, seed):
    x, y = tk.grid(size)
    n = tk.value_noise(size, 6, seed, 3)
    tongues = 0.5 + 0.5 * np.sin(2 * np.pi * (x * 4 + n * 0.6))
    height = (1 - y) * (0.6 + 0.5 * tongues) - 0.35
    body = tk.smoothstep(-0.02, 0.02, height) * (y > 0.02)
    core = tk.smoothstep(0.12, 0.16, height)
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
    fade = np.clip(1.0 - r * 1.2, 0, 1)
    # Tileable: blend the swirl into plain bleed towards the tile's edges.
    return np.clip(swirl * (0.35 + 0.65 * fade) + (1 - fade) * 0.25, 0, 1), _zero(size)


def marble(size, seed):
    x, y = tk.grid(size)
    n = tk.value_noise(size, 4, seed, octaves=5, persistence=0.55)
    veins = np.abs(np.sin(2 * np.pi * (x * 2 + n * 2.5)))
    return np.clip(1 - veins, 0, 1) ** 4, (veins < 0.04).astype(float) * 0.5


def brushstrokes(size, seed):
    rng = np.random.default_rng(seed)
    colour, ink = _zero(size), _zero(size)
    for k, (cx, cy) in enumerate(tk.scatter(size, 16, seed, size / 5)):
        length = int(size * (0.18 + 0.12 * rng.random())) | 1
        h = length // 2
        yy, xx = np.mgrid[-h:h + 1, -h:h + 1].astype(float) / h
        width = (0.16 + 0.08 * rng.random()) * (1 - xx ** 6) * (1 + 0.15 * np.sin(xx * 3))
        bristles = 0.75 + 0.25 * np.sin(yy * 40 + rng.random() * 6) * (0.5 + 0.5 * xx)
        stroke = (np.abs(yy - 0.12 * np.sin(xx * 2.5)) < width).astype(float) * bristles
        stroke = tk.rotate_shape(stroke, rng.random() * 180)
        tk.stamp(colour, stroke * (1.0 if k % 3 else 0.5), cx, cy)
    return colour, ink


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
            arm = np.abs(np.sin(a * 3)) * r < 0.06
            branch = (np.abs(np.sin(a * 3 + 0.5)) * r < 0.05) & (r > 0.4) & (r < 0.7)
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
    over = np.maximum(_band(x, 0.75, 0.012, 0.003), _band(y, 0.75, 0.012, 0.003))
    return np.clip(colour, 0, 1), over * 0.6


def madras(size, seed):
    """Madras: a summery plaid of uneven bands in several tones."""
    x, y = tk.grid(size)
    bands = [(0.1, 0.16, 1.0), (0.3, 0.06, 0.5), (0.45, 0.2, 0.7), (0.66, 0.04, 1.0), (0.8, 0.12, 0.35)]
    a = np.maximum.reduce([v * _band(x, c, w, 0.004) for c, w, v in bands])
    b = np.maximum.reduce([v * _band(y, c, w, 0.004) for c, w, v in bands])
    return np.clip(0.55 * a + 0.55 * b, 0, 1), np.clip(a * b, 0, 1) * 0.3


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
    return np.clip((0.14 - r) * px + 0.5, 0, 1), _zero(size)


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
    colour = np.clip(head + 0.5 * leaves, 0, 1)
    ink = np.clip(_outline(head, 1) + swirl * 0.75 + _outline(leaves, 1) * 0.6, 0, 1)
    return colour, ink


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
        colour = np.clip(cup + 0.5 * leaf, 0, 1)
        ink = np.clip(_outline(cup, 1) + stem * 0.8 + petal_lines * 0.6 + _outline(leaf, 1) * 0.5, 0, 1)
        return colour, ink
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
        colour = np.clip(fruit - 0.7 * shine + 0.5 * leaf, 0, 1)
        return colour, np.clip(_outline(fruit, 1) * 0.6 + _outline(leaf, 1) * 0.5, 0, 1)
    return _scatter_shapes(size, [lemon(int(size / 3.4)), lemon(int(size / 4.6))], 10, seed, size / 3.8)


def mushrooms(size, seed):
    """Toadstools: spotted caps on stout stems (cottagecore)."""
    def toadstool(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        cap = _soft(np.hypot(x / 0.8, (y + 0.02) / 0.62) - 1, r * 0.6) * (y < 0.05)
        stem = _soft(np.maximum(np.abs(x) - 0.24 - 0.06 * (y - 0.05), np.abs(y - 0.45) - 0.4), r) * (1 - cap)
        spots = sum(_soft(np.hypot(x - sx, y - sy) - sr, r) for sx, sy, sr in ((-0.35, -0.2, 0.12), (0.2, -0.35, 0.1), (0.45, -0.08, 0.08), (-0.05, -0.08, 0.07)))
        colour = np.clip(cap - spots + 0.4 * stem, 0, 1)
        ink = np.clip(_outline(cap, 1) + _outline(stem, 1) * 0.7, 0, 1)
        return colour, ink
    return _scatter_shapes(size, [toadstool(int(size / 4.5)), toadstool(int(size / 6.5))], 11, seed, size / 4.2, rotate=False)


def ferns(size, seed):
    """Fern fronds: curving stems of leaflets, smaller towards the tip (botanical prints)."""
    def frond(px):
        r = px // 2
        y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
        bend = 0.18 * (y + 1) ** 2 - 0.18
        xs = x - bend
        stem = (np.abs(xs) < 0.025) & (np.abs(y) < 0.95)
        leaflets = np.zeros_like(x)
        for k in range(9):
            t = -0.85 + k * 0.2
            w = 0.36 * (1 - (t + 0.95) / 2.2)
            for side in (-1, 1):
                cx = side * (w * 0.6)
                leaflets = np.maximum(leaflets, _soft(np.hypot((xs - cx) / max(w * 0.6, 0.05), (y - t - side * 0.03) / 0.07) - 1, r * 0.08))
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
        colour = np.clip(0.5 * leaves * (1 - berries) + berries, 0, 1)
        ink = np.clip(_outline(leaves, 1) * 0.6 + _outline(berries, 1) * 0.5, 0, 1)
        return colour, ink
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
        tone = np.where(np.floor((d - 0.36) / 0.18) % 2 == 0, 1.0, 0.5)
        clouds = sum(_soft(np.hypot(x - sx, y - 0.4) - 0.2, r) for sx in (-0.75, -0.55, 0.55, 0.75))
        clouds = np.clip(clouds, 0, 1)
        colour = np.clip(bands * tone * (1 - clouds), 0, 1)
        return tk.blur_small(colour, 0.7), np.clip(_outline(bands.astype(float), 1) * 0.4 + _outline(clouds, 1) * 0.6, 0, 1)
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
        return np.clip(shape * (1 - ring) + 0.5 * ring, 0, 1), np.clip(_outline(shape, 1) * 0.5 + _outline(ring.astype(float), 1) * 0.5, 0, 1)
    colour, ink = _scatter_shapes(size, [planet(int(size / 3.5)), planet(int(size / 5.5))], 6, seed, size / 3)
    return np.maximum(colour, stars(size, seed + 5)[0] * 0.5), ink


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
        colour = tk.blur_small(body.astype(float) + wings * 0.5, 0.8)
        return np.clip(colour, 0, 1), np.clip(stripes * 0.85 + _outline(body.astype(float), 1) * 0.5 + _outline(wings.astype(float), 1) * 0.4 + sting, 0, 1)
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
    "buffalo": (buffalo_check, "Buffalo check", ["street", "casual", "gothic"], (3, 5), 0.8),
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
    "vine": (vine, "Trailing vine", ["elegant", "bookish", "cute"], (3, 5), 0.6),
    "paisley": (paisley, "Paisley", ["elegant", "bookish", "gothic", "street"], (3, 5), 0.8),
    "moons": (moons_and_stars, "Moons and stars", ["cute", "gothic"], (2, 4), 0.6),
    "clouds": (clouds, "Clouds", ["cute", "casual"], (2, 4), 0.6),
    "cherries": (cherries, "Cherries", ["cute", "date", "casual"], (3, 6), 0.7),
    "bows": (bows, "Bows", ["cute", "elegant"], (3, 6), 0.6),
    "butterflies": (butterflies, "Butterflies", ["cute", "casual", "date"], (3, 5), 0.6),
    "paws": (paw_prints, "Paw prints", ["cute", "casual"], (3, 6), 0.5),
    "strawberries": (strawberries, "Strawberries", ["cute", "casual"], (3, 6), 0.6),
    "raindrops": (raindrops, "Raindrops", ["cute", "casual"], (4, 7), 0.4),
    "snowflakes": (snowflakes, "Snowflakes", ["cute", "casual", "bookish"], (3, 6), 0.4),
    "skulls": (skulls, "Skulls", ["gothic", "street"], (3, 6), 0.7),
    "crosses": (crosses, "Crosses", ["gothic"], (4, 7), 0.5),
    "bats": (bats, "Bats", ["gothic", "cute"], (2, 4), 0.5),
    "spiderweb": (spiderweb, "Spiderweb", ["gothic"], (2, 4), 0.4),
    "lightning": (lightning, "Lightning bolts", ["street", "sporty"], (3, 6), 0.5),
    "flames": (flames, "Flames", ["street", "gothic"], (2, 3), 0.4),
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
    "opart": (op_art, "Op art squares", ["street", "gothic"], (2, 4), 0.3),
    "roses": (roses, "Cabbage roses", ["elegant", "date", "cute", "gothic"], (2, 4), 0.9),
    "sunflowers": (sunflowers, "Sunflowers", ["cute", "casual"], (2, 4), 0.6),
    "tulips": (tulips, "Tulips", ["cute", "casual", "elegant"], (2, 4), 0.6),
    "lemons": (lemons, "Lemons", ["cute", "casual", "date"], (2, 4), 0.6),
    "mushrooms": (mushrooms, "Toadstools", ["cute", "bookish"], (2, 4), 0.5),
    "ferns": (ferns, "Ferns", ["bookish", "casual", "elegant"], (2, 4), 0.5),
    "holly": (holly, "Holly", ["cute", "casual"], (2, 4), 0.3),
    "anchors": (anchors, "Anchors", ["casual", "cute", "bookish"], (3, 5), 0.5),
    "cats": (cats, "Cats", ["cute", "casual"], (3, 5), 0.6),
    "notes": (music_notes, "Music notes", ["cute", "street", "casual"], (3, 5), 0.4),
    "rainbows": (rainbows, "Rainbows", ["cute", "casual"], (2, 4), 0.5),
    "lips": (lips, "Kisses", ["date", "street", "cute"], (3, 5), 0.4),
    "planets": (planets, "Planets", ["cute", "street", "bookish"], (2, 4), 0.5),
    "fish": (fish, "Fish", ["cute", "casual", "sporty"], (3, 5), 0.4),
    "seagulls": (seagulls, "Seagulls", ["casual", "cute"], (3, 5), 0.4),
    "bunnies": (bunnies, "Bunnies", ["cute"], (3, 5), 0.5),
    "pumpkins": (pumpkins, "Pumpkins", ["cute", "gothic", "casual"], (2, 4), 0.3),
    "eyes": (eyes, "Eyes", ["gothic", "street"], (2, 4), 0.4),
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
}

# Prints knitted into the cloth (colourwork, stripes, intarsia) rather than printed on it: the only ones a knit takes
# (fabrics.PRINT_TAGS). They carry the tag "knitted" in the library.
KNITTED = {"stripes", "breton", "argyle", "knit", "snowflakes", "rugby", "hearts", "stars", "fairisle", "nordic"}
