"""Building blocks for seamless (tileable) texture tiles, in numpy: every function returns a float image in [0, 1] whose
left edge meets its right and top meets its bottom, so the tile repeats without a seam. Used by make_textures.py."""

import numpy as np


def grid(size):
    """(x, y) in [0, 1), size x size, x across and y down."""
    y, x = np.mgrid[0:size, 0:size] / size
    return x, y


def value_noise(size, cells, seed, octaves=1, persistence=0.5):
    """Smooth tileable noise: random values on a ``cells`` x ``cells`` lattice (wrapping), smoothstep-interpolated, summed
    over ``octaves`` (each twice as fine, ``persistence`` as strong). In [0, 1]."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size))
    amplitude, weight = 1.0, 0.0
    for octave in range(octaves):
        n = cells * 2 ** octave
        lattice = rng.random((n, n))
        x, y = grid(size)
        gx, gy = x * n, y * n
        x0, y0 = np.floor(gx).astype(int), np.floor(gy).astype(int)
        fx, fy = gx - x0, gy - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        x0, y0 = x0 % n, y0 % n
        x1, y1 = (x0 + 1) % n, (y0 + 1) % n
        top = lattice[y0, x0] * (1 - fx) + lattice[y0, x1] * fx
        bottom = lattice[y1, x0] * (1 - fx) + lattice[y1, x1] * fx
        total += amplitude * (top * (1 - fy) + bottom * fy)
        weight += amplitude
        amplitude *= persistence
    return total / weight


def stretched_noise(size, cells_x, cells_y, seed):
    """Tileable noise with different lattice sizes across and down (streaks: slubs in a weave, a velvet's nap)."""
    rng = np.random.default_rng(seed)
    lattice = rng.random((cells_y, cells_x))
    x, y = grid(size)
    gx, gy = x * cells_x, y * cells_y
    x0, y0 = np.floor(gx).astype(int), np.floor(gy).astype(int)
    fx, fy = gx - x0, gy - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    x0, y0 = x0 % cells_x, y0 % cells_y
    x1, y1 = (x0 + 1) % cells_x, (y0 + 1) % cells_y
    top = lattice[y0, x0] * (1 - fx) + lattice[y0, x1] * fx
    bottom = lattice[y1, x0] * (1 - fx) + lattice[y1, x1] * fx
    return top * (1 - fy) + bottom * fy


def blur(image, radius):
    """Box blur, ``radius`` pixels, wrapping (three passes look Gaussian)."""
    if radius < 1:
        return image
    out = image
    for _ in range(3):
        for axis in (0, 1):
            acc = np.zeros_like(out)
            for d in range(-radius, radius + 1):
                acc += np.roll(out, d, axis=axis)
            out = acc / (2 * radius + 1)
    return out


def worley(size, cells, seed, jitter=1.0):
    """Tileable cellular noise: (distance to the nearest feature point, to the second nearest, the nearest's id), with one
    point per cell of a ``cells`` x ``cells`` grid, distances in cell widths."""
    rng = np.random.default_rng(seed)
    points = (np.arange(cells)[:, None, None] * 0 + 0.5) + (rng.random((cells, cells, 2)) - 0.5) * jitter
    x, y = grid(size)
    gx, gy = x * cells, y * cells
    cx, cy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    first = np.full((size, size), 9.0)
    second = np.full((size, size), 9.0)
    ident = np.zeros((size, size), dtype=np.int64)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx, ny = cx + dx, cy + dy
            px = nx + points[ny % cells, nx % cells, 0]
            py = ny + points[ny % cells, nx % cells, 1]
            d = np.hypot(gx - px, gy - py)
            idx = (ny % cells) * cells + (nx % cells)
            closer = d < first
            second = np.where(closer, first, np.minimum(second, d))
            ident = np.where(closer, idx, ident)
            first = np.where(closer, d, first)
    return first, second, ident


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def normalise(image, low=0.0, high=1.0):
    """Stretched to [low, high]."""
    lo, hi = float(image.min()), float(image.max())
    return low + (image - lo) / max(hi - lo, 1e-9) * (high - low)


def stamp(canvas, shape, cx, cy, value=1.0, mode="max"):
    """Draws ``shape`` (a small float array, centred) onto ``canvas`` at pixel (cx, cy), wrapping round the edges."""
    size = canvas.shape[0]
    h, w = shape.shape
    ys = (np.arange(h) + int(round(cy)) - h // 2) % size
    xs = (np.arange(w) + int(round(cx)) - w // 2) % size
    block = canvas[np.ix_(ys, xs)]
    if mode == "max":
        canvas[np.ix_(ys, xs)] = np.maximum(block, shape * value)
    elif mode == "over":
        canvas[np.ix_(ys, xs)] = block * (1 - shape) + value * shape
    else:
        canvas[np.ix_(ys, xs)] = block + shape * value
    return canvas


def disc(radius, soft=1.0):
    """A filled anti-aliased disc, ``radius`` pixels."""
    r = int(np.ceil(radius + soft + 1))
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    return np.clip((radius - np.hypot(x, y)) / soft + 0.5, 0.0, 1.0)


def ring(outer, inner, soft=1.0):
    """An anti-aliased ring between radii ``inner`` and ``outer`` pixels."""
    r = int(np.ceil(outer + soft + 1))
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    d = np.hypot(x, y)
    return np.clip(np.minimum((outer - d) / soft + 0.5, (d - inner) / soft + 0.5), 0.0, 1.0)


def polygon_mask(points, size_px, soft=1.0):
    """A filled anti-aliased polygon (points in pixels, relative to the centre of a size_px square)."""
    r = size_px // 2
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(float)
    pts = np.asarray(points, float)
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    for i in range(n):  # even-odd rule
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        crosses = ((y1 > y) != (y2 > y)) & (x < (x2 - x1) * (y - y1) / np.where(y2 - y1 == 0, 1e-9, y2 - y1) + x1)
        inside ^= crosses
    mask = inside.astype(float)
    return blur_small(mask, soft)


def blur_small(mask, soft):
    if soft <= 0:
        return mask
    k = max(1, int(round(soft)))
    out = mask
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for d in range(-k, k + 1):
            acc += np.roll(out, d, axis=axis)
        out = acc / (2 * k + 1)
    return out


def scatter(size, count, seed, min_gap):
    """``count`` points spread over the tile (wrapping) at least ``min_gap`` pixels apart, where they fit."""
    rng = np.random.default_rng(seed)
    points = []
    tries = 0
    while len(points) < count and tries < count * 200:
        tries += 1
        p = rng.random(2) * size
        ok = True
        for q in points:
            d = np.abs(p - q)
            d = np.minimum(d, size - d)
            if np.hypot(*d) < min_gap:
                ok = False
                break
        if ok:
            points.append(p)
    return np.array(points)


def rotate_shape(shape, degrees):
    """A small shape rotated about its centre (nearest sampling, then softened)."""
    h, w = shape.shape
    n = int(np.ceil(np.hypot(h, w))) | 1
    c = n // 2
    y, x = np.mgrid[0:n, 0:n] - c
    a = np.radians(degrees)
    sx = np.cos(a) * x + np.sin(a) * y + w // 2
    sy = -np.sin(a) * x + np.cos(a) * y + h // 2
    xi, yi = np.round(sx).astype(int), np.round(sy).astype(int)
    valid = (xi >= 0) & (xi < w) & (yi >= 0) & (yi < h)
    out = np.zeros((n, n))
    out[valid] = shape[yi[valid], xi[valid]]
    return blur_small(out, 0.6)


def is_tileable(image, tolerance=0.25):
    """Whether the tile's opposite edges meet: the jump across each seam is no bigger than a typical jump inside."""
    inner = np.abs(np.diff(image, axis=1)).mean() + np.abs(np.diff(image, axis=0)).mean()
    seam = np.abs(image[:, 0] - image[:, -1]).mean() + np.abs(image[0, :] - image[-1, :]).mean()
    return seam <= inner * (1 + tolerance) + 1e-3
