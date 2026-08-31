# Derive TerrainRegion boxes for a Sheet's Front from its hand-drawn map PNG.
#
# The map is drawn portrait at 300 DPI (2550x3300 for an 8.5x11 sheet) and placed on the
# sheet rotated +90 deg (counter-clockwise), so:
#   sheet X = (py - H/2) / PPU ;  sheet Y = (px - W/2) / PPU   (px right, py down)
# Landscape sheet spans X -5.5..5.5, Y -4.25..4.25 (SheetGeometry).
#
# Pixels are classified by colour (gray fill = wall, strong blue = water, green/brown =
# tree or island, near-black = stroke), pooled onto a 0.1-unit occupancy grid, cleaned
# with morphological closing/opening, and greedily decomposed into maximal rectangles.
# Trees become one padded bounding box each; the island (the green blob whose surrounding
# ring is water) and a ring around it are carved out of the water. A flood-fill with the
# player's half-extents then reports which sheet edges are reachable, so sealed corridors
# are a decision, not a surprise (plan 2026-08-31-map1-terrain.md, review finding B1/N3).
#
# Usage:
#   python Tools/extract_map_regions.py <map.png> <out_boxes.json> [overlay.png]
#
# Output JSON: [{"kind": "wall"|"water"|"tree", "cx", "cy", "w", "h"}, ...] in sheet units.
import json
import sys

import numpy as np
from PIL import Image, ImageDraw

PPU = 300.0
CELL = 30            # px per grid cell (0.1 units)
WALL_FRAC = 0.30     # min gray fraction for a wall cell
WATER_FRAC = 0.25    # min strong-blue fraction for a water cell
STROKE_PAD = 0.06    # units of padding around wall/tree boxes to swallow the black stroke
PLAYER_HALF = (0.22, 0.35)   # Player.prefab BoxCollider2D half-extents
SPAWN = (-2.0, -1.0)         # player start on the sheet (Desk.unity)

# Expected connectivity, asserted after extraction so a re-run that silently changes the
# map's routes fails loudly (plan 2026-08-31-map1-terrain.md, Q1 answer "sealed as drawn").
# Edit these alongside the map or when Aaron changes the intended routing.
ISLAND = (3.3, -0.9)                       # a point on the island, for the water-lock check
EXPECT_EDGES_NO_ABILITIES = {"left"}       # neighbourless edge only; exits are fold-gated
EXPECT_EDGES_WITH_SWIM = {"left", "right"}
EXPECT_ISLAND_WATERLOCKED = True           # unreachable on foot, reachable with Swim


def dil(x):
    y = x.copy()
    y[1:, :] |= x[:-1, :]; y[:-1, :] |= x[1:, :]
    y[:, 1:] |= x[:, :-1]; y[:, :-1] |= x[:, 1:]
    return y


def ero(x):
    y = x.copy()
    y[1:, :] &= x[:-1, :]; y[:-1, :] &= x[1:, :]
    y[:, 1:] &= x[:, :-1]; y[:, :-1] &= x[:, 1:]
    return y


def close_open(m):
    c = ero(dil(m))       # closing: bridge 1-cell gaps in wobbly strokes
    return dil(ero(c))    # opening: shave 1-cell specks and fuzz protrusions


def components(mask):
    lbl = np.zeros(mask.shape, dtype=int)
    cur = 0
    for i in range(mask.shape[0]):
        for j in range(mask.shape[1]):
            if mask[i, j] and lbl[i, j] == 0:
                cur += 1
                stack = [(i, j)]
                lbl[i, j] = cur
                while stack:
                    y, x = stack.pop()
                    for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                        if 0 <= ny < mask.shape[0] and 0 <= nx < mask.shape[1] \
                                and mask[ny, nx] and lbl[ny, nx] == 0:
                            lbl[ny, nx] = cur
                            stack.append((ny, nx))
    return lbl, cur


def max_rect(mask):
    best = (0, None)
    heights = np.zeros(mask.shape[1], dtype=int)
    for i in range(mask.shape[0]):
        heights = np.where(mask[i], heights + 1, 0)
        stack = []
        for j in range(mask.shape[1] + 1):
            h = heights[j] if j < mask.shape[1] else 0
            start = j
            while stack and stack[-1][1] >= h:
                s, sh = stack.pop()
                if sh * (j - s) > best[0]:
                    best = (sh * (j - s), (i - sh + 1, i, s, j - 1))
                start = s
            if not stack or h > stack[-1][1]:
                stack.append((start, h))
    return best[1]


def decompose(mask, min_cells=6, max_boxes=30, coverage=0.95):
    m = mask.copy()
    total = m.sum()
    out = []
    while len(out) < max_boxes and (total - m.sum()) / max(1, total) < coverage:
        rect = max_rect(m)
        if rect is None:
            break
        y0, y1, x0, x1 = rect
        if (y1 - y0 + 1) * (x1 - x0 + 1) < min_cells:
            break
        out.append(rect)
        m[y0:y1 + 1, x0:x1 + 1] = False
    return out


def extract(src):
    img = Image.open(src).convert("RGBA")
    arr = np.asarray(img).astype(np.int32)
    rgb, a = arr[..., :3], arr[..., 3:4]
    rgb = (rgb * a + 255 * (255 - a)) // 255  # composite on white
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    v = rgb.max(axis=2)
    sat = v - rgb.min(axis=2)

    black = v < 90
    gray = (sat < 40) & (v >= 90) & (v < 215)
    blue = (b > 120) & (b - r > 60) & (b - g > 45)
    green = ((g > 80) & (g - r > 25) & (g - b > 25)) | \
            ((r > 100) & (r - b > 40) & (r - g > 20) & (g > b))  # canopy | trunk

    H, W = v.shape
    gh, gw = H // CELL, W // CELL

    def cell_frac(mask):
        return mask[:gh * CELL, :gw * CELL].reshape(gh, CELL, gw, CELL).mean(axis=(1, 3))

    wall_cells = close_open(cell_frac(gray) > WALL_FRAC)
    water_raw = cell_frac(blue) > WATER_FRAC
    green_cells = cell_frac(green) > 0.20
    black_cells = cell_frac(black) > 0.25

    # island = green blob whose surrounding ring is water; trees = the other green blobs.
    # Sketchy strokes split each tree into fragments, so merge blobs by dilation first.
    gl, gn = components(dil(dil(green_cells)))
    trees, island = [], np.zeros_like(green_cells)
    for k in range(1, gn + 1):
        comp = gl == k
        cells = comp & green_cells
        if cells.sum() < 20:
            continue
        ring = dil(comp) & ~comp
        if (ring & water_raw).sum() / max(1, ring.sum()) > 0.6:
            island |= cells
        else:
            ys, xs = np.where(cells)
            trees.append((ys.min(), ys.max(), xs.min(), xs.max()))
    if not island.any():
        raise SystemExit("no island found inside the water -- check thresholds")

    # water = blue fill plus the lake's black waterline, minus the island and a ring
    # around it (its own outline stays land, so the island is standable to its edge)
    isl_dil = dil(dil(dil(island)))
    lake_line = black_cells & dil(dil(water_raw))
    water_cells = (water_raw | lake_line) & ~isl_dil

    # a 1-cell dilation before decomposing fills staircase notches from wobbly strokes,
    # trading <=0.1u of generosity for far fewer boxes
    wall_rects = decompose(dil(wall_cells))
    water_rects = decompose(dil(water_cells) & ~isl_dil, coverage=0.97)

    half_w, half_h = W / 2.0, H / 2.0

    def to_sheet(rect, pad):
        cy0, cy1, cx0, cx1 = rect
        x0 = (cy0 * CELL - half_h) / PPU - pad
        x1 = ((cy1 + 1) * CELL - half_h) / PPU + pad
        y0 = (cx0 * CELL - half_w) / PPU - pad
        y1 = ((cx1 + 1) * CELL - half_w) / PPU + pad
        x0, x1 = max(x0, -half_h / PPU), min(x1, half_h / PPU)
        y0, y1 = max(y0, -half_w / PPU), min(y1, half_w / PPU)
        return {"cx": round((x0 + x1) / 2, 3), "cy": round((y0 + y1) / 2, 3),
                "w": round(x1 - x0, 3), "h": round(y1 - y0, 3)}

    boxes = [{"kind": "wall", **to_sheet(rc, STROKE_PAD)} for rc in wall_rects]
    boxes += [{"kind": "water", **to_sheet(rc, 0.0)} for rc in water_rects]
    boxes += [{"kind": "tree", **to_sheet(bb, STROKE_PAD)} for bb in trees]
    return boxes, rgb.astype(np.uint8)


def reachability(boxes, kinds, label):
    """Flood-fill from the spawn with the player's half-extents; report reachable edges."""
    step = 0.05
    sw, sh = 11.0, 8.5
    hx, hy = PLAYER_HALF
    nx, ny = int(sw / step), int(sh / step)
    free = np.ones((nx, ny), dtype=bool)
    xs = -sw / 2 + (np.arange(nx) + 0.5) * step
    ys = -sh / 2 + (np.arange(ny) + 0.5) * step
    free[np.abs(xs) > sw / 2 - hx, :] = False
    free[:, np.abs(ys) > sh / 2 - hy] = False
    for b in boxes:
        if b["kind"] not in kinds:
            continue
        ix = np.abs(xs - b["cx"]) < b["w"] / 2 + hx
        iy = np.abs(ys - b["cy"]) < b["h"] / 2 + hy
        free[np.ix_(ix, iy)] = False
    si, sj = int((SPAWN[0] + sw / 2) / step), int((SPAWN[1] + sh / 2) / step)
    if not free[si, sj]:
        raise SystemExit(f"[{label}] spawn {SPAWN} is inside a solid box")
    seen = np.zeros_like(free)
    stack = [(si, sj)]
    seen[si, sj] = True
    while stack:
        i, j = stack.pop()
        for a, b2 in ((i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1)):
            if 0 <= a < nx and 0 <= b2 < ny and free[a, b2] and not seen[a, b2]:
                seen[a, b2] = True
                stack.append((a, b2))
    edge = {
        "left": seen[int(hx / step) + 1, :].any(),
        "right": seen[nx - int(hx / step) - 2, :].any(),
        "bottom": seen[:, int(hy / step) + 1].any(),
        "top": seen[:, ny - int(hy / step) - 2].any(),
    }
    print(f"[{label}] reachable edges: " +
          ", ".join(k for k, on in edge.items() if on) + (" (none)" if not any(edge.values()) else ""))
    ii, ij = int((ISLAND[0] + sw / 2) / step), int((ISLAND[1] + sh / 2) / step)
    return edge, bool(seen[ii, ij])


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__ or "usage: extract_map_regions.py <map.png> <out.json> [overlay.png]")
    src, out_json = sys.argv[1], sys.argv[2]
    boxes, rgb = extract(src)
    counts = {}
    for b in boxes:
        counts[b["kind"]] = counts.get(b["kind"], 0) + 1
    print("boxes:", counts)

    edges_walk, island_walk = reachability(boxes, {"wall", "water", "tree"}, "no abilities")
    edges_swim, island_swim = reachability(boxes, {"wall", "tree"}, "with Swim")

    got_walk = {k for k, on in edges_walk.items() if on}
    got_swim = {k for k, on in edges_swim.items() if on}
    if got_walk != EXPECT_EDGES_NO_ABILITIES:
        raise SystemExit(f"connectivity changed: no-ability edges {got_walk}, "
                         f"expected {EXPECT_EDGES_NO_ABILITIES} -- JSON not written")
    if got_swim != EXPECT_EDGES_WITH_SWIM:
        raise SystemExit(f"connectivity changed: with-Swim edges {got_swim}, "
                         f"expected {EXPECT_EDGES_WITH_SWIM} -- JSON not written")
    if EXPECT_ISLAND_WATERLOCKED and (island_walk or not island_swim):
        raise SystemExit(f"island water-lock broken: on-foot={island_walk}, "
                         f"with-Swim={island_swim} -- JSON not written")
    print("connectivity matches the expected state (sealed as drawn; island water-locked)")

    with open(out_json, "w") as f:
        json.dump(boxes, f, indent=1)
    print("wrote", out_json)

    if len(sys.argv) > 3:
        land = Image.fromarray(np.rot90(rgb, 1).copy()).convert("RGBA")
        lay = Image.new("RGBA", land.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        h2, w2 = rgb.shape[0] / 2.0, rgb.shape[1] / 2.0

        def to_px(x, y):
            return (x * PPU + h2, w2 - 1 - y * PPU)

        for b in boxes:
            p0 = to_px(b["cx"] - b["w"] / 2, b["cy"] + b["h"] / 2)
            p1 = to_px(b["cx"] + b["w"] / 2, b["cy"] - b["h"] / 2)
            c = {"wall": (255, 0, 0), "water": (0, 200, 255), "tree": (255, 0, 255)}[b["kind"]]
            d.rectangle([p0, p1], outline=c + (255,), fill=c + (70,), width=4)
        land = Image.alpha_composite(land, lay).convert("RGB")
        d = ImageDraw.Draw(land)
        sx, sy = to_px(*SPAWN)
        d.ellipse([sx - 20, sy - 20, sx + 20, sy + 20], fill=(255, 128, 0))
        land.resize((1320, 1020)).save(sys.argv[3])
        print("wrote", sys.argv[3])


if __name__ == "__main__":
    main()
