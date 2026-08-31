# Rewrite a hand-authored Sheet variant prefab from a boxes JSON (extract_map_regions.py):
# removes every nested instance whose name override starts with "Test ", removes every
# previously generated region (any nested Wall/Water prefab instance), then adds one
# nested Wall/Water prefab instance per box under the variant's Front root.
#
# Re-running REPLACES all generated regions: box positions/sizes hand-tuned in the
# Inspector are lost on a re-run. The canonical box record for Map 1 lives beside the
# plan (Documents/Plans/2026-08-31-map1-boxes.json).
#
# YAML rules for hand-authored variants (memory: hand-authored-prefab-variants):
# - an object inherited through a nested instance gets fileID (base ^ instanceID) & 0x7FF...F
# - new instance IDs are multiples of 100000 so pairwise XORs exceed every source fileID
# - every added object needs a stripped Transform block plus an m_AddedGameObjects entry
# Run Tools/check_links.py afterwards.
#
# Usage:
#   python Tools/gen_sheet_regions.py <sheet_variant.prefab> <boxes.json>
import json
import re
import sys

WALL_GUID = "7a11b2c3d4e5f60718293a4b5c6d7e09"
WATER_GUID = "7a11b2c3d4e5f60718293a4b5c6d7e0a"
SHEET_GUID = "a2bf3af30e704404aac0f9a74c5a8530"   # base Sheet prefab
FRONT_STRIPPED = 4000000000002000                  # stripped Front transform in the variant
FRONT_SOURCE = 2001                                # Front transform fileID in the base Sheet
OUTER_INSTANCE = 4000000000000001                  # the variant's outer PrefabInstance
MASK = 0x7FFFFFFFFFFFFFFF

KIND_GUID = {"wall": WALL_GUID, "water": WATER_GUID, "tree": WALL_GUID}
KIND_NAME = {"wall": "Wall", "water": "Water", "tree": "Tree"}


def anchor(doc):
    m = re.match(r"^--- !u!\d+ &(\d+)", doc)
    return int(m.group(1)) if m else None


def fmt(v):
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: gen_sheet_regions.py <sheet_variant.prefab> <boxes.json>")
    prefab_path, boxes_path = sys.argv[1], sys.argv[2]
    src = open(prefab_path, encoding="utf-8").read()
    docs = re.split(r"(?=^--- !u!)", src, flags=re.M)
    header, body = docs[0], docs[1:]

    # nested instances named "Test *", plus previously generated regions
    # (any nested instance of the Wall/Water prefabs) so a re-run replaces, not duplicates
    test_instances = set()
    for doc in body:
        if not doc.startswith("--- !u!1001"):
            continue
        if re.search(r"propertyPath: m_Name\n      value: Test ", doc) or \
                re.search(r"m_SourcePrefab: \{fileID: \d+, guid: (?:" +
                          WALL_GUID + "|" + WATER_GUID + r"), type: 3\}", doc):
            test_instances.add(anchor(doc))
    if not test_instances:
        print("note: no 'Test *' or region instances found to remove")

    # drop those instances and every stripped block that belongs to them
    removed_anchors = set()
    kept = []
    for doc in body:
        a = anchor(doc)
        m = re.search(r"m_PrefabInstance: \{fileID: (\d+)\}", doc)
        owner = int(m.group(1)) if m and "stripped" in doc.splitlines()[0] else None
        if a in test_instances or owner in test_instances:
            removed_anchors.add(a)
            continue
        kept.append(doc)

    boxes = json.load(open(boxes_path))
    max_inst = max(a for a in (anchor(d) for d in kept) if a is not None)
    base_id = (max_inst // 100000 + 2) * 100000

    counters = {}
    new_docs, added_entries = [], []
    for k, bx in enumerate(boxes):
        inst = base_id + k * 100000
        guid = KIND_GUID[bx["kind"]]
        counters[bx["kind"]] = counters.get(bx["kind"], 0) + 1
        name = f"{KIND_NAME[bx['kind']]} {counters[bx['kind']]}"
        tid = (1001 ^ inst) & MASK
        mods = [
            (1000, "m_Name", name),
            (1001, "m_LocalPosition.x", fmt(bx["cx"])),
            (1001, "m_LocalPosition.y", fmt(bx["cy"])),
            (1001, "m_LocalPosition.z", "-0.05"),
            (1002, "m_Enabled", "0"),                 # the map drawing is the visual
            (1002, "m_Size.x", fmt(bx["w"])),
            (1002, "m_Size.y", fmt(bx["h"])),
            (1003, "m_Size.x", fmt(bx["w"])),
            (1003, "m_Size.y", fmt(bx["h"])),
            (1003, "m_SpriteTilingProperty.oldSize.x", fmt(bx["w"])),
            (1003, "m_SpriteTilingProperty.oldSize.y", fmt(bx["h"])),
            (1003, "m_SpriteTilingProperty.newSize.x", fmt(bx["w"])),
            (1003, "m_SpriteTilingProperty.newSize.y", fmt(bx["h"])),
        ]
        lines = [f"--- !u!1001 &{inst}", "PrefabInstance:", "  m_ObjectHideFlags: 0",
                 "  serializedVersion: 2", "  m_Modification:", "    serializedVersion: 3",
                 f"    m_TransformParent: {{fileID: {FRONT_STRIPPED}}}", "    m_Modifications:"]
        for fid, path, val in mods:
            lines += [f"    - target: {{fileID: {fid}, guid: {guid}, type: 3}}",
                      f"      propertyPath: {path}",
                      f"      value: {val}",
                      "      objectReference: {fileID: 0}"]
        lines += ["    m_RemovedComponents: []", "    m_RemovedGameObjects: []",
                  "    m_AddedGameObjects: []", "    m_AddedComponents: []",
                  f"  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}"]
        new_docs.append("\n".join(lines) + "\n")
        new_docs.append(f"--- !u!4 &{tid} stripped\nTransform:\n"
                        f"  m_CorrespondingSourceObject: {{fileID: 1001, guid: {guid}, type: 3}}\n"
                        f"  m_PrefabInstance: {{fileID: {inst}}}\n"
                        "  m_PrefabAsset: {fileID: 0}\n")
        added_entries.append(
            f"    - targetCorrespondingSourceObject: {{fileID: {FRONT_SOURCE}, guid: {SHEET_GUID}, type: 3}}\n"
            f"      insertIndex: -1\n"
            f"      addedObject: {{fileID: {tid}}}\n")

    # in the outer PrefabInstance: drop added-object entries pointing at removed blocks,
    # then splice in the new entries
    kept_anchors = {anchor(d) for d in kept}
    if OUTER_INSTANCE not in kept_anchors or FRONT_STRIPPED not in kept_anchors:
        raise SystemExit("target prefab is not a Sheet variant in the expected layout "
                         "(outer PrefabInstance / stripped Front transform missing) -- "
                         "aborting, file not written")

    entry_re = re.compile(
        r"    - targetCorrespondingSourceObject: \{fileID: \d+, guid: " + SHEET_GUID +
        r", type: 3\}\n      insertIndex: -1\n      addedObject: \{fileID: (\d+)\}\n")
    spliced = False
    for i, doc in enumerate(kept):
        if anchor(doc) != OUTER_INSTANCE:
            continue
        doc = entry_re.sub(
            lambda m: "" if int(m.group(1)) in removed_anchors else m.group(0), doc)
        new_doc = doc.replace("    m_AddedGameObjects:\n",
                              "    m_AddedGameObjects:\n" + "".join(added_entries), 1)
        spliced = new_doc != doc or not added_entries
        kept[i] = new_doc
    if not spliced:
        raise SystemExit("could not splice m_AddedGameObjects entries into the outer "
                         "PrefabInstance -- aborting, file not written")

    # keep the outer instance and its stripped sheet transforms last, new docs before them
    tail_ids = {OUTER_INSTANCE, FRONT_STRIPPED, 4000000000003000}
    rest = [d for d in kept if anchor(d) not in tail_ids]
    tail = [d for d in kept if anchor(d) in tail_ids]
    out = header + "".join(rest) + "".join(new_docs) + "".join(tail)

    anchors = re.findall(r"^--- !u!\d+ &(\d+)", out, flags=re.M)
    if len(anchors) != len(set(anchors)):
        raise SystemExit("duplicate anchors generated -- aborting, file not written")
    have = {int(a) for a in anchors}
    refs = {int(x) for x in re.findall(r"addedObject: \{fileID: (\d+)\}", out)}
    if refs - have:
        raise SystemExit(f"dangling addedObject refs {refs - have} -- aborting, file not written")

    open(prefab_path, "w", encoding="utf-8", newline="\n").write(out)
    print(f"wrote {prefab_path}: removed {len(test_instances)} old Test/region instances, "
          f"added {len(boxes)} regions ({counters})")


if __name__ == "__main__":
    main()
