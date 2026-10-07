#!/usr/bin/env python3
"""Generate class skeletons from the Borland class records: Stage 2 of
Herculan/docs/herculan/plan-completing-coverage.md.

    python tools/scripts/es2_skeletons.py BIN [--write]

For every class record (tools/scripts/es2_classes.py) of BIN that has no known_structs.json layout,
a struct of the record's size whose only fields are the bases, embedded at their subobject offsets,
and the vtable pointer typed with the class's vtable shape. A class whose vtable has a shape of its
own while its primary base's has another inlines that base instead ("inline": true), so that the
vtable pointer can be retyped.

Vtable shapes: a class whose table is as long as its primary base's shares the base's shape; in
DBSIM a class whose VSHELL namesake's table has a shape takes a copy of it when every slot's
function has the same name in both binaries; any other class roots a new shape. A new shape's first
slots are its parent shape's; each further slot is named for the role its implementations' names
agree on (the part after the first '_', less a trailing NoOp/None), or slot_0xNN.

`this` typing ("applications"): a function in the primary vtables of a set of classes, or the
+0x28 destructor of their records, takes the classes' common base as its first parameter. A name
prefix is not evidence: Mech_ComponentGeometryTest_Candidate's first parameter is a component
record and Cam_AttachTo's the object followed, so a non-virtual method is typed when it is read.
Functions with a known_symbols signature own their parameters and are left out, as are first
parameters already typed with a struct.

Without --write it only reports. Existing entries are never changed: a class that already has a
layout (by name, or by the struct's "class" key) is reported when its size disagrees with the record.
"""

from __future__ import annotations

import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_naming as N  # noqa: E402

GS = os.path.join(N.REPO, "tools", "ghidra_scripts")
STRUCTS = os.path.join(GS, "known_structs.json")
VTABLES = os.path.join(GS, "known_vtables.json")
GENERIC = re.compile(r"^(Rtl_|Stub_|maybe_)")
# VSHELL spells a few of DBSIM's library functions differently; both name the same code.
TWIN = {"Rtl_PureError": "Rtl_PureVirtualCalled"}


def twin_name(name):
    if not name:
        return name
    name = TWIN.get(name, name)
    return name[len("Vshell"):] if name.startswith("Vshell") else name


def struct_name(cls):
    """A Ghidra-safe name for an RTTI class name, spelled as the template destructors are named:
    ARRAY<GUN_STATE *> -> ARRAY_GUN_STATE_Ptr."""
    m = re.fullmatch(r"(\w+)<(.+)>", cls)
    if not m:
        return cls
    arg = m.group(2)
    stars = arg.count("*")
    return f"{m.group(1)}_{arg.replace('*', '').strip()}" + ("_" + "Ptr" * stars if stars else "")


def role_of(name):
    if not name or GENERIC.match(name) or "_" not in name:
        return None
    r = re.sub(r"(NoOp|None)$", "", name.split("_", 1)[1])
    # A name like CTLWindow_Slot00NoOp records where the function sits, not what it does.
    return None if not r or re.fullmatch(r"Slot[0-9a-fA-F]+", r) else r


def fmt_impls(fns, known, cap=8):
    out = [f"{known[f]} ({f})" if f in known else f for f in fns[:cap]]
    if len(fns) > cap:
        out.append(f"and {len(fns) - cap} more")
    return ", ".join(out)


class Binary:
    def __init__(self, binary):
        self.binary = binary
        self.known = N.names(binary)
        self.fns = N.functions(binary)
        _, self.recs, vts = N.class_records(binary)
        self.tables, _, _ = N.build_tables(binary)
        self.vt = {}
        for va, lst in vts.items():
            if len(lst) > 1:
                print(f"NOTE {self.recs[va]['name']}: {len(lst)} primary vtables, using {lst[0]:08x}")
            self.vt[va] = f"{lst[0]:08x}"
        self.by_name = {r["name"]: va for va, r in self.recs.items()}

    def table(self, va):
        return self.tables.get(self.vt.get(va), [])

    def base0(self, va):
        for b, sub in self.recs[va]["bases"]:
            if sub == 0 and b in self.recs:
                return b
        return None

    def ancestors(self, va):
        out = []
        while va is not None and va not in out:
            out.append(va)
            va = self.base0(va)
        return out

    def lca(self, vas):
        vas = list(vas)
        common = set(self.ancestors(vas[0]))
        for v in vas[1:]:
            common &= set(self.ancestors(v))
        for a in self.ancestors(vas[0]):
            if a in common:
                return a
        return None

    def order(self):
        """Record VAs, every base before its derived classes."""
        done, out = set(), []

        def visit(va):
            if va in done:
                return
            done.add(va)
            for b, _ in self.recs[va]["bases"]:
                if b in self.recs:
                    visit(b)
            out.append(va)
        for va in sorted(self.recs, key=lambda v: self.recs[v]["name"]):
            visit(va)
        return out

    def vptr_owner(self, va):
        """(base VA, subobject offset) of the base whose own vtable pointer is the class's, or None."""
        r = self.recs[va]
        for b, sub in r["bases"]:
            if b in self.recs and sub <= r["vptr"] < sub + self.recs[b]["size"]:
                assert self.recs[b]["vptr"] == r["vptr"] - sub, (r["name"], self.recs[b]["name"])
                return b, sub
        return None


def load_structs():
    with open(STRUCTS, encoding="utf-8-sig") as f:
        return json.load(f)


def load_vtables():
    with open(VTABLES, encoding="utf-8-sig") as f:
        return json.load(f)


def decomp_params(body):
    """(parameter count, first parameter's declaration) from a decompile body's signature line."""
    m = N.HDR.match(body)
    text = re.sub(r"/\*.*?\*/", "", body[m.end():] if m else body, flags=re.S)
    for line in text.splitlines():
        s = line.strip()
        if not s:
            continue
        if "(" not in s:
            continue
        inner = s[s.index("(") + 1:s.rindex(")")] if ")" in s else ""
        inner = inner.strip()
        if inner in ("", "void"):
            return 0, ""
        depth, parts, cur = 0, [], ""
        for ch in inner:
            if ch == "," and depth == 0:
                parts.append(cur)
                cur = ""
                continue
            depth += ch == "("
            depth -= ch == ")"
            cur += ch
        parts.append(cur)
        return len(parts), parts[0].strip()
    return 0, ""


def generate(binary):
    B = Binary(binary)
    recs, known = B.recs, B.known
    ks, kv = load_structs(), load_vtables()
    report = []

    # Existing layouts, by RTTI class name.
    layout = {}
    for s in ks["structs"]:
        if s["binary"] == binary:
            layout[s.get("class", s["name"])] = s
    struct_of = {}
    for va, r in recs.items():
        s = layout.get(r["name"])
        struct_of[va] = s["name"] if s else struct_name(r["name"])
        if s and s["size"] != r["size"]:
            report.append(f"SIZE {s['name']} is 0x{s['size']:x}, record '{r['name']}' says 0x{r['size']:x}")

    # Shapes.
    shapes = {v["name"]: v for v in kv["vtables"] if v["binary"] == binary}
    shape_by_vt = {i["address"]: v["name"] for v in shapes.values() for i in v["instances"]}
    new_shapes, new_instances = {}, {}
    shape_len = {k: len(v["slots"]) for k, v in shapes.items()}
    shape_of = {}
    vs = None
    if binary == "DBSIM":
        vs = Binary("VSHELL")
        vs_shapes = {v["name"]: v for v in kv["vtables"] if v["binary"] == "VSHELL"}
        vs_shape_by_vt = {i["address"]: v["name"] for v in vs_shapes.values() for i in v["instances"]}

    def add_instance(sname, va):
        vt = B.vt[va]
        label = known.get(vt) or f"{struct_of[va]}Vtable"
        new_instances.setdefault(sname, []).append(
            {"address": vt, "label": label,
             "description": f"RTTI '{recs[va]['name']}', 0x{recs[va]['size']:x} bytes."})
        shape_by_vt[vt] = sname

    for va in B.order():
        r = recs[va]
        if r["vptr"] < 0 or va not in B.vt:
            continue
        vt = B.vt[va]
        if vt in shape_by_vt:
            shape_of[va] = shape_by_vt[vt]
            continue
        n = len(B.table(va))
        own = B.vptr_owner(va)
        parent = shape_of.get(own[0]) if own else None
        plen = shape_len.get(parent, 0)
        if parent and n == plen:
            shape_of[va] = parent
            add_instance(parent, va)
            continue
        if vs and r["name"] in vs.by_name:
            vva = vs.by_name[r["name"]]
            vsn = vs_shape_by_vt.get(vs.vt.get(vva))
            if vsn and len(vs_shapes[vsn]["slots"]) == n == len(vs.table(vva)):
                pairs = [(twin_name(B.known.get(f1)), twin_name(vs.known.get(f2)))
                         for (_, f1), (_, f2) in zip(B.table(va), vs.table(vva))]
                bad = [(i, a, b) for i, (a, b) in enumerate(pairs) if a != b]
                if not bad:
                    sname = vsn[len("Vshell"):] if vsn.startswith("Vshell") else vsn + "Dbsim"
                    if sname not in new_shapes and sname not in shapes:
                        new_shapes[sname] = {"root": va, "copy": vsn, "parent": None,
                                             "slots": [dict(s) for s in vs_shapes[vsn]["slots"]]}
                        shape_len[sname] = n
                    shape_of[va] = sname
                    add_instance(sname, va)
                    continue
                report.append(f"PAIR {r['name']}: {vsn} does not pair up at "
                              + ", ".join(f"+0x{4 * i:x} {a} / {b}" for i, a, b in bad))
        sname = f"{struct_of[va]}Vtable"
        assert sname not in shapes and sname not in new_shapes, sname
        new_shapes[sname] = {"root": va, "copy": None, "parent": parent if parent and n > plen else None}
        shape_len[sname] = n
        if parent and n < plen:
            report.append(f"SHORT {r['name']}: {n} slots, fewer than its base's {parent} ({plen})")
        shape_of[va] = sname
        add_instance(sname, va)

    # Slot roles and descriptions for the new shapes, from every class that uses each.
    for sname, sh in new_shapes.items():
        users = [va for va, s in shape_of.items() if s == sname]
        users.sort(key=lambda v: (len(B.ancestors(v)), recs[v]["name"]))
        n = len(B.table(sh["root"]))
        p = sh["parent"]
        pslots = (new_shapes[p]["out"]["slots"] if p in new_shapes else shapes[p]["slots"]) if p else []
        inherited = len(pslots)
        slots = []
        for i in range(n):
            o = 4 * i
            impls = []
            for u in users:
                t = B.table(u)
                if i < len(t) and t[i][1] not in impls:
                    impls.append(t[i][1])
            where = "Implementations: " + fmt_impls(impls, known) + "."
            if sh["copy"]:
                name = sh["slots"][i]["name"]
                desc = f"The role of {sh['copy']} +0x{o:02x} (VSHELL). {where}"
            elif i < inherited:
                name = pslots[i]["name"]
                desc = f"As {sh['parent']} +0x{o:02x}. {where}"
            else:
                roles = {role_of(known.get(f)) for f in impls} - {None}
                name = roles.pop() if len(roles) == 1 else f"slot_0x{o:02x}"
                desc = where
            slots.append({"offset": o, "name": name, "description": desc})
        seen = set()
        for s in slots:
            if s["name"] in seen:
                s["name"] = f"{s['name']}_0x{s['offset']:02x}"
            seen.add(s["name"])
        root = recs[sh["root"]]
        more = len(users) - 1
        desc = (f"The vtable of RTTI '{root['name']}' ({B.vt[sh['root']]})"
                + (f" and of the {more} class{'es' if more > 1 else ''} derived from it that keep its length" if more else "")
                + f": {n} slots, read from the image up to the next class's type descriptor. ")
        if sh["copy"]:
            desc += f"Each slot's function has the same name as in VSHELL's {sh['copy']}, whose slot names these are."
        else:
            if sh["parent"]:
                desc += f"The first {inherited} slots are {sh['parent']}'s. "
            desc += ("A slot is named for the role its implementations' names agree on, and is "
                     "slot_0xNN where they disagree or none is named.")
        sh["out"] = {"name": sname, "binary": binary, "description": desc, "slots": slots}

    def shape_type(va):
        s = shape_of.get(va)
        return f"{s} *" if s else "void *"

    # Structs.
    new_structs = []
    for va in B.order():
        r = recs[va]
        if r["name"] in layout:
            continue
        fields = []
        own = B.vptr_owner(va) if r["vptr"] >= 0 else None
        for b, sub in r["bases"]:
            if b not in recs:
                report.append(f"BASE {r['name']}: base record {b:08x} not found")
                continue
            br = recs[b]
            if sub + br["size"] > r["size"]:
                report.append(f"BASE {r['name']}: {br['name']} at +0x{sub:x} runs past 0x{r['size']:x}")
                continue
            f = {"offset": sub, "width": br["size"], "type": struct_of[b]}
            inline = own is not None and own[0] == b and shape_of.get(va) != shape_of.get(b)
            if inline:
                f["inline"] = True
            else:
                f["name"] = "base" if sub == 0 else f"base_{struct_of[b]}"
            f["confidence"] = "high"
            f["description"] = (f"Base {br['name']} at +0x{sub:x}, from the class record"
                                + (", inlined so that the vtable pointer takes this class's shape." if inline else "."))
            fields.append(f)
        if r["vptr"] >= 0 and (own is None or any(f.get("inline") for f in fields)):
            fields.append({"offset": r["vptr"], "width": 4, "type": shape_type(va), "name": "vtbl",
                           "confidence": "high",
                           "description": f"The vtable pointer, at the record's +0x08 offset; primary vtable {B.vt.get(va, 'not found')}."})
        fields.sort(key=lambda f: (f["offset"], not f.get("inline")))
        bases = ", ".join(f"{recs[b]['name']}" + (f" at +0x{sub:x}" if sub else "") for b, sub in r["bases"] if b in recs)
        s = {"name": struct_of[va], "binary": binary, "class": r["name"], "size": r["size"],
             "description": (f"RTTI '{r['name']}', 0x{r['size']:x} bytes"
                             + (f", derived from {bases}" if bases else ", no base")
                             + f" (class record {va:08x}). A skeleton: only the bases and the vtable pointer are placed."),
             "fields": fields}
        if s["name"] == r["name"]:
            del s["class"]
        new_structs.append(s)

    # Applications.
    have = {(a["function"], a["parameter"]) for a in ks["applications"]
            if a.get("binary", binary) == binary}
    sigs = {e["address"] for e in N.entries(binary) if e.get("signature")}
    bodies = N.bodies(binary)
    type_names = {s["name"] for s in ks["structs"]} | {s["name"] for s in new_structs} | set(shapes) | set(new_shapes)
    held, slot_at = {}, {}
    for va in recs:
        for o, fn in B.table(va):
            held.setdefault(fn, set()).add(va)
            slot_at.setdefault(fn, (va, o))
        if recs[va]["dtor"]:
            fn = f"{recs[va]['dtor']:08x}"
            held.setdefault(fn, set()).add(va)
    cands = {}
    for fn, classes in held.items():
        c = B.lca(classes)
        if c is None:
            continue
        if len(classes) == 1:
            (only,) = classes
            if fn in slot_at and slot_at[fn][0] == only:
                why = f"slot +0x{slot_at[fn][1]:x} of {recs[only]['name']}'s vtable ({B.vt[only]})"
            else:
                why = f"{recs[only]['name']}'s destructor (class record +0x28)"
        else:
            why = (f"held by the vtables or destructor fields of {len(classes)} classes "
                   f"({', '.join(sorted(recs[v]['name'] for v in classes)[:4])}{', ...' if len(classes) > 4 else ''}), "
                   f"whose common base is {recs[c]['name']}")
        cands[fn] = (c, why)
    apps, skipped = [], {"signature": 0, "no parameter": 0, "typed": 0, "listed": 0, "not a function": 0}
    for fn in sorted(cands):
        c, why = cands[fn]
        if fn not in B.fns:
            skipped["not a function"] += 1
            continue
        if fn in sigs:
            skipped["signature"] += 1
            continue
        if (fn, 0) in have:
            skipped["listed"] += 1
            continue
        count, first = decomp_params(bodies.get(fn, ""))
        if count == 0:
            skipped["no parameter"] += 1
            continue
        if any(t + " *" in first or t + "*" in first for t in type_names):
            skipped["typed"] += 1
            continue
        apps.append({"function": fn, "parameter": 0, "struct": struct_of[c], "binary": binary,
                     "description": f"{known.get(fn, 'FUN_' + fn)}'s this: {why}."})
    # Existing applications that disagree with the vtable evidence.
    for a in ks["applications"]:
        if a.get("binary", binary) != binary or a["parameter"] != 0 or a["function"] not in held:
            continue
        c = B.lca(held[a["function"]])
        if c is not None and struct_of[c] != a["struct"]:
            report.append(f"APP {a['function']} {known.get(a['function'], '')} is typed {a['struct']}; "
                          f"its vtables' common base is {recs[c]['name']} ({struct_of[c]})")
    return B, new_shapes, new_instances, new_structs, apps, skipped, report


def ordered(structs):
    """Every struct after the structs its fields name (a pointer field needs none), keeping the given
    order otherwise. Both binaries define some of the same names, so a struct is keyed by both."""
    by = {(s["binary"], s["name"]): s for s in structs}
    out, done = [], set()

    def visit(s, stack=()):
        key = (s["binary"], s["name"])
        if key in done:
            return
        assert key not in stack, stack
        for f in s["fields"]:
            if f["type"].endswith("*"):
                continue
            dep = (s["binary"], re.sub(r"\[\d+\]$", "", f["type"]))
            if dep in by and dep != key:
                visit(by[dep], stack + (key,))
        done.add(key)
        out.append(s)
    for s in structs:
        visit(s)
    return out


def write_structs(ks, new_structs, apps):
    ks["structs"] = ordered(ks["structs"] + new_structs)
    ks["applications"] += apps
    raw = open(STRUCTS, "rb").read()
    nl = "\r\n" if b"\r\n" in raw else "\n"
    text = json.dumps(ks, indent=2, ensure_ascii=False) + "\n"
    with open(STRUCTS, "w", encoding="utf-8", newline=nl) as f:
        f.write(text)


def write_vtables(new_shapes, new_instances):
    raw = open(VTABLES, "rb").read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    nl = "\r\n" if b"\r\n" in raw else "\n"
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    for sname, insts in new_instances.items():
        if sname in new_shapes:
            continue
        head = text.index(f'"name": "{sname}"')
        start = text.index('"instances": [', head)
        end = text.index("\n      ]", start)
        add = "".join(",\n        " + json.dumps(i, ensure_ascii=False) for i in insts)
        text = text[:end] + add + text[end:]
    blocks = []
    for sname, sh in new_shapes.items():
        o = sh["out"]
        lines = ["    {",
                 f'      "name": {json.dumps(o["name"])},',
                 f'      "binary": {json.dumps(o["binary"])},',
                 f'      "description": {json.dumps(o["description"], ensure_ascii=False)},',
                 '      "slots": [',
                 ",\n".join("        " + json.dumps(s, ensure_ascii=False) for s in o["slots"]),
                 "      ],",
                 '      "instances": [',
                 ",\n".join("        " + json.dumps(i, ensure_ascii=False) for i in new_instances.get(sname, [])),
                 "      ]",
                 "    }"]
        blocks.append("\n".join(lines))
    if blocks:
        tail = text.rindex("\n  ]\n}")
        text = text[:tail] + ",\n" + ",\n".join(blocks) + text[tail:]
    json.loads(text)
    data = text.replace("\n", nl).encode("utf-8")
    with open(VTABLES, "wb") as f:
        f.write((b"\xef\xbb\xbf" if bom else b"") + data)


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2 or sys.argv[1] not in ("DBSIM", "VSHELL"):
        print(__doc__)
        return 2
    binary = sys.argv[1]
    B, new_shapes, new_instances, new_structs, apps, skipped, report = generate(binary)
    print(f"== {binary}: {len(new_shapes)} new shapes, "
          f"{sum(len(v) for k, v in new_instances.items() if k not in new_shapes)} instances added to existing shapes, "
          f"{len(new_structs)} new structs, {len(apps)} applications; skipped {skipped}")
    for line in report:
        print(line)
    for sname, sh in new_shapes.items():
        o = sh["out"]
        print(f"\nSHAPE {sname} ({len(o['slots'])} slots{', copy of ' + sh['copy'] if sh['copy'] else ''}"
              f"{', parent ' + sh['parent'] if sh['parent'] else ''}): "
              + " ".join(s["name"] for s in o["slots"]))
        print("   instances: " + " ".join(i["label"] for i in new_instances.get(sname, [])))
    for sname, insts in new_instances.items():
        if sname not in new_shapes:
            print(f"\n+INST {sname}: " + " ".join(f"{i['address']}={i['label']}" for i in insts))
    print()
    for s in new_structs:
        print(f"STRUCT {s['name']} 0x{s['size']:x}: " + "; ".join(
            f"+0x{f['offset']:x} {f['type']}{' inline' if f.get('inline') else ''}" for f in s["fields"]))
    print()
    for a in apps:
        print(f"APPLY {a['function']} {a['struct']}: {a['description']}")
    if "--write" in sys.argv:
        ks = load_structs()
        write_vtables(new_shapes, new_instances)
        write_structs(ks, new_structs, apps)
        print("written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
