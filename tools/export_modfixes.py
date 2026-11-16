"""
Export the per-mod fixes as JSON: which mod, which mod versions, which game versions.

The compatibility index (polyfill.doomods.com) reads this list to label a mod that only runs because a
fix patches it "Works with Polyfill" instead of "Works". A ModFix leaves no triage finding, so the reports
alone never told the index; Polyfill releases from the change on report such a mod as adaptable, and this
list corrects the reports that came before them.

Read from the source rather than from a running game: the four values are string literals on every fix
(or a `const string FixId`), and the registry in ModFixes/Fixes.cs says which fixes actually run. A fix whose
values are not literals, or a registered fix that cannot be found, is an error, so the list never silently
loses a fix. CI runs this for that reason.

Fixes for no mod in particular (Mod => "*") are left out. They repair patches that other mods aim at the
game, and those already reach the index as findings.

    python tools/export_modfixes.py > ../PolyfillIndex/src/data/mod-fixes.json
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXES = ROOT / "ModFixes"

CLASS = re.compile(r"\bclass\s+(\w+)\s*:\s*(\w+)")
PROP = re.compile(r"override\s+string\s+(Id|Mod|ModVersions|GameVersions)\s*=>\s*([^;]+);")
CONST = re.compile(r"const\s+string\s+(\w+)\s*=\s*\"([^\"]*)\"\s*;")
LITERAL = re.compile(r"^\"([^\"]*)\"$")


def fail(text):
    print(f"export_modfixes: {text}", file=sys.stderr)
    sys.exit(1)


def registered():
    text = (FIXES / "Fixes.cs").read_text(encoding="utf-8")
    start = text.find("List<Fix> All")
    if start < 0:
        fail("ModFixes/Fixes.cs has no 'List<Fix> All' registry")
    end = text.find("};", start)
    return re.findall(r"new\s+(\w+)\s*\(\s*\)", text[start:end])


def shared_consts():
    """`Contract.<Class>.<Name>` for every string constant in Contract/, which some fixes share a range from."""
    out = {}
    for path in sorted((ROOT / "Contract").glob("*.cs")):
        text = path.read_text(encoding="utf-8")
        heads = list(re.finditer(r"\bclass\s+(\w+)", text))
        for i, head in enumerate(heads):
            body = text[head.end() : heads[i + 1].start() if i + 1 < len(heads) else len(text)]
            for name, value in CONST.findall(body):
                out[f"Contract.{head.group(1)}.{name}"] = value
    return out


def classes():
    found = {}
    shared = shared_consts()
    for path in sorted(FIXES.glob("*.cs")):
        text = path.read_text(encoding="utf-8")
        consts = {**shared, **dict(CONST.findall(text))}
        heads = list(CLASS.finditer(text))
        for i, head in enumerate(heads):
            body = text[head.end() : heads[i + 1].start() if i + 1 < len(heads) else len(text)]
            values = {}
            for name, expression in PROP.findall(body):
                expression = expression.strip()
                literal = LITERAL.match(expression)
                if literal:
                    values[name] = literal.group(1)
                elif expression in consts:
                    values[name] = consts[expression]
                else:
                    fail(f"{path.name}: {head.group(1)}.{name} is '{expression}', which is not a string literal")
            found[head.group(1)] = (path.name, values)
    return found


def main():
    known = classes()
    out, general = [], 0
    for name in registered():
        if name not in known:
            fail(f"{name} is registered in Fixes.cs and no class of that name was found")
        file, values = known[name]
        missing = [key for key in ("Id", "Mod", "ModVersions", "GameVersions") if key not in values]
        if missing:
            fail(f"{file}: {name} has no literal {', '.join(missing)}")
        if values["Mod"] == "*":
            general += 1
            continue
        out.append(
            {
                "id": values["Id"],
                "mod": values["Mod"],
                "modVersions": values["ModVersions"],
                "gameVersions": values["GameVersions"],
            }
        )

    out.sort(key=lambda fix: (fix["mod"].lower(), fix["id"]))
    print(json.dumps({"fixes": out}, indent="\t", ensure_ascii=False))
    print(f"export_modfixes: {len(out)} per-mod fix(es) exported, {general} general one(s) left out", file=sys.stderr)


if __name__ == "__main__":
    main()
