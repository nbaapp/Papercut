"""Asset-link check for hand-authored Unity YAML. Run from the project root: python Tools/check_links.py

Every {fileID, guid} reference in the scene/prefabs must resolve to a real object in the target asset,
including fileIDs Unity derives for objects inherited by a prefab variant ((base ^ instance) & 0x7FFF...)."""
import os, re, glob, sys
ROOT = os.getcwd()
MASK = 0x7FFFFFFFFFFFFFFF
BUILTIN = {"0000000000000000e000000000000000", "0000000000000000f000000000000000"}

def guid_map():
    m = {}
    for meta in glob.glob(os.path.join(ROOT, "Assets", "**", "*.meta"), recursive=True) + glob.glob(os.path.join(ROOT, "Library", "PackageCache", "*", "**", "*.cs.meta"), recursive=True) + glob.glob(os.path.join(ROOT, "Library", "PackageCache", "*", "**", "*.mat.meta"), recursive=True):
        g = re.search(r"^guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read(), re.M)
        if g: m[g.group(1)] = meta[:-5]
    return m

def blocks(text):
    return re.findall(r"^--- !u!(\d+) &(\d+)( stripped)?\n(.*?)(?=^--- |\Z)", text, re.M | re.S)

_cache = {}
def object_ids(path):
    """All addressable fileIDs of an asset: its own blocks plus derived IDs of everything inherited from nested/variant sources."""
    if path in _cache: return _cache[path]
    text = open(path, encoding="utf-8").read()
    ids = set()
    bl = blocks(text)
    for t, fid, stripped, body in bl:
        ids.add(int(fid))
    for t, fid, stripped, body in bl:
        if t == "1001":
            src = re.search(r"m_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]{32})", body).group(1)
            src_path = GUIDS.get(src)
            if src_path is None or not os.path.exists(src_path):
                print(f"  {os.path.relpath(path, ROOT)}: PrefabInstance {fid} source guid {src} missing"); ERR.append(1); continue
            for base in object_ids(src_path):
                ids.add((base ^ int(fid)) & MASK)
    _cache[path] = ids
    return ids

GUIDS = guid_map()
ERR = []
def check(path):
    text = open(path, encoding="utf-8").read()
    local = {int(f) for _, f, _, _ in blocks(text)}
    # duplicates
    all_ids = [int(f) for _, f, _, _ in blocks(text)]
    dup = {i for i in all_ids if all_ids.count(i) > 1}
    if dup: print(f"  {os.path.relpath(path, ROOT)}: duplicate fileIDs {dup}"); ERR.append(1)
    n_local = n_ext = 0
    for m in re.finditer(r"\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: (\d))?\}", text):
        fid, guid = int(m.group(1)), m.group(2)
        if fid == 0: continue
        if guid is None:
            n_local += 1
            if fid not in local:
                print(f"  {os.path.relpath(path, ROOT)}: local fileID {fid} not defined"); ERR.append(1)
        else:
            n_ext += 1
            if guid in BUILTIN: continue
            tgt = GUIDS.get(guid)
            if tgt is None: print(f"  {os.path.relpath(path, ROOT)}: guid {guid} has no asset"); ERR.append(1); continue
            if tgt.endswith((".prefab", ".unity")):
                if fid == 100100000: continue
                if fid not in object_ids(tgt):
                    print(f"  {os.path.relpath(path, ROOT)}: {fid} not found in {os.path.relpath(tgt, ROOT)}"); ERR.append(1)
            elif tgt.endswith(".cs"):
                if fid != 11500000: print(f"  {os.path.relpath(path, ROOT)}: script ref {fid}"); ERR.append(1)
            elif tgt.endswith(".mat"):
                if fid != 2100000: print(f"  {os.path.relpath(path, ROOT)}: material ref {fid}"); ERR.append(1)
            elif tgt.endswith((".png", ".psd")):
                # A texture is 2800000; a sprite sub-asset must be pinned in the .meta's internalIDToNameTable.
                meta = open(tgt + ".meta", encoding="utf-8").read()
                pinned = {int(x) for x in re.findall(r"^\s+213: (-?\d+)$", meta, re.M)}
                if fid != 2800000 and fid != 21300000 and fid not in pinned:  # 21300000 is the default single-sprite id
                    print(f"  {os.path.relpath(path, ROOT)}: sprite {fid} not pinned in {os.path.relpath(tgt, ROOT)}.meta"); ERR.append(1)
    print(f"{os.path.relpath(path, ROOT)}: {len(local)} objects, {n_local} local refs, {n_ext} external refs")

for p in sorted(glob.glob("Assets/Papercut/Prefabs/**/*.prefab", recursive=True)) + sorted(glob.glob("Assets/Papercut/Sheets/*.prefab")) + sorted(glob.glob("Assets/Scenes/*.unity")):
    check(os.path.join(ROOT, p))
print("FAIL" if ERR else "ALL LINKS OK"); sys.exit(1 if ERR else 0)
