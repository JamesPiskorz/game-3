"""Fabric surfaces: what a cloth is made of, drawn as a seamless grey tile that shades the garment's own colours (the
weave of denim, the stitches of a knit, the grain of leather). Each function takes the tile size in pixels and a seed
and returns a float image in [0, 1]: 1 is the cloth's full colour, darker values the shadows of its structure.

Every tile covers TILE_METRES of cloth, so a fabric's structure is the same size on every garment. Sizes are drawn
bigger than life, the way a cel-shaded game draws them (a knit stitch is 14 mm, a corduroy wale 12 mm, denim's twill
8 mm), and each fabric has a second, broader layer (a denim's fade, a tweed's flecks, a satin's sheen) so it still reads
as itself across a room. All frequencies are whole numbers per tile, so every tile repeats without a seam.

LIFT is how much of the structure is also added as light (the shader's _BaseLift), so it shows on dark cloth too,
where darkening alone does nothing: the glint of black patent, the flecks of a charcoal tweed, the stitches of a black
knit."""

import numpy as np

import texture_kit as tk

TILE_METRES = 0.25


def _shade(structure, depth):
    """A structure in [0, 1] (1 raised, 0 sunk) as cloth shading: full colour on top, ``depth`` darker in the hollows."""
    return 1.0 - depth * (1.0 - np.clip(structure, 0.0, 1.0))


def _twill(size, lines, steep=0.5):
    """Diagonal ribs rising to the right, ``lines`` per tile down (``lines * steep`` must be whole)."""
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


def _sheen(size, seed, across=5, base=0.75, sharp=2.5, down=1):
    """Soft bands of light hanging down the cloth (satin, lycra, patent), ``base`` between the bands and 1 on them."""
    bands = tk.normalise(tk.blur(tk.stretched_noise(size, across, down, seed), 3))
    return base + (1 - base) * bands ** sharp


def _fibres(size, seed, across=48, down=256, light=0.72, dark=0.28):
    """Short streaks of lighter and darker yarn along the rows (a marl, a chambray, a tweed's flecks): +1 light, -1 dark."""
    n = tk.normalise(tk.stretched_noise(size, across, down, seed))
    return tk.smoothstep(light, light + 0.12, n) - tk.smoothstep(dark, dark - 0.12, n)


def _creases(size, seed, cells=5, width=0.025, octaves=3):
    """Thin creases wandering over the cloth (leather, waxed cotton, acid wash): 1 on a crease, 0 off it."""
    n = tk.value_noise(size, cells, seed, octaves=octaves, persistence=0.55)
    return np.exp(-((n - 0.5) / width) ** 2)


def _crumple(size, seed, cells=4, octaves=3, strength=1.0):
    """Cloth crumpled into crisp creases (taffeta, nylon), lit from the top left: a ridged height field, each slope
    lit or shadowed by which way it faces, so every crease is a sharp line of light beside one of shadow. Around 0.5."""
    height = np.zeros((size, size))
    amplitude = 1.0
    for octave in range(octaves):
        n = cells * 2 ** octave
        first, second, _ = tk.worley(size, n, seed + octave, jitter=0.9)
        height += amplitude * (second - first) / n  # straight creases where the cells meet, as paper crumples
        amplitude *= 0.45
    step = max(1, size // 256)
    slope = height - np.roll(np.roll(height, step, axis=0), step, axis=1)
    slope = tk.blur(slope, 1)
    return np.clip(0.5 + 0.2 * strength * slope / (slope.std() + 1e-9), 0, 1)


def _knit(size, seed, cols, rows, depth, fuzz=0.0):
    """Stockinette: columns of V stitches, ``cols`` x ``rows`` per tile, each V two plump legs leaning in to meet at the
    bottom, lit from above, with dark gaps between the columns."""
    x, y = tk.grid(size)
    u = (x * cols) % 1.0
    structure = np.zeros((size, size))
    for row_offset in (-1.0, 0.0, 1.0):
        v = (y * rows) % 1.0 + row_offset
        for centre, lean in ((0.27, 0.36), (0.73, -0.36)):
            du, dv = u - centre, v - 0.5
            n = np.hypot(lean, 1.0)
            along = (du * lean + dv) / n
            across = (du - dv * lean) / n
            r = (along / 0.6) ** 2 + (across / 0.21) ** 2
            leg = np.clip(1 - r, 0, 1) ** 0.5 * (1.0 - 0.35 * np.clip(along / 0.6 + 0.5, 0, 1))
            structure = np.maximum(structure, leg)
    structure = tk.normalise(structure)
    if fuzz:
        structure = (1 - fuzz) * structure + fuzz * tk.blur(structure, 3)
    return _shade(structure * 0.9 + 0.1 * tk.value_noise(size, 8, seed, 2), depth)


# ----------------------------------------------------------------------------------------------- woven cottons, linens

def plain(size, seed):
    """Smooth cotton (poplin, broadcloth): barely any texture, a soft mottle."""
    return _shade(tk.value_noise(size, 5, seed, octaves=3), 0.08)


def poplin(size, seed):
    """Poplin shirting: a fine rib across and a crisp, faint sheen."""
    x, y = tk.grid(size)
    rib = 0.5 + 0.5 * np.sin(2 * np.pi * 64 * y)
    return _shade(0.5 * rib + 0.5 * tk.normalise(_sheen(size, seed, 6, 0.0, 1.5)), 0.12)


def oxford(size, seed):
    """Oxford cloth: a basket weave, pairs of threads over pairs, the white and coloured threads heathered."""
    x, y = tk.grid(size)
    n = 20
    over = ((np.floor(x * n) + np.floor(y * n)) % 2 == 0)
    sub_x = 0.5 + 0.5 * np.cos(4 * np.pi * n * x)
    sub_y = 0.5 + 0.5 * np.cos(4 * np.pi * n * y)
    weave = np.where(over, sub_x * np.sin(np.pi * ((y * n) % 1.0)), sub_y * np.sin(np.pi * ((x * n) % 1.0)))
    heather = _fibres(size, seed, 64, 160)
    return _shade(tk.normalise(0.75 * weave + 0.25 * heather), 0.28)


def chambray(size, seed):
    """Chambray: white and coloured threads woven together, so it looks softly flecked and streaky."""
    weave = _weave(size, 40)
    rng = np.random.default_rng(seed)
    threads = rng.random(40)
    x, y = tk.grid(size)
    light = threads[(np.floor(x * 40)).astype(int) % 40]
    slubs = _fibres(size, seed + 1, 160, 12, 0.7, 0.3)
    return _shade(tk.normalise(0.5 * weave + 0.25 * light + 0.25 * slubs), 0.32)


def canvas(size, seed):
    """Canvas: a coarse plain weave with uneven (slubby) threads."""
    slub = 0.5 * tk.stretched_noise(size, 3, 32, seed) + 0.5 * tk.stretched_noise(size, 32, 3, seed + 1)
    return _shade(tk.normalise(0.72 * _weave(size, 32) + 0.28 * slub), 0.34)


def linen(size, seed):
    """Linen: a loose weave with strong slubs (thick and thin threads) both ways and soft creases."""
    slub = _fibres(size, seed, 3, 56, 0.7, 0.3) + _fibres(size, seed + 1, 56, 3, 0.7, 0.3)
    creases = tk.normalise(tk.blur(tk.stretched_noise(size, 3, 8, seed + 5), 2))
    structure = 0.6 * _weave(size, 28) + 0.4 * tk.normalise(slub)
    return np.clip(_shade(tk.normalise(structure), 0.32) * (0.9 + 0.1 * creases), 0, 1)


def gauze(size, seed):
    """Cotton gauze and cheesecloth: an open, crinkled weave with gaps between the threads."""
    x, y = tk.grid(size)
    n = 24
    gaps = np.minimum(np.abs(np.sin(np.pi * n * x)), np.abs(np.sin(np.pi * n * y)))
    crinkle = tk.stretched_noise(size, 24, 3, seed)
    return _shade(tk.normalise(0.65 * (1 - gaps) + 0.35 * crinkle), 0.34)


def seersucker(size, seed):
    """Seersucker: puckered stripes alternating with flat ones, 1 cm each."""
    x, y = tk.grid(size)
    stripe = (np.floor(x * 24) % 2 == 0)
    first, second, _ = tk.worley(size, 40, seed, jitter=0.8)
    pucker = tk.smoothstep(0.0, 0.35, second - first) * (1 - 0.5 * first)
    return np.where(stripe, _shade(tk.normalise(pucker), 0.40), _shade(tk.value_noise(size, 8, seed + 1, 2), 0.06))


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


def dobby(size, seed):
    """Dobby shirting: a plain weave with little raised dots woven in, in offset rows."""
    x, y = tk.grid(size)
    n = 24
    u = (x * n) % 1.0 - 0.5
    v = (y * n * 2 + np.where(np.floor(x * n) % 2 == 0, 0.0, 0.5)) % 1.0 - 0.5
    dot = np.clip(1 - (u / 0.22) ** 2 - (v / 0.38) ** 2, 0, 1) ** 0.5
    shadow = np.clip(1 - ((u - 0.06) / 0.25) ** 2 - ((v - 0.1) / 0.42) ** 2, 0, 1)
    ground = 0.55 + 0.1 * _weave(size, 64)
    return _shade(np.clip(np.maximum(ground - 0.25 * shadow * (dot == 0), dot), 0, 1), 0.36)


def crinkle(size, seed):
    """Crinkle cotton and crushed gauze: fine creases pressed in down the cloth."""
    n = tk.stretched_noise(size, 36, 3, seed) * 0.7 + 0.3 * tk.stretched_noise(size, 72, 5, seed + 1)
    ridges = 1 - np.abs(2 * tk.normalise(n) - 1)
    return _shade(tk.smoothstep(0.1, 0.9, ridges), 0.36)


# ---------------------------------------------------------------------------------------------------------- twills

def denim(size, seed):
    """Denim: a bold diagonal twill, rope-dyed slubs streaking down it (lighter threads) and a worn, faded mottle."""
    rib = tk.smoothstep(0.15, 0.85, _twill(size, 32))
    streaks = _fibres(size, seed, 120, 4, 0.66, 0.3)
    fade = tk.value_noise(size, 3, seed + 1, octaves=3)
    return _shade(tk.normalise(0.55 * rib + 0.25 * streaks + 0.2 * fade), 0.44)


def stonewash(size, seed):
    """Stonewashed denim: the twill worn soft and faded pale in big patches, the streaks bleached."""
    rib = tk.smoothstep(0.15, 0.85, _twill(size, 32))
    streaks = _fibres(size, seed, 120, 4, 0.62, 0.3)
    fade = tk.smoothstep(0.3, 0.75, tk.value_noise(size, 4, seed + 1, octaves=4, persistence=0.6))
    return _shade(tk.normalise(0.35 * rib + 0.25 * streaks + 0.4 * fade), 0.44)


def acid_wash(size, seed):
    """Acid-wash denim: bleached marbling, pale blotches broken by dark crinkled veins."""
    bleach = tk.smoothstep(0.36, 0.66, tk.value_noise(size, 10, seed, octaves=4, persistence=0.6))
    veins = _creases(size, seed + 1, 12, 0.018, 3)
    rib = _twill(size, 32)
    return _shade(np.clip(0.3 + 0.6 * bleach - 0.3 * veins + 0.12 * rib, 0, 1), 0.5)


def twill(size, seed):
    """Chino and gabardine twill: a fine diagonal rib, subtler than denim."""
    return _shade(0.75 * tk.smoothstep(0.1, 0.9, _twill(size, 44)) + 0.25 * tk.value_noise(size, 6, seed, 3), 0.24)


def flannel(size, seed):
    """Flannel: a brushed twill, the rib softened under a fuzzy, heathered nap."""
    rib = tk.blur(_twill(size, 28), 2)
    nap = tk.value_noise(size, 40, seed, octaves=2)
    heather = _fibres(size, seed + 1, 40, 120, 0.7, 0.3)
    return _shade(tk.normalise(0.4 * rib + 0.4 * nap + 0.2 * heather), 0.28)


def herringbone(size, seed):
    """Herringbone wool: twill that changes direction in 3 cm columns, a zigzag of ribs, flecked."""
    x, y = tk.grid(size)
    col = np.floor(x * 8)
    direction = np.where(col % 2 == 0, 1.0, -1.0)
    rib = tk.smoothstep(0.2, 0.8, 0.5 + 0.5 * np.sin(2 * np.pi * 24 * (direction * x + y)))
    divide = np.exp(-((((x * 8) % 1.0) - 0.0) / 0.03) ** 2) + np.exp(-((((x * 8) % 1.0) - 1.0) / 0.03) ** 2)
    flecks = _fibres(size, seed, 64, 200, 0.75, 0.25)
    return _shade(tk.normalise(0.75 * rib + 0.15 * flecks - 0.3 * divide), 0.44)


def tweed(size, seed):
    """Tweed: a coarse wool twill, mottled and flecked with lighter and darker fibres."""
    flecks = _fibres(size, seed, 64, 200, 0.7, 0.26)
    mottle = tk.value_noise(size, 10, seed + 2, octaves=3)
    return _shade(tk.normalise(0.3 * _twill(size, 24) + 0.25 * mottle + 0.6 * flecks), 0.5)


def satin_stripe(size, seed):
    """Satin stripe (dress shirts, waistcoats, pyjamas): a self-coloured stripe, shining satin bands between matte ones."""
    x, y = tk.grid(size)
    band = tk.smoothstep(0.0, 0.03, np.abs(((x * 8) % 1.0) - 0.5) - 0.2)
    satin = _sheen(size, seed, 6, 0.86, 1.5)
    matte = _shade(0.5 + 0.5 * np.sin(2 * np.pi * 64 * y), 0.12) * 0.88
    return np.clip(band * matte + (1 - band) * satin, 0, 1)


def felt(size, seed):
    """Boiled wool and felt (melton coats, berets): a dense matted surface, softly mottled, no weave showing."""
    mottle = tk.value_noise(size, 8, seed, octaves=4, persistence=0.6)
    fibres = tk.blur(tk.value_noise(size, 128, seed + 1, 1), 1)
    return _shade(tk.normalise(0.7 * mottle + 0.3 * fibres), 0.22)


def pinstripe_wool(size, seed):
    """Pinstripe suiting: a fine wool twill with a thin pale thread every 1.5 cm."""
    x, y = tk.grid(size)
    rib = _twill(size, 56)
    stripe = np.exp(-((((x * 16) % 1.0) - 0.5) / 0.045) ** 2)
    return np.clip(_shade(0.7 * rib + 0.3 * tk.value_noise(size, 6, seed, 2), 0.16) * 0.8 + 0.25 * stripe, 0, 1)


def houndstooth_wool(size, seed):
    """Houndstooth wool (coats, suits): the broken check woven in light and dark yarn of one colour."""
    x, y = tk.grid(size)
    n = 16
    u, v = (x * n) % 1.0, (y * n) % 1.0
    cell = (u < 0.5) & (v < 0.5)
    tooth = ((u >= 0.5) & (v < 0.5) & (u - 0.5 < v)) | ((u < 0.5) & (v >= 0.5) & (v - 0.5 < u))
    step = (u >= 0.5) & (v >= 0.5) & (np.abs(u - v) < 0.18)
    check = tk.blur_small((cell | tooth | step).astype(float), 1)
    return _shade(tk.normalise(0.75 * (1 - check) + 0.25 * _twill(size, 64)), 0.38)


def ripstop(size, seed):
    """Ripstop nylon (rain jackets, puffers): a smooth weave with a reinforcing grid of thicker threads, a soft sheen."""
    x, y = tk.grid(size)
    n = 24
    grid_ = np.maximum(np.exp(-((((x * n) % 1.0) - 0.5) / 0.07) ** 2), np.exp(-((((y * n) % 1.0) - 0.5) / 0.07) ** 2))
    sheen = tk.normalise(_sheen(size, seed, 5, 0.0, 1.5))
    return _shade(tk.normalise(0.7 * (1 - grid_) + 0.3 * sheen), 0.3)


def nylon(size, seed):
    """Nylon shell (windbreakers, track jackets): smooth and slick, crumpled into crisp creases that catch the light."""
    return np.clip(0.68 + 0.32 * _crumple(size, seed, 4, 3, 1.0) * (0.75 + 0.25 * _sheen(size, seed + 1, 4, 0.0, 2)), 0, 1)


def waxed(size, seed):
    """Waxed cotton (field jackets): canvas under a waxy sheen, worn paler along its creases."""
    weave = _weave(size, 32)
    wax = tk.normalise(tk.value_noise(size, 4, seed, octaves=3))
    scuffs = 1 - np.abs(2 * tk.normalise(tk.stretched_noise(size, 14, 4, seed + 1)) - 1)
    return np.clip(_shade(tk.normalise(0.3 * weave + 0.5 * wax + 0.2 * tk.smoothstep(0.75, 1.0, scuffs)), 0.34), 0, 1)


# ----------------------------------------------------------------------------------------------------------- knits

def jersey(size, seed):
    """Cotton jersey (T-shirts): columns of small knit stitches, faint."""
    return _knit(size, seed, 32, 42, 0.2)


def marl(size, seed):
    """Marl (heathered jersey): flecks of paler and darker yarn knitted through it, like a grey marl sweatshirt."""
    fibres = _fibres(size, seed, 40, 300, 0.7, 0.32)
    stitches = _knit(size, seed + 1, 32, 42, 1.0)
    return _shade(tk.normalise(0.65 * fibres + 0.35 * stitches), 0.42)


def slub_jersey(size, seed):
    """Slub jersey: a T-shirt knit with thicker, paler streaks of yarn across it."""
    slubs = _fibres(size, seed, 3, 90, 0.72, 0.2)
    return _shade(tk.normalise(0.55 * slubs + 0.45 * _knit(size, seed + 1, 32, 42, 1.0)), 0.3)


def knit(size, seed):
    """Stockinette knit (sweaters, cardigans): columns of V stitches."""
    return _knit(size, seed, 18, 24, 0.5)


def chunky_knit(size, seed):
    """A chunky hand knit: big V stitches, deep shadows between."""
    return _knit(size, seed, 8, 10, 0.6)


def lurex_knit(size, seed):
    """Lurex knit (party sweaters): a fine knit with metallic yarn glinting through it."""
    rng = np.random.default_rng(seed)
    glints = np.clip(tk.blur_small((rng.random((size, size)) < 0.02).astype(float), 1) * 3, 0, 1)
    return np.clip(_knit(size, seed, 24, 32, 0.4) * (0.9 + 0.1 * glints) + 0.18 * glints, 0, 1)


def mohair(size, seed):
    """Mohair and angora: a knit under a soft halo of fuzz."""
    fuzz = tk.blur(tk.value_noise(size, 96, seed + 3, 1), 1)
    return np.clip(_knit(size, seed, 16, 21, 0.4, fuzz=0.75) * (0.88 + 0.12 * fuzz), 0, 1)


def rib_knit(size, seed):
    """Rib knit (cuffs, tank tops, turtlenecks): raised columns of knit stitches between sunk purl columns, 1 cm apart."""
    x, y = tk.grid(size)
    ribs = 24
    u = (x * ribs) % 1.0
    raised = np.clip(np.sin(2 * np.pi * u) * 1.5, -1, 1) * 0.5 + 0.5
    stitches = tk.normalise(_knit(size, seed, ribs * 2, 40, 1.0))
    return _shade(tk.normalise(0.7 * raised + 0.3 * stitches * raised), 0.42)


def wide_rib(size, seed):
    """A wide rib (ribbed sweaters, sweater dresses): broad raised ribs of knit stitches between narrow sunk ones."""
    x, y = tk.grid(size)
    u = (x * 10) % 1.0
    raised = tk.smoothstep(0.0, 0.12, u) * tk.smoothstep(0.0, 0.12, 0.7 - u)
    stitches = tk.normalise(_knit(size, seed, 40, 34, 1.0))
    return _shade(tk.normalise(0.6 * raised + 0.4 * stitches * (0.4 + 0.6 * raised)), 0.5)


def pointelle(size, seed):
    """Pointelle (cardigans, camisoles): a fine knit with little eyelet holes in diamonds."""
    stitches = _knit(size, seed, 24, 32, 1.0)
    holes = np.zeros((size, size))
    hole = tk.disc(size / 72, 1.2)
    step = size / 6
    for row in range(6):
        for col in range(6):
            for dx, dy in ((0, -0.25), (0.25, 0), (0, 0.25), (-0.25, 0)):
                tk.stamp(holes, hole, (col + 0.5 + dx) * step, (row + 0.5 + dy) * step)
    return _shade(np.clip(tk.normalise(stitches) - 0.9 * holes, 0, 1), 0.5)


def seed_stitch(size, seed):
    """Seed stitch (moss stitch): knit and purl alternating stitch by stitch, a field of little bumps."""
    x, y = tk.grid(size)
    cols, rows = 26, 34
    u = (x * cols) % 1.0 - 0.5
    v = (y * rows + 0.5 * (np.floor(x * cols) % 2)) % 1.0 - 0.5
    nub = np.clip(1 - (u / 0.48) ** 2 - (v / 0.44) ** 2, 0, 1) ** 0.6 * (1 - 0.3 * (v + 0.5))
    return _shade(tk.normalise(nub * 0.9 + 0.1 * tk.value_noise(size, 8, seed, 2)), 0.48)


def cable_knit(size, seed):
    """Cable knit (fisherman sweaters): twisted cables in columns, ribs either side, over a seed-stitch ground."""
    x, y = tk.grid(size)
    u = (x * 4) % 1.0
    v = (y * 6) % 1.0
    cable_zone = np.abs(u - 0.5) < 0.24
    s1 = 0.5 + 0.17 * np.sin(2 * np.pi * v)
    s2 = 0.5 - 0.17 * np.sin(2 * np.pi * v)
    strand1 = np.exp(-((u - s1) / 0.075) ** 2)
    strand2 = np.exp(-((u - s2) / 0.075) ** 2)
    front = np.where(np.cos(2 * np.pi * v) > 0, strand1, strand2)
    strand = np.maximum(front, 0.7 * np.maximum(strand1, strand2))
    # Stitches along the strands.
    strand = strand * (0.8 + 0.2 * np.sin(2 * np.pi * 36 * y) ** 2)
    ground = tk.normalise(seed_stitch(size, seed)) * 0.45 + 0.3
    structure = np.where(cable_zone, np.maximum(strand, 0.12), ground)
    border = np.exp(-((np.abs(u - 0.5) - 0.29) / 0.025) ** 2)
    return _shade(tk.normalise(np.maximum(structure, border * 0.9)), 0.55)


def waffle(size, seed):
    """Waffle knit (thermals, pyjamas): a grid of little square pockets."""
    x, y = tk.grid(size)
    n = 18
    walls = np.maximum(np.abs(np.cos(np.pi * n * x)), np.abs(np.cos(np.pi * n * y))) ** 4
    floor = 0.25 + 0.35 * (((x * n) % 1.0) + ((y * n) % 1.0)) / 2  # lit from the top left
    return _shade(tk.normalise(np.maximum(walls * 0.9, floor) + 0.1 * tk.value_noise(size, 10, seed, 2)), 0.42)


def pique(size, seed):
    """Piqué (polo shirts): a honeycomb of raised cells."""
    first, second, _ = tk.worley(size, 32, seed, jitter=0.25)
    cells = tk.smoothstep(0.0, 0.25, second - first)
    return _shade(cells, 0.3)


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
    """Fleece and sweatshirt fleece: a soft fuzzy pile, lightly pilled."""
    first, second, _ = tk.worley(size, 36, seed + 1)
    pills = 1 - tk.smoothstep(0.05, 0.3, first)
    fuzz = tk.value_noise(size, 24, seed, octaves=3, persistence=0.7)
    return _shade(tk.normalise(0.6 * fuzz + 0.4 * pills), 0.28)


def boucle(size, seed):
    """Bouclé: a wool of little curls and loops, nubbly all over, the loops catching the light."""
    first, second, _ = tk.worley(size, 30, seed)
    nub = np.clip(1 - first / 0.62, 0, 1) ** 0.7
    curl = np.exp(-((first - 0.3) / 0.05) ** 2) * tk.smoothstep(0.04, 0.12, second - first)
    small, _, _ = tk.worley(size, 64, seed + 2)
    fluff = np.clip(1 - small / 0.6, 0, 1)
    return _shade(tk.normalise(0.5 * nub + 0.25 * curl + 0.25 * fluff), 0.46)


def terry(size, seed):
    """Terry towelling (robes): dense little loops."""
    first, _, _ = tk.worley(size, 52, seed)
    return _shade(1.0 - tk.smoothstep(0.05, 0.4, first) * 0.85, 0.38)


def sherpa(size, seed):
    """Sherpa and shearling fleece: tight curly clumps, bumpy all over."""
    big, _, _ = tk.worley(size, 18, seed)
    small, _, _ = tk.worley(size, 44, seed + 1)
    clump = np.clip(1 - big / 0.7, 0, 1) ** 0.6
    curl = np.clip(1 - small / 0.6, 0, 1) ** 0.8
    return _shade(tk.normalise(tk.blur(0.55 * clump + 0.45 * curl, 1)), 0.5)


def chenille(size, seed):
    """Chenille: fat, fuzzy cords of pile running down the cloth."""
    x, y = tk.grid(size)
    u = (x * 14) % 1.0
    cord = np.sin(np.pi * u) ** 0.7
    fuzz = tk.blur(tk.value_noise(size, 80, seed, 1), 1)
    return _shade(tk.normalise(0.75 * cord + 0.25 * fuzz), 0.46)


def faux_fur(size, seed):
    """Faux fur: soft clumps of fibres lying one way."""
    clumps = tk.value_noise(size, 9, seed, octaves=3, persistence=0.55)
    strands = tk.stretched_noise(size, 96, 8, seed + 1)
    tips = tk.smoothstep(0.55, 0.9, tk.stretched_noise(size, 60, 6, seed + 2))
    return _shade(tk.normalise(0.5 * clumps + 0.3 * strands + 0.2 * tips), 0.46)


# --------------------------------------------------------------------------------------------- smooth and lustrous

def satin(size, seed):
    """Satin and silk: smooth, with long soft sheen bands, bright crests where the cloth catches the light."""
    fine = tk.stretched_noise(size, 128, 4, seed + 1)
    return np.clip(_sheen(size, seed, 5, 0.76, 2.2) * (0.97 + 0.03 * fine), 0, 1)


def shantung(size, seed):
    """Shantung and dupioni silk: a crisp sheen across slubs of thicker thread."""
    slubs = _fibres(size, seed, 5, 110, 0.68, 0.25)
    return _shade(tk.normalise(0.55 * _sheen(size, seed + 1, 5, 0.0, 1.6) + 0.45 * slubs), 0.32)


def moire(size, seed):
    """Moiré silk: a watery sheen of rippling bands, like wood grain."""
    x, y = tk.grid(size)
    n = tk.value_noise(size, 3, seed, octaves=3)
    ripples = (0.5 + 0.5 * np.sin(2 * np.pi * (20 * y + 3.5 * n))) ** 4
    patches = tk.value_noise(size, 2, seed + 1, octaves=2)
    return np.clip(0.76 + 0.24 * ripples * (0.4 + 0.6 * patches), 0, 1)


def taffeta(size, seed):
    """Taffeta (ball gowns, party dresses): stiff and rustling, crumpled into crisp shining facets over a fine rib."""
    x, y = tk.grid(size)
    rib = 0.5 + 0.5 * np.sin(2 * np.pi * 72 * y)
    crumple = _crumple(size, seed, 4, 3, 1.2)
    return np.clip((0.6 + 0.4 * crumple) * (0.94 + 0.06 * rib), 0, 1)


def crepe(size, seed):
    """Crêpe (dresses, blouses): a pebbly crinkle, matte."""
    first, second, _ = tk.worley(size, 40, seed)
    pebble = tk.smoothstep(0.0, 0.3, second - first)
    return _shade(tk.normalise(0.6 * pebble + 0.4 * tk.value_noise(size, 20, seed + 1, 2)), 0.2)


def chiffon(size, seed):
    """Chiffon and voile: very fine, almost flat, a faint crinkle and a soft sheen."""
    return _shade(tk.normalise(0.6 * tk.stretched_noise(size, 20, 3, seed) + 0.4 * _sheen(size, seed + 1, 4, 0.0, 1.5)), 0.12)


def organza(size, seed):
    """Organza: a crisp, sheer silk whose layers ripple into a shimmering moiré."""
    n = tk.value_noise(size, 3, seed, octaves=2)
    x, y = tk.grid(size)
    ripple = 0.5 + 0.5 * np.sin(2 * np.pi * (4 * x + 7 * y) + 7 * n)
    return np.clip(0.9 + 0.1 * ripple ** 2, 0, 1)


def tulle(size, seed):
    """Tulle and net (ball gowns, petticoats): a fine hexagonal net, layered so the holes half fill."""
    x, y = tk.grid(size)
    n = 28
    u = x * n
    v = y * n * 2 / np.sqrt(3)
    hexes = (np.abs(np.sin(np.pi * u)) * np.abs(np.sin(np.pi * (u * 0.5 + v * 0.5))) * np.abs(np.sin(np.pi * (u * 0.5 - v * 0.5))))
    layered = tk.normalise(hexes + np.roll(hexes, size // 7, axis=1) * 0.6)
    return _shade(layered, 0.3)


def lycra(size, seed):
    """Lycra and spandex (swimwear, leggings, activewear): smooth and tight with a strong sheen."""
    return np.clip(_sheen(size, seed, 4, 0.8, 2.0) * (0.97 + 0.03 * tk.value_noise(size, 40, seed + 1, 2)), 0, 1)


def velvet(size, seed):
    """Velvet and velour: a deep soft pile that catches the light in uneven, crushed patches."""
    patches = tk.value_noise(size, 4, seed, octaves=4, persistence=0.6)
    pile = tk.value_noise(size, 64, seed + 1, octaves=1)
    return _shade(tk.normalise(0.85 * tk.smoothstep(0.15, 0.9, patches) + 0.15 * pile), 0.34)


def metallic(size, seed):
    """Lurex and lamé: metallic threads glinting in specks over a sheen."""
    rng = np.random.default_rng(seed)
    glints = tk.blur_small((rng.random((size, size)) < 0.012).astype(float), 1) * 3
    sheen = _sheen(size, seed + 1, 6, 0.0, 1.5)
    sparkle = tk.value_noise(size, 90, seed + 2, 1)
    return np.clip(0.72 + 0.16 * tk.normalise(0.6 * sheen + 0.4 * sparkle) + 0.2 * np.clip(glints, 0, 1), 0, 1)


def glitter(size, seed):
    """Glitter: a dense scatter of bright specks."""
    rng = np.random.default_rng(seed)
    specks = np.clip(tk.blur_small((rng.random((size, size)) < 0.05).astype(float), 1) * 3, 0, 1)
    big = np.clip(tk.blur_small((rng.random((size, size)) < 0.006).astype(float), 2) * 8, 0, 1)
    return np.clip(0.66 + 0.2 * specks + 0.14 * big + 0.04 * tk.value_noise(size, 6, seed, 2), 0, 1)


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


def beaded(size, seed):
    """Beading (gowns, evening tops): rows of little round beads, each with a highlight."""
    x, y = tk.grid(size)
    n = 30
    u = (x * n + 0.5 * (np.floor(y * n) % 2)) % 1.0 - 0.5
    v = (y * n) % 1.0 - 0.5
    r = np.hypot(u, v)
    bead = np.clip(1 - (r / 0.46) ** 2, 0, 1) ** 0.5
    glint = np.exp(-(((u + 0.15) ** 2 + (v + 0.15) ** 2) / 0.006))
    rng = np.random.default_rng(seed)
    flash = rng.random((n, n))[np.floor(y * n).astype(int) % n, np.floor(x * n).astype(int) % n] < 0.1
    return np.clip(0.5 + 0.38 * bead + 0.25 * glint + 0.1 * flash * bead, 0, 1)


def lace(size, seed):
    """Lace: a floral net, the motifs solid and the net between them open (darker, where skin or lining shows)."""
    x, y = tk.grid(size)
    canvas_ = np.zeros((size, size))
    net = 0.5 + 0.5 * np.cos(2 * np.pi * 48 * x) * np.cos(2 * np.pi * 48 * y)
    rng = np.random.default_rng(seed)
    flower = _flower(int(size / 7), petals=6, roundness=0.55)
    for cx, cy in tk.scatter(size, 9, seed, size / 4.2):
        tk.stamp(canvas_, tk.rotate_shape(flower, rng.random() * 360), cx, cy)
    ring = tk.ring(size / 30, size / 45)
    for cx, cy in tk.scatter(size, 18, seed + 3, size / 8):
        tk.stamp(canvas_, ring, cx, cy)
    return np.clip(0.35 + 0.65 * np.maximum(canvas_, 0.25 * net), 0, 1)


def jacquard(size, seed):
    """Jacquard and brocade: a woven self-coloured motif, the figure in a shining satin weave, the ground in twill."""
    x, y = tk.grid(size)
    canvas_ = np.zeros((size, size))
    flower = _flower(int(size / 3.2), petals=8, roundness=0.35)
    for cx, cy in ((size * 0.25, size * 0.25), (size * 0.75, size * 0.75)):
        tk.stamp(canvas_, flower, cx, cy)
    leaf = tk.rotate_shape(_leaf(int(size / 5)), 45)
    for cx, cy in ((size * 0.75, size * 0.25), (size * 0.25, size * 0.75)):
        tk.stamp(canvas_, leaf, cx, cy)
    figure = tk.blur_small((canvas_ > 0.5).astype(float), 1)
    ground = _shade(_twill(size, 48), 0.3) * 0.82
    shine = 0.92 + 0.08 * np.sin(2 * np.pi * 96 * x)
    return np.clip(figure * shine + (1 - figure) * ground, 0, 1)


# ------------------------------------------------------------------------------------------- leathers, piles, others

def leather(size, seed):
    """Leather: a pebbled grain, soft creases and a waxy sheen."""
    first, _, _ = tk.worley(size, 36, seed)
    pebble = np.clip(1 - first / 0.65, 0, 1) ** 0.5
    sheen = _sheen(size, seed + 2, 4, 0.0, 2.0)
    return _shade(tk.normalise(0.45 * pebble + 0.55 * sheen), 0.36)


def perforated(size, seed):
    """Perforated leather (trainers, jackets): a smooth grain punched with rows of little holes."""
    x, y = tk.grid(size)
    n = 24
    u = (x * n) % 1.0 - 0.5
    v = (y * n + 0.5 * (np.floor(x * n) % 2)) % 1.0 - 0.5
    hole = 1 - tk.smoothstep(0.12, 0.17, np.hypot(u, v))
    rim = np.exp(-((np.hypot(u, v) - 0.19) / 0.03) ** 2)
    grain = tk.normalise(tk.value_noise(size, 3, seed, 2))
    return np.clip(_shade(grain, 0.16) - 0.6 * hole + 0.06 * rim, 0, 1)


def patent(size, seed):
    """Patent leather: glossy and smooth, long bright reflections."""
    return np.clip(np.maximum(_sheen(size, seed, 4, 0.7, 4.0), _sheen(size, seed + 1, 9, 0.7, 10.0)), 0, 1)


def vinyl(size, seed):
    """Vinyl and PVC: wet-look gloss, crisp white streaks of reflection."""
    streaks = tk.normalise(tk.blur(tk.stretched_noise(size, 7, 1, seed), 2)) ** 8
    second = tk.normalise(tk.blur(tk.stretched_noise(size, 13, 1, seed + 1), 1)) ** 14
    return np.clip(0.62 + 0.38 * np.maximum(streaks, second), 0, 1)


def croc(size, seed):
    """Embossed croc: big square-ish scales with deep grooves."""
    first, second, _ = tk.worley(size, 12, seed, jitter=0.5)
    scales = tk.smoothstep(0.0, 0.12, second - first)
    dome = 1.0 - tk.smoothstep(0.0, 0.6, first)
    return _shade(tk.normalise(0.7 * scales + 0.3 * dome), 0.42)


def snake(size, seed):
    """Embossed snakeskin: rows of overlapping diamond scales, each domed and shaded at its lower edge."""
    x, y = tk.grid(size)
    cols, rows = 10, 20
    u = (x * cols + 0.5 * (np.floor(y * rows) % 2)) % 1.0 - 0.5
    v = (y * rows) % 1.0 - 0.5
    d = np.abs(u) / 0.5 + np.abs(v) / 0.5
    # The neighbouring row's diamonds fill the corners.
    u2 = (x * cols + 0.5 * ((np.floor(y * rows) + 1) % 2)) % 1.0 - 0.5
    v2 = ((y * rows) % 1.0) - 0.5
    d2 = np.minimum(np.abs(u2) / 0.5 + np.abs(v2 - 0.5) / 0.5, np.abs(u2) / 0.5 + np.abs(v2 + 0.5) / 0.5)
    inside = np.minimum(d, d2)
    scale = 1 - tk.smoothstep(0.75, 1.0, inside)
    dome = (1 - inside) * (1 - 0.4 * (v + 0.5))
    return _shade(tk.normalise(0.6 * scale + 0.4 * np.clip(dome, 0, 1)), 0.42)


def suede(size, seed):
    """Suede: a fine nap brushed into soft lighter and darker patches."""
    nap = tk.value_noise(size, 80, seed, octaves=2)
    brushed = tk.blur(tk.stretched_noise(size, 5, 9, seed + 1), 3)
    return _shade(tk.normalise(0.3 * nap + 0.7 * tk.normalise(brushed)), 0.3)


def corduroy(size, seed):
    """Corduroy: rounded vertical wales of pile with sharp, deep grooves between."""
    x, y = tk.grid(size)
    u = (x * 20) % 1.0
    wale = np.sin(np.pi * u) ** 0.45
    pile = tk.stretched_noise(size, 240, 10, seed)
    return _shade(tk.normalise(0.85 * wale + 0.15 * pile), 0.52)


def ottoman(size, seed):
    """Ottoman: heavy horizontal cords woven across (coats, structured skirts)."""
    x, y = tk.grid(size)
    v = (y * 18) % 1.0
    cord = np.sin(np.pi * v) ** 0.6 * (1 - 0.25 * v)
    fine = 0.5 + 0.5 * np.sin(2 * np.pi * 96 * x)
    return _shade(tk.normalise(0.88 * cord + 0.12 * fine * cord), 0.46)


def _cord(size, seed, wales, depth):
    """Corduroy with ``wales`` per tile."""
    x, y = tk.grid(size)
    u = (x * wales) % 1.0
    wale = np.sin(np.pi * u) ** 0.45
    pile = tk.stretched_noise(size, 240, 10, seed)
    return _shade(tk.normalise(0.85 * wale + 0.15 * pile), depth)


def needlecord(size, seed):
    """Needlecord: a fine corduroy, its wales 6 mm apart (shirts, dresses)."""
    return _cord(size, seed, 40, 0.4)


def jumbo_cord(size, seed):
    """Jumbo cord: fat wales 2.5 cm apart (jackets, wide trousers)."""
    return _cord(size, seed, 10, 0.58)


def scuba(size, seed):
    """Scuba and neoprene: thick, springy and smooth, a faint pebble and a soft sheen (skater skirts, wetsuits)."""
    first, second, _ = tk.worley(size, 90, seed)
    pebble = tk.smoothstep(0.0, 0.3, second - first)
    return np.clip(_sheen(size, seed + 1, 3, 0.86, 2.0) * (0.95 + 0.05 * pebble), 0, 1)


def gloss_puffer(size, seed):
    """A glossy puffer: shiny channels, each with a bright highlight along its top and its seam in shadow."""
    x, y = tk.grid(size)
    v = (y * 2) % 1.0
    puff = np.sin(np.pi * v) ** 0.5
    highlight = np.exp(-((v - 0.32) / 0.06) ** 2) * (0.7 + 0.3 * tk.stretched_noise(size, 5, 2, seed))
    stitch = (v < 0.025) | (v > 0.975)
    structure = np.where(stitch, 0.15, 0.55 * puff + 0.45 * highlight)
    return np.clip(0.5 + 0.5 * structure, 0, 1)


def guipure(size, seed):
    """Guipure lace: bold flower motifs joined by thick bars, no net between (the gaps dark, where the lining shows)."""
    canvas_ = np.zeros((size, size))
    rng = np.random.default_rng(seed)
    flower = _flower(int(size / 3.8), petals=5, roundness=0.6)
    points = tk.scatter(size, 7, seed, size / 3.2)
    for cx, cy in points:
        tk.stamp(canvas_, tk.rotate_shape(flower, rng.random() * 360), cx, cy)
    # Bars joining each motif to its nearest neighbours, wrapping round the tile.
    x, y = np.mgrid[0:size, 0:size][::-1].astype(float)
    for i, p in enumerate(points):
        d = [((q - p + size / 2) % size - size / 2) for q in points]
        near = sorted(range(len(points)), key=lambda j: np.hypot(*d[j]))[1:3]
        for j in near:
            dx, dy = d[j]
            length = np.hypot(dx, dy)
            ux, uy = dx / length, dy / length
            rx = (x - p[0] + size / 2) % size - size / 2
            ry = (y - p[1] + size / 2) % size - size / 2
            along = rx * ux + ry * uy
            across = np.abs(-rx * uy + ry * ux)
            canvas_ = np.maximum(canvas_, ((along > 0) & (along < length) & (across < size / 110)).astype(float))
    return np.clip(0.4 + 0.6 * tk.blur_small(canvas_, 1), 0, 1)


def rubber(size, seed):
    """Rubber (rain boots, clogs): smooth and glossy with soft reflections."""
    return np.clip(_sheen(size, seed, 3, 0.78, 3.0), 0, 1)


def mesh(size, seed):
    """Athletic mesh: rows of small round holes."""
    x, y = tk.grid(size)
    n = 32
    u = (x * n) % 1.0 - 0.5
    v = (y * n + np.where(np.floor(x * n) % 2 == 0, 0.0, 0.5)) % 1.0 - 0.5
    return _shade(tk.smoothstep(0.22, 0.3, np.hypot(u, v)), 0.55)


def straw(size, seed):
    """Woven straw and raffia (sun hats, espadrilles, beach bags): broad flat strands, over and under, fibrous."""
    x, y = tk.grid(size)
    n = 10
    over = ((np.floor(x * n) + np.floor(y * n)) % 2 == 0)
    fibre_down = tk.stretched_noise(size, 160, 6, seed)
    fibre_across = tk.stretched_noise(size, 6, 160, seed + 1)
    edge_x = np.sin(np.pi * ((x * n) % 1.0)) ** 0.4
    edge_y = np.sin(np.pi * ((y * n) % 1.0)) ** 0.4
    strand = np.where(over, edge_x * (0.75 + 0.25 * fibre_down) * (0.8 + 0.2 * edge_y),
                      edge_y * (0.75 + 0.25 * fibre_across) * (0.8 + 0.2 * edge_x))
    return _shade(tk.normalise(strand), 0.5)


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


# --------------------------------------------------------------------------------------------- made into a surface

def patchwork(size, seed):
    """Patchwork: 5 cm squares of the cloth in lighter and darker shades, seamed and topstitched together."""
    x, y = tk.grid(size)
    n = 5
    rng = np.random.default_rng(seed)
    shade = 0.7 + 0.3 * rng.random((n, n))
    cell = shade[np.floor(y * n).astype(int) % n, np.floor(x * n).astype(int) % n]
    u, v = (x * n) % 1.0, (y * n) % 1.0
    edge = np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v))
    stitch = (edge > 0.05) & (edge < 0.07) & (np.floor((x + y) * 160) % 2 == 0)
    cloth = cell * (0.88 + 0.12 * _weave(size, 32))
    return np.clip(np.where(edge < 0.025, cloth * 0.55, np.where(stitch, cloth * 0.8, cloth)), 0, 1)


def pleated(size, seed):
    """Knife pleats (pleated skirts and dresses): 2.5 cm pleats down the cloth, each lit at its folded edge and
    shading into the fold under the next."""
    x, y = tk.grid(size)
    u = (x * 10) % 1.0
    face = 1.0 - 0.75 * u ** 1.5
    edge = np.exp(-(u / 0.035) ** 2) * 0.15
    fold = np.exp(-((1 - u) / 0.04) ** 2) * 0.3
    return np.clip(_shade(np.clip(face + edge - fold, 0, 1), 0.5) * (0.97 + 0.03 * tk.value_noise(size, 6, seed, 2)), 0, 1)


def smocked(size, seed):
    """Smocking and shirring: rows of gathering stitches, the cloth puckered into little folds between them."""
    x, y = tk.grid(size)
    rows = 12
    v = (y * rows) % 1.0
    row = np.floor(y * rows)
    folds = 0.5 + 0.5 * np.sin(2 * np.pi * 24 * x + 1.7 * row)
    gather = np.exp(-((v - 0.5) / 0.07) ** 2)
    puff = np.sin(np.pi * v) ** 0.7
    return _shade(np.clip(0.3 + 0.7 * folds * (1 - gather) * (0.5 + 0.5 * np.abs(np.cos(np.pi * v))) * puff + 0.2 * puff - 0.35 * gather, 0, 1), 0.5)


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
    # Added with the second pass over the library.
    "dobby": dobby, "crinkle": crinkle, "stonewash": stonewash, "acid_wash": acid_wash, "nylon": nylon, "waxed": waxed,
    "marl": marl, "slub_jersey": slub_jersey, "mohair": mohair, "pointelle": pointelle, "seed_stitch": seed_stitch,
    "sherpa": sherpa, "chenille": chenille, "shantung": shantung, "moire": moire, "taffeta": taffeta,
    "organza": organza, "glitter": glitter, "beaded": beaded, "perforated": perforated, "vinyl": vinyl, "snake": snake,
    "ottoman": ottoman, "straw": straw, "pleated": pleated, "smocked": smocked,
    "satin_stripe": satin_stripe, "felt": felt, "lurex_knit": lurex_knit,
    "pinstripe_wool": pinstripe_wool, "houndstooth_wool": houndstooth_wool, "patchwork": patchwork,
    "needlecord": needlecord, "jumbo_cord": jumbo_cord, "scuba": scuba, "gloss_puffer": gloss_puffer, "guipure": guipure,
}

# Each fabric's catalog entry: the style tags archetypes weight it by, whether a print goes on it, and how common it is.
NO_PRINT = {"denim", "croc", "sequins", "lace", "quilted", "diamond_quilt", "corduroy",
            "mesh", "rubber", "metallic", "cable_knit", "faux_fur", "eyelet", "tulle", "crochet", "tweed",
            "herringbone", "jacquard", "velvet", "boucle", "seersucker", "terry", "waffle",
            "stonewash", "acid_wash", "waxed", "pointelle", "seed_stitch", "sherpa", "chenille", "moire",
            "glitter", "beaded", "perforated", "vinyl", "snake", "ottoman", "straw", "smocked", "marl", "satin_stripe", "pinstripe_wool", "houndstooth_wool", "patchwork",
            "jumbo_cord", "gloss_puffer", "guipure"}
# Fabrics that take only some prints: a knit takes only what is knitted into it (Fair Isle, stripes, argyle), not a
# printed floral; a leather only an animal print. Prints with one of these tags (prints.KNITTED's "knitted",
# prints.ANIMAL's "animal").
PRINT_TAGS = {"knit": ["knitted"], "chunky_knit": ["knitted"], "mohair": ["knitted"], "lurex_knit": ["knitted"],
              "rib_knit": ["knitted"], "wide_rib": ["knitted"],
              # Leathers take animal prints (leopard pumps, zebra boots, snakeskin loafers) and nothing else.
              "leather": ["animal"], "suede": ["animal"], "patent": ["animal"]}
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
    "dobby": ["bookish", "elegant", "casual"], "crinkle": ["casual", "cute"], "stonewash": ["casual", "street"],
    "acid_wash": ["street", "gothic"], "nylon": ["sporty", "street"], "waxed": ["bookish", "casual", "street"],
    "marl": ["casual", "sporty", "street"], "slub_jersey": ["casual"], "mohair": ["cute", "elegant", "gothic"],
    "pointelle": ["cute", "date"], "seed_stitch": ["bookish", "cute", "casual"], "sherpa": ["casual", "cute", "street"],
    "chenille": ["cute", "casual"], "shantung": ["elegant", "date"], "moire": ["elegant", "gothic"],
    "taffeta": ["elegant", "date", "cute"], "organza": ["elegant", "cute", "date"], "glitter": ["date", "cute", "street"],
    "beaded": ["elegant", "date"], "perforated": ["sporty", "street"], "vinyl": ["gothic", "street", "date"],
    "snake": ["street", "elegant", "gothic"], "ottoman": ["elegant", "bookish"], "straw": ["casual", "cute"],
    "pleated": ["cute", "elegant", "bookish"], "smocked": ["cute", "casual", "date"],
    "satin_stripe": ["elegant", "bookish", "date"], "felt": ["bookish", "elegant", "casual"], "lurex_knit": ["date", "cute", "elegant"],
    "pinstripe_wool": ["elegant", "bookish", "gothic"], "houndstooth_wool": ["elegant", "bookish"],
    "patchwork": ["cute", "casual", "street"], "needlecord": ["bookish", "casual", "cute"],
    "jumbo_cord": ["casual", "street", "bookish"], "scuba": ["sporty", "street", "date"], "gloss_puffer": ["street", "sporty"],
    "guipure": ["elegant", "date", "gothic"],
}

# Fabrics that usually come in their own colours, and how often an item made in one is: (chance, hex colours). Denim
# is mostly indigo, straw natural, leather black and browns; the rest take the outfit's colours.
NATURAL = {
    "denim": (0.75, ["#27334d", "#2f4670", "#3d5f8f", "#5a7caa", "#1d1d22", "#7a7f88"]),
    "stonewash": (0.8, ["#5c7eab", "#7898c0", "#47648e", "#8e949c"]),
    "acid_wash": (0.85, ["#6f8db5", "#93a8c4", "#4c4e55", "#a3a8b0"]),
    "chambray": (0.7, ["#88a4c6", "#6d8db5", "#a9bcd6"]),
    "leather": (0.6, ["#1b1919", "#3b281f", "#7f4e2c", "#a87448", "#521b1f"]),
    "suede": (0.6, ["#ae8257", "#9a7149", "#56392a", "#7a746e", "#66653f"]),
    "croc": (0.5, ["#1b1919", "#4a3022", "#8a5a32", "#2e3b2c"]),
    "snake": (0.5, ["#c9b693", "#8a7458", "#1b1919", "#b8a890"]),
    "straw": (0.85, ["#d8c08a", "#c7a76d", "#e5d5ae", "#35302b"]),
    "tweed": (0.5, ["#6a5c47", "#77786a", "#8a7963", "#474b57", "#6d5040"]),
    "herringbone": (0.5, ["#59595d", "#6a5c47", "#3e4350", "#7a7468"]),
    "corduroy": (0.45, ["#8a5a30", "#6b5a3a", "#a07848", "#4a5a3a", "#712a2a", "#3e3530"]),
    "waxed": (0.7, ["#4a5236", "#38382d", "#5a4632", "#2a3040"]),
    "faux_fur": (0.5, ["#efe6d6", "#d6c6ad", "#b5977a", "#2a2624"]),
    "sherpa": (0.65, ["#f0e8d8", "#ded0b8", "#c4ab8e"]),
    "marl": (0.6, ["#9a9a9e", "#7b7b81", "#c6c6ca", "#4b4b51"]),
    "metallic": (0.6, ["#c9a24a", "#b8bcc4", "#c49080", "#8a8c92"]),
    "vinyl": (0.5, ["#141416", "#9a1a20", "#e9e4dc"]),
    "patent": (0.35, ["#141416", "#9a1a20", "#c8a08a"]),
    "canvas": (0.3, ["#c2a878", "#68684a", "#e3d9c4", "#2c3346"]),
    "linen": (0.3, ["#d8ccb4", "#efebe2", "#b9a98c"]),
    "ripstop": (0.3, ["#5b6648", "#2b3140", "#c8b48c"]),
}

# How common each fabric is among the ones a garment can be made in (1 unless listed): statement fabrics are rarer.
WEIGHT = {
    "vinyl": 0.35, "glitter": 0.5, "beaded": 0.5, "acid_wash": 0.5, "metallic": 0.6, "snake": 0.6, "croc": 0.6,
    "faux_fur": 0.6, "lurex_knit": 0.6, "moire": 0.6, "mesh": 0.6, "perforated": 0.6, "rubber": 0.6, "organza": 0.7,
    "taffeta": 0.7, "sequins": 0.7, "patent": 0.8, "straw": 0.8, "crochet": 0.8, "sherpa": 0.8, "patchwork": 0.6,
    "denim": 1.4, "jersey": 1.3, "twill": 1.2, "poplin": 1.2, "knit": 1.2, "leather": 1.2,
}

# How much of each fabric's structure is added as light (the shader's _BaseLift), so it reads on dark cloth: the
# shiny ones most, their highlights being the light they reflect.
DEFAULT_LIFT = 0.12
LIFT = {
    "plain": 0.04, "poplin": 0.06, "chiffon": 0.06, "crepe": 0.08, "jersey": 0.08, "lace": 0.06, "eyelet": 0.06,
    "tweed": 0.3, "herringbone": 0.2, "marl": 0.35, "chambray": 0.2, "denim": 0.18, "stonewash": 0.3, "acid_wash": 0.5,
    "boucle": 0.25, "sherpa": 0.3, "mohair": 0.3, "faux_fur": 0.3, "chenille": 0.25, "velvet": 0.1, "corduroy": 0.2,
    "knit": 0.2, "chunky_knit": 0.22, "cable_knit": 0.2, "seed_stitch": 0.18, "pointelle": 0.18,
    "satin": 0.4, "shantung": 0.3, "moire": 0.35, "taffeta": 0.38, "organza": 0.08, "lycra": 0.32, "nylon": 0.3,
    "metallic": 0.5, "glitter": 0.6, "sequins": 0.5, "beaded": 0.45, "jacquard": 0.3,
    "leather": 0.28, "patent": 0.45, "vinyl": 0.65, "rubber": 0.45, "croc": 0.3, "snake": 0.3, "perforated": 0.2,
    "waxed": 0.32, "quilted": 0.22, "diamond_quilt": 0.2, "straw": 0.2,
    "satin_stripe": 0.3, "felt": 0.1, "lurex_knit": 0.4, "pinstripe_wool": 0.35, "houndstooth_wool": 0.3, "patchwork": 0.25,
    "needlecord": 0.2, "jumbo_cord": 0.22, "scuba": 0.35, "gloss_puffer": 0.5, "guipure": 0.08,
}
