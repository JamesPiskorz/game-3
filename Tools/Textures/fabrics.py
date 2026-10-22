"""Fabric surfaces: what a cloth is made of, drawn as a seamless grey tile that shades the garment's own colours (the
weave of denim, the stitches of a knit, the grain of leather). Each function takes the tile size in pixels and a seed
and returns a float image in [0, 1]: 1 is the cloth's full colour, darker values the shadows of its structure.

Every tile covers TILE_METRES of cloth, so a fabric's structure is the same size on every garment. Sizes are a little
bigger than life (a knit stitch is 7 mm, denim's twill 5 mm), so they read on a cel-shaded character at game distance.
All frequencies are whole numbers per tile, so every tile repeats without a seam."""

import numpy as np

import texture_kit as tk

TILE_METRES = 0.25


def _shade(structure, depth):
    """A structure in [0, 1] (1 raised, 0 sunk) as cloth shading: full colour on top, ``depth`` darker in the hollows."""
    return 1.0 - depth * (1.0 - np.clip(structure, 0.0, 1.0))


def _twill(size, lines, steep=0.5):
    """Diagonal ribs, ``lines`` per tile down (``lines * steep`` must be whole)."""
    x, y = tk.grid(size)
    return 0.5 + 0.5 * np.sin(2 * np.pi * lines * (x * steep + y))


def _weave(size, n):
    """A plain weave, thread over thread: n threads each way per tile."""
    x, y = tk.grid(size)
    warp = 0.5 + 0.5 * np.cos(2 * np.pi * n * x)
    weft = 0.5 + 0.5 * np.cos(2 * np.pi * n * y)
    over = ((np.floor(x * n) + np.floor(y * n)) % 2 == 0)
    # Each crossing: the thread on top is rounded (bright in the middle of its span), the one below hidden.
    span_x = np.sin(np.pi * ((x * n) % 1.0))
    span_y = np.sin(np.pi * ((y * n) % 1.0))
    return np.where(over, warp * span_y, weft * span_x)


# ----------------------------------------------------------------------------------------------- woven cottons, linens

def plain(size, seed):
    """Smooth cotton (poplin, broadcloth): barely any texture, a soft mottle."""
    return _shade(tk.value_noise(size, 6, seed, octaves=3), 0.07)


def poplin(size, seed):
    """Poplin shirting: a very fine, faint rib across."""
    x, y = tk.grid(size)
    rib = 0.5 + 0.5 * np.sin(2 * np.pi * 90 * y)
    return _shade(0.6 * rib + 0.4 * tk.value_noise(size, 8, seed, 3), 0.08)


def oxford(size, seed):
    """Oxford cloth: a basket weave, pairs of threads over pairs, with a heathered look."""
    x, y = tk.grid(size)
    n = 30
    over = ((np.floor(x * n) + np.floor(y * n)) % 2 == 0)
    sub_x = 0.5 + 0.5 * np.cos(4 * np.pi * n * x)
    sub_y = 0.5 + 0.5 * np.cos(4 * np.pi * n * y)
    weave = np.where(over, sub_x, sub_y)
    heather = tk.value_noise(size, 120, seed, 1)
    return _shade(tk.normalise(0.7 * weave + 0.3 * heather), 0.18)


def chambray(size, seed):
    """Chambray: a fine plain weave of white and coloured threads, so it looks softly flecked."""
    weave = _weave(size, 64)
    rng = np.random.default_rng(seed)
    threads = rng.random(64)
    x, y = tk.grid(size)
    light = threads[(np.floor(x * 64)).astype(int) % 64]
    return _shade(tk.normalise(0.6 * weave + 0.4 * light), 0.20)


def canvas(size, seed):
    """Canvas: a coarse plain weave with uneven (slubby) threads."""
    slub = 0.5 * tk.stretched_noise(size, 3, 56, seed) + 0.5 * tk.stretched_noise(size, 56, 3, seed + 1)
    return _shade(tk.normalise(0.7 * _weave(size, 56) + 0.3 * slub), 0.26)


def linen(size, seed):
    """Linen: a looser weave than canvas with strong slubs and soft creases."""
    slub = 0.5 * tk.stretched_noise(size, 2, 44, seed) + 0.5 * tk.stretched_noise(size, 44, 2, seed + 1)
    creases = tk.normalise(tk.blur(tk.stretched_noise(size, 3, 8, seed + 5), 2))
    structure = 0.55 * _weave(size, 44) + 0.45 * slub
    return np.clip(_shade(tk.normalise(structure), 0.24) * (0.93 + 0.07 * creases), 0, 1)


def gauze(size, seed):
    """Cotton gauze and cheesecloth: an open, crinkled weave with gaps between the threads."""
    x, y = tk.grid(size)
    n = 36
    gaps = np.minimum(np.abs(np.sin(np.pi * n * x)), np.abs(np.sin(np.pi * n * y)))
    crinkle = tk.value_noise(size, 10, seed, 3)
    return _shade(tk.normalise(0.7 * (1 - gaps) + 0.3 * crinkle), 0.25)


def seersucker(size, seed):
    """Seersucker: puckered stripes alternating with flat ones."""
    x, y = tk.grid(size)
    stripe = (np.floor(x * 20) % 2 == 0)
    return np.where(stripe, _shade(tk.value_noise(size, 40, seed, octaves=2), 0.28),
                    _shade(tk.value_noise(size, 8, seed + 1, 2), 0.05))


def eyelet(size, seed):
    """Broderie anglaise (eyelet): little embroidered holes in flower clusters on cotton."""
    canvas_ = np.zeros((size, size))
    hole = tk.disc(size / 64, 1.0)
    ring = tk.ring(size / 44, size / 64)
    step = size / 6
    for row in range(6):
        for col in range(6):
            cx, cy = (col + (0.5 if row % 2 else 0)) * step, (row + 0.5) * step
            for k in range(6):
                a = k * np.pi / 3
                px, py = cx + np.cos(a) * size / 22, cy + np.sin(a) * size / 22
                tk.stamp(canvas_, ring, px, py, 0.6, mode="max")
                tk.stamp(canvas_, hole, px, py, 1.0, mode="max")
            tk.stamp(canvas_, hole * 0.8, cx, cy, 1.0, mode="max")
    base = _shade(tk.value_noise(size, 8, seed, 3), 0.06)
    return np.clip(base - 0.55 * canvas_ * (canvas_ > 0.7) - 0.15 * canvas_ * (canvas_ <= 0.7), 0, 1)


# ---------------------------------------------------------------------------------------------------------- twills

def denim(size, seed):
    """Denim: a steep diagonal twill, slubs (thicker threads) streaking across and a worn mottle."""
    slubs = tk.stretched_noise(size, 4, 90, seed)
    wear = tk.value_noise(size, 4, seed + 1, octaves=3)
    return _shade(tk.normalise(0.55 * _twill(size, 48) + 0.3 * slubs + 0.15 * wear), 0.32)


def twill(size, seed):
    """Chino and gabardine twill: a fine diagonal rib, subtler than denim."""
    return _shade(0.75 * _twill(size, 70) + 0.25 * tk.value_noise(size, 10, seed, 3), 0.15)


def flannel(size, seed):
    """Flannel: a brushed twill, the rib softened under a fuzzy nap."""
    rib = tk.blur(_twill(size, 40), 1)
    nap = tk.value_noise(size, 60, seed, octaves=2)
    return _shade(tk.normalise(0.45 * rib + 0.55 * nap), 0.18)


def herringbone(size, seed):
    """Herringbone wool: twill that changes direction in columns, a zigzag of ribs."""
    x, y = tk.grid(size)
    col = np.floor(x * 16)
    direction = np.where(col % 2 == 0, 1.0, -1.0)
    rib = 0.5 + 0.5 * np.sin(2 * np.pi * 64 * (direction * x * 0.5 + y))
    return _shade(0.8 * rib + 0.2 * tk.value_noise(size, 20, seed, 2), 0.28)


def tweed(size, seed):
    """Tweed: a coarse wool twill flecked with darker and lighter fibres."""
    rng = np.random.default_rng(seed)
    flecks = tk.blur_small((rng.random((size, size)) < 0.04).astype(float), 1)
    mottle = tk.value_noise(size, 30, seed + 2, octaves=2)
    return _shade(tk.normalise(0.45 * _twill(size, 36) + 0.35 * mottle - 0.5 * flecks), 0.36)


def ripstop(size, seed):
    """Ripstop nylon (rain jackets, puffers): a smooth weave with a reinforcing grid of thicker threads."""
    x, y = tk.grid(size)
    n = 40
    grid_ = np.maximum(np.exp(-((((x * n) % 1.0) - 0.5) / 0.08) ** 2), np.exp(-((((y * n) % 1.0) - 0.5) / 0.08) ** 2))
    crinkle = tk.value_noise(size, 30, seed, 2)
    return _shade(tk.normalise(0.75 * (1 - grid_) + 0.25 * crinkle), 0.16)


# ----------------------------------------------------------------------------------------------------------- knits

def jersey(size, seed):
    """Cotton jersey (T-shirts): columns of tiny knit loops, faint."""
    x, y = tk.grid(size)
    cols, rows = 48, 64
    u = (x * cols) % 1.0
    v = (y * rows) % 1.0
    loop = np.exp(-(((u - 0.5) / 0.34) ** 2)) * (0.6 + 0.4 * np.sin(np.pi * v))
    return _shade(tk.normalise(loop) * 0.8 + 0.2 * tk.value_noise(size, 8, seed, 3), 0.18)


def _knit(size, seed, cols, rows, depth):
    """Stockinette: columns of V stitches, ``cols`` x ``rows`` per tile."""
    x, y = tk.grid(size)
    u = (x * cols) % 1.0
    v = (y * rows) % 1.0
    left = np.abs((u - 0.25) * 2 - (v - 0.5) * 0.9)
    right = np.abs((u - 0.75) * 2 + (v - 0.5) * 0.9)
    loop = np.where(u < 0.5, left, right)
    stitch = np.exp(-(loop / 0.32) ** 2)
    edge = np.minimum(u, 1 - u) * 2
    structure = stitch * (0.55 + 0.45 * tk.smoothstep(0.0, 0.25, edge))
    return _shade(tk.normalise(structure) * 0.9 + 0.1 * tk.value_noise(size, 8, seed, 2), depth)


def knit(size, seed):
    """Stockinette knit (sweaters, cardigans): columns of V stitches."""
    return _knit(size, seed, 36, 48, 0.46)


def chunky_knit(size, seed):
    """A chunky hand knit: big V stitches, deep shadows between."""
    return _knit(size, seed, 14, 19, 0.55)


def rib_knit(size, seed):
    """Rib knit (cuffs, tank tops, turtlenecks): raised vertical ribs."""
    x, y = tk.grid(size)
    ribs = 40
    rib = np.clip(np.sin(2 * np.pi * ribs * x) * 1.6, -1, 1) * 0.5 + 0.5
    stitch = 0.5 + 0.5 * np.cos(2 * np.pi * 60 * y + np.pi * np.floor(x * ribs * 2))
    return _shade(tk.normalise(0.78 * rib + 0.22 * stitch), 0.30)


def wide_rib(size, seed):
    """A wide rib (ribbed sweaters, sweater dresses): broad raised ribs with knit stitches on them."""
    x, y = tk.grid(size)
    rib = np.clip(np.sin(2 * np.pi * 16 * x) * 1.4, -1, 1) * 0.5 + 0.5
    stitches = _knit(size, seed, 64, 64, 1.0)
    return _shade(tk.normalise(0.7 * rib + 0.3 * stitches), 0.34)


def cable_knit(size, seed):
    """Cable knit (fisherman sweaters): twisted cables in columns over a purl background."""
    x, y = tk.grid(size)
    u = (x * 4) % 1.0
    v = (y * 6) % 1.0
    cable_zone = np.abs(u - 0.5) < 0.24
    s1 = 0.5 + 0.17 * np.sin(2 * np.pi * v)
    s2 = 0.5 - 0.17 * np.sin(2 * np.pi * v)
    strand = np.maximum(np.exp(-((u - s1) / 0.07) ** 2), np.exp(-((u - s2) / 0.07) ** 2))
    over = np.where(np.sin(2 * np.pi * v * 2) > 0, 1.0, 0.75)
    purl = 0.5 + 0.5 * np.sin(2 * np.pi * 80 * y) * np.sin(2 * np.pi * 70 * x)
    structure = np.where(cable_zone, strand * over, 0.35 + 0.25 * purl)
    border = np.exp(-((np.abs(u - 0.5) - 0.28) / 0.03) ** 2)
    return _shade(tk.normalise(np.maximum(structure, border * 0.9)), 0.45)


def waffle(size, seed):
    """Waffle knit (thermals, pyjamas): a grid of little square pockets."""
    x, y = tk.grid(size)
    n = 30
    walls = np.maximum(np.abs(np.cos(np.pi * n * x)), np.abs(np.cos(np.pi * n * y))) ** 4
    return _shade(tk.normalise(walls * 0.85 + 0.15 * tk.value_noise(size, 10, seed, 2)), 0.32)


def pique(size, seed):
    """Piqué (polo shirts): a fine honeycomb of raised cells."""
    first, second, _ = tk.worley(size, 48, seed, jitter=0.25)
    cells = tk.smoothstep(0.0, 0.25, second - first)
    return _shade(cells, 0.22)


def crochet(size, seed):
    """Crochet: open lacy rounds of chunky stitches."""
    x, y = tk.grid(size)
    n = 6
    u = (x * n) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    r = np.hypot(u, v)
    a = np.arctan2(v, u)
    rings = np.exp(-((r - 0.18) / 0.05) ** 2) + np.exp(-((r - 0.36) / 0.05) ** 2) * (0.6 + 0.4 * np.cos(a * 8))
    spokes = np.exp(-(np.sin(a * 6) / 0.25) ** 2) * (r < 0.36) * (r > 0.08)
    corners = np.exp(-((np.abs(u) + np.abs(v) - 0.62) / 0.06) ** 2)
    structure = np.clip(rings + 0.7 * spokes + corners, 0, 1)
    return np.clip(0.4 + 0.6 * tk.blur_small(structure, 1), 0, 1)


def fleece(size, seed):
    """Fleece and sweatshirt fleece: a soft fuzzy pile."""
    return _shade(tk.value_noise(size, 48, seed, octaves=3, persistence=0.7), 0.17)


def boucle(size, seed):
    """Bouclé: a wool of little curls and loops, nubbly all over."""
    first, second, _ = tk.worley(size, 36, seed)
    loops = tk.smoothstep(0.04, 0.22, np.abs(first - 0.3))
    fluff = tk.value_noise(size, 70, seed + 1, 2)
    return _shade(tk.normalise(0.7 * loops + 0.3 * fluff), 0.34)


def terry(size, seed):
    """Terry towelling (robes): dense little loops."""
    first, _, _ = tk.worley(size, 90, seed)
    return _shade(1.0 - tk.smoothstep(0.05, 0.35, first) * 0.8, 0.28)


def faux_fur(size, seed):
    """Faux fur and shearling: soft clumps of fibres lying one way."""
    clumps = tk.value_noise(size, 18, seed, octaves=3, persistence=0.55)
    strands = tk.stretched_noise(size, 120, 10, seed + 1)
    return _shade(tk.normalise(0.6 * clumps + 0.4 * strands), 0.32)


# --------------------------------------------------------------------------------------------- smooth and lustrous

def satin(size, seed):
    """Satin and silk: smooth, with long soft sheen bands, the cloth catching the light along its folds."""
    bands = tk.blur(tk.stretched_noise(size, 5, 1, seed), 3)  # soft folds hanging down the garment
    fine = tk.stretched_noise(size, 128, 4, seed + 1)
    return _shade(tk.normalise(0.85 * bands + 0.15 * fine), 0.10)


def crepe(size, seed):
    """Crêpe (dresses, blouses): a fine pebbly crinkle, matte."""
    first, second, _ = tk.worley(size, 70, seed)
    pebble = tk.smoothstep(0.0, 0.3, second - first)
    return _shade(tk.normalise(0.6 * pebble + 0.4 * tk.value_noise(size, 30, seed + 1, 2)), 0.14)


def chiffon(size, seed):
    """Chiffon and voile: very fine, almost flat, a faint crinkle."""
    return _shade(tk.value_noise(size, 24, seed, octaves=3), 0.08)


def tulle(size, seed):
    """Tulle and net (ball gowns, petticoats): a fine hexagonal net, layered so the holes half fill."""
    x, y = tk.grid(size)
    n = 40
    u = x * n
    v = y * n * 2 / np.sqrt(3)
    hexes = (np.abs(np.sin(np.pi * u)) * np.abs(np.sin(np.pi * (u * 0.5 + v * 0.5))) * np.abs(np.sin(np.pi * (u * 0.5 - v * 0.5))))
    layered = tk.normalise(hexes + np.roll(hexes, size // 7, axis=1) * 0.6)
    return _shade(layered, 0.22)


def lycra(size, seed):
    """Lycra and spandex (swimwear, leggings, activewear): smooth and tight with a soft sheen."""
    sheen = tk.blur(tk.stretched_noise(size, 4, 1, seed), 3)
    return _shade(0.7 * tk.normalise(sheen) + 0.3 * tk.value_noise(size, 40, seed + 1, 2), 0.05)


def velvet(size, seed):
    """Velvet and velour: a deep soft pile that catches the light in uneven patches."""
    patches = tk.value_noise(size, 5, seed, octaves=4, persistence=0.6)
    pile = tk.value_noise(size, 64, seed + 1, octaves=1)
    return _shade(tk.normalise(0.8 * patches + 0.2 * pile), 0.36)


def metallic(size, seed):
    """Lurex and lamé: metallic threads glinting in specks over a sheen."""
    rng = np.random.default_rng(seed)
    glints = tk.blur_small((rng.random((size, size)) < 0.02).astype(float), 1)
    sheen = tk.blur(tk.stretched_noise(size, 6, 1, seed + 1), 2)
    sparkle = tk.value_noise(size, 90, seed + 2, 1)
    return np.clip(_shade(tk.normalise(0.5 * sheen + 0.5 * sparkle), 0.16) * (1 - 0.25 * glints) + 0.0 * glints, 0, 1)


def sequins(size, seed):
    """Sequins: overlapping 1 cm discs in rows, each tilted its own way, so one edge catches the light and the other
    falls into shadow, with the odd one flashing bright. Each row overlaps the one above, like scales."""
    canvas_ = np.full((size, size), 0.3)
    rng = np.random.default_rng(seed)
    rows = 24
    step = size / rows
    r = step * 0.7
    pad = int(np.ceil(r + 2))
    yy, xx = np.mgrid[-pad:pad + 1, -pad:pad + 1].astype(float)
    disc = np.clip((r - np.hypot(xx, yy)) / 1.0 + 0.5, 0, 1)
    rim = np.clip(1 - np.abs(np.hypot(xx, yy) - r * 0.85) / 1.2, 0, 1)
    for row in range(rows):
        for col in range(rows):
            a = rng.random() * 2 * np.pi
            tilt = (np.cos(a) * xx + np.sin(a) * yy) / r
            face = 0.6 + 0.28 * tilt + (0.3 if rng.random() < 0.08 else 0.0) + 0.12 * rim * (tilt > 0)
            cx, cy = (col + (0.5 if row % 2 else 0.0)) * step, row * step
            ys = (np.arange(disc.shape[0]) + int(round(cy)) - pad) % size
            xs = (np.arange(disc.shape[1]) + int(round(cx)) - pad) % size
            block = canvas_[np.ix_(ys, xs)]
            canvas_[np.ix_(ys, xs)] = block * (1 - disc) + np.clip(face, 0, 1) * disc
    return np.clip(canvas_, 0, 1)
def lace(size, seed):
    """Lace: a floral net, the motifs solid and the net between them open (darker, where skin or lining shows)."""
    x, y = tk.grid(size)
    canvas_ = np.zeros((size, size))
    net = 0.5 + 0.5 * np.cos(2 * np.pi * 60 * x) * np.cos(2 * np.pi * 60 * y)
    rng = np.random.default_rng(seed)
    flower = _flower(int(size / 7), petals=6, roundness=0.55)
    for cx, cy in tk.scatter(size, 9, seed, size / 4.2):
        tk.stamp(canvas_, tk.rotate_shape(flower, rng.random() * 360), cx, cy)
    ring = tk.ring(size / 30, size / 45)
    for cx, cy in tk.scatter(size, 18, seed + 3, size / 8):
        tk.stamp(canvas_, ring, cx, cy)
    return np.clip(0.35 + 0.65 * np.maximum(canvas_, 0.25 * net), 0, 1)


def jacquard(size, seed):
    """Jacquard and brocade: a woven self-coloured motif, the figure in a satin weave, the ground in twill."""
    x, y = tk.grid(size)
    canvas_ = np.zeros((size, size))
    flower = _flower(int(size / 3.2), petals=8, roundness=0.35)
    for cx, cy in ((size * 0.25, size * 0.25), (size * 0.75, size * 0.75)):
        tk.stamp(canvas_, flower, cx, cy)
    leaf = tk.rotate_shape(_leaf(int(size / 5)), 45)
    for cx, cy in ((size * 0.75, size * 0.25), (size * 0.25, size * 0.75)):
        tk.stamp(canvas_, leaf, cx, cy)
    figure = canvas_ > 0.5
    ground = _twill(size, 60)
    sheen = 0.5 + 0.5 * np.sin(2 * np.pi * 120 * x)
    return np.where(figure, _shade(sheen, 0.10), _shade(ground, 0.22))


# ------------------------------------------------------------------------------------------- leathers, piles, others

def leather(size, seed):
    """Leather: a pebbled grain of irregular cells with creased edges."""
    first, second, _ = tk.worley(size, 40, seed)
    cells = tk.smoothstep(0.0, 0.18, second - first)
    creases = tk.value_noise(size, 6, seed + 1, octaves=3)
    return _shade(tk.normalise(0.75 * cells + 0.25 * creases), 0.30)


def patent(size, seed):
    """Patent leather: glossy and smooth, long bright reflections."""
    bands = tk.blur(tk.stretched_noise(size, 4, 1, seed), 4)
    return np.clip(0.93 + 0.07 * tk.normalise(bands), 0, 1)


def croc(size, seed):
    """Embossed croc: big square-ish scales with deep grooves."""
    first, second, _ = tk.worley(size, 12, seed, jitter=0.5)
    scales = tk.smoothstep(0.0, 0.12, second - first)
    dome = 1.0 - tk.smoothstep(0.0, 0.6, first)
    return _shade(tk.normalise(0.7 * scales + 0.3 * dome), 0.42)


def suede(size, seed):
    """Suede: a fine nap with soft brushed streaks."""
    nap = tk.value_noise(size, 90, seed, octaves=2)
    brushed = tk.blur(tk.stretched_noise(size, 5, 30, seed + 1), 2)
    return _shade(tk.normalise(0.5 * nap + 0.5 * brushed), 0.22)


def corduroy(size, seed):
    """Corduroy: soft vertical wales with deep grooves."""
    x, y = tk.grid(size)
    wale = np.abs(np.sin(np.pi * 40 * x)) ** 0.6
    pile = tk.stretched_noise(size, 200, 12, seed)
    return _shade(tk.normalise(0.85 * wale + 0.15 * pile), 0.42)


def rubber(size, seed):
    """Rubber (rain boots, clogs): smooth and glossy with soft reflections."""
    return np.clip(0.94 + 0.06 * tk.normalise(tk.value_noise(size, 12, seed, 3)), 0, 1)


def mesh(size, seed):
    """Athletic mesh: a grid of small round holes."""
    x, y = tk.grid(size)
    n = 50
    u = (x * n) % 1.0 - 0.5
    v = (y * n + np.where(np.floor(x * n) % 2 == 0, 0.0, 0.5)) % 1.0 - 0.5
    return _shade(tk.smoothstep(0.22, 0.3, np.hypot(u, v)), 0.45)


def quilted(size, seed):
    """Quilted puffer fabric: puffed channels with stitched seams across."""
    x, y = tk.grid(size)
    v = (y * 2) % 1.0
    puff = np.sin(np.pi * v) ** 0.5
    stitch = (v < 0.02) | (v > 0.98)
    dashes = np.floor(x * 60) % 2 == 0
    structure = puff * (0.85 + 0.15 * tk.value_noise(size, 6, seed, 2))
    structure = np.where(stitch & dashes, structure * 0.4, structure)
    return _shade(tk.normalise(structure), 0.45)


def diamond_quilt(size, seed):
    """Diamond quilting (quilted jackets, bags): puffed diamonds between diagonal stitch lines."""
    x, y = tk.grid(size)
    n = 4
    a = (x + y) * n % 1.0
    b = (x - y) * n % 1.0
    puff = np.sqrt(np.sin(np.pi * a) * np.sin(np.pi * b))
    stitch = (np.minimum(a, 1 - a) < 0.02) | (np.minimum(b, 1 - b) < 0.02)
    structure = np.where(stitch & (np.floor((x + y) * 80) % 2 == 0), puff * 0.3, puff)
    return _shade(tk.normalise(structure), 0.42)


def _flower(size, petals=5, roundness=0.6):
    """A flower shape (petals round a centre), ``size`` pixels across, in [0, 1]."""
    r = size // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float)
    angle = np.arctan2(y, x)
    radius = np.hypot(x, y) / max(r, 1)
    petal_edge = roundness + (1 - roundness) * np.abs(np.cos(petals * angle / 2))
    body = np.clip((petal_edge - radius) * r / 1.2 + 0.5, 0, 1)
    centre = np.clip((0.22 - radius) * r / 1.2 + 0.5, 0, 1)
    return np.clip(body - 0.5 * centre, 0, 1)


def _leaf(size):
    """A leaf (pointed oval), ``size`` pixels long, upright."""
    r = size // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float) / max(r, 1)
    width = 0.42 * np.sqrt(np.clip(1 - y * y, 0, 1)) * (1 - 0.3 * y)
    return np.clip((width - np.abs(x)) * r / 1.2 + 0.5, 0, 1)


FABRICS = {
    "plain": plain, "poplin": poplin, "oxford": oxford, "chambray": chambray, "canvas": canvas, "linen": linen,
    "gauze": gauze, "seersucker": seersucker, "eyelet": eyelet,
    "denim": denim, "twill": twill, "flannel": flannel, "herringbone": herringbone, "tweed": tweed, "ripstop": ripstop,
    "jersey": jersey, "knit": knit, "chunky_knit": chunky_knit, "rib_knit": rib_knit, "wide_rib": wide_rib,
    "cable_knit": cable_knit, "waffle": waffle, "pique": pique, "crochet": crochet, "fleece": fleece, "boucle": boucle,
    "terry": terry, "faux_fur": faux_fur,
    "satin": satin, "crepe": crepe, "chiffon": chiffon, "tulle": tulle, "lycra": lycra, "velvet": velvet,
    "metallic": metallic, "sequins": sequins, "lace": lace, "jacquard": jacquard,
    "leather": leather, "patent": patent, "croc": croc, "suede": suede, "corduroy": corduroy, "rubber": rubber,
    "mesh": mesh, "quilted": quilted, "diamond_quilt": diamond_quilt,
}

# Each fabric's catalog entry: the style tags archetypes weight it by, whether a print goes on it, and how common it is.
NO_PRINT = {"denim", "leather", "patent", "croc", "suede", "sequins", "lace", "quilted", "diamond_quilt", "corduroy",
            "mesh", "rubber", "metallic", "cable_knit", "chunky_knit", "faux_fur", "eyelet", "tulle", "crochet", "tweed",
            "herringbone", "jacquard", "velvet", "boucle", "seersucker", "wide_rib", "rib_knit", "knit", "terry", "waffle"}
TAGS = {
    "plain": ["casual", "cute"], "poplin": ["elegant", "bookish", "casual"], "oxford": ["bookish", "elegant"],
    "chambray": ["casual", "bookish"], "canvas": ["casual", "street"], "linen": ["casual", "elegant", "bookish"],
    "gauze": ["casual", "cute"], "seersucker": ["casual", "cute", "bookish"], "eyelet": ["cute", "date"],
    "denim": ["casual", "street"], "twill": ["casual", "bookish"], "flannel": ["casual", "street", "bookish"],
    "herringbone": ["bookish", "elegant"], "tweed": ["bookish", "elegant"], "ripstop": ["sporty", "street"],
    "jersey": ["casual", "sporty"], "knit": ["cute", "bookish", "casual"], "chunky_knit": ["cute", "casual"],
    "rib_knit": ["casual", "street", "elegant"], "wide_rib": ["cute", "elegant", "casual"], "cable_knit": ["bookish", "cute"],
    "waffle": ["casual", "sporty"], "pique": ["sporty", "bookish", "casual"], "crochet": ["cute", "casual"],
    "fleece": ["casual", "sporty", "street"], "boucle": ["elegant", "cute"], "terry": ["casual", "sporty"],
    "faux_fur": ["street", "elegant", "gothic"], "satin": ["elegant", "date"], "crepe": ["elegant", "date"],
    "chiffon": ["elegant", "cute", "date"], "tulle": ["cute", "elegant"], "lycra": ["sporty"], "velvet": ["gothic", "elegant", "date"],
    "metallic": ["date", "street"], "sequins": ["date", "elegant"], "lace": ["elegant", "gothic", "date", "cute"],
    "jacquard": ["elegant", "gothic"], "leather": ["street", "gothic"], "patent": ["gothic", "elegant", "date"],
    "croc": ["elegant", "street"], "suede": ["casual", "bookish", "street"], "corduroy": ["bookish", "casual"],
    "rubber": ["casual"], "mesh": ["sporty", "street"], "quilted": ["casual", "street", "sporty"],
    "diamond_quilt": ["elegant", "casual"],
}
