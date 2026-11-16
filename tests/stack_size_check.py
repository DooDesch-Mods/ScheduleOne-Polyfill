"""Check a stack-size mod in a running game: give an item with the game's own command, then read the slots.

Checks (PASS/FAIL each):
  limit - the item's stack limit is --limit
  slot  - one inventory slot holds more than --vanilla of it, which the game's give command only does when
          the larger limit is applied (it starts a new slot when one is full)
  racks - with --racks: every placed drying rack has capacity --limit (the game's own is 20)
Needs a Debug Polyfill (command polyfillstack) and a loaded save.

    python -I tests/stack_size_check.py --slot mptest --item pseudo --give 30 --limit 40 --vanilla 20 --racks
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.environ.get("S1_BRIDGE", r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge"))
import s1  # noqa: E402

# Mod log lines carry emoji; the Windows console code page cannot print them.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def texts(reply):
    raw = (reply.get("result") or {}).get("lines") or []
    return [line.get("text", "") if isinstance(line, dict) else str(line) for line in raw]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", required=True)
    p.add_argument("--item", default="cuke")
    p.add_argument("--give", type=int, default=30)
    p.add_argument("--limit", type=int, required=True, help="the stack size the mod is set to")
    p.add_argument("--vanilla", type=int, default=10, help="the game's own limit for the item")
    p.add_argument("--racks", action="store_true", help="also require every drying rack at --limit")
    p.add_argument("--applied", default="Applied custom stack size", help="the mod's log line once it applied")
    p.add_argument("--timeout", type=int, default=60)
    a = p.parse_args()

    g = s1.Game(a.slot)
    ok, _ = g.wait_ready(timeout=300)
    if not ok:
        print("FAIL setup: the save never came up")
        return 1
    deadline = time.monotonic() + a.timeout
    applied = g.log(grep=a.applied, lines=1)
    while not applied and time.monotonic() < deadline:
        time.sleep(2)
        applied = g.log(grep=a.applied, lines=1)
    print("  mod: " + (applied[-1][:200] if applied else "no %r line within %d s" % (a.applied, a.timeout)))

    for line in texts(g.run("give %s %d" % (a.item, a.give))):
        print("  give: " + line[:200])
    lines = texts(g.run("polyfillstack " + a.item))
    for line in lines:
        if "polyfillstack" in line:
            print("  " + line[:200])

    limit = None
    biggest = 0
    racks = {}
    for line in lines:
        m = re.search(r"polyfillstack: \S+ stack limit (\d+)", line)
        if m:
            limit = int(m.group(1))
        m = re.search(r"polyfillstack: inventory slot \d+ holds \S+ x(\d+)", line)
        if m:
            biggest = max(biggest, int(m.group(1)))
        m = re.search(r"polyfillstack: (\d+) drying rack\(s\) with capacity (\d+)", line)
        if m:
            racks[int(m.group(2))] = int(m.group(1))

    failed = 0
    if limit == a.limit:
        print("PASS limit: %s stack limit %d" % (a.item, limit))
    else:
        print("FAIL limit: %s stack limit %s, expected %d" % (a.item, limit, a.limit))
        failed += 1
    if biggest > a.vanilla:
        print("PASS slot: one slot holds %d %s, more than the game's %d" % (biggest, a.item, a.vanilla))
    else:
        print("FAIL slot: the largest slot holds %d %s, not more than the game's %d" % (biggest, a.item, a.vanilla))
        failed += 1
    if a.racks:
        if racks and set(racks) == {a.limit}:
            print("PASS racks: %d drying rack(s), all with capacity %d" % (racks[a.limit], a.limit))
        else:
            print("FAIL racks: capacities %s, expected all %d" % (racks or "none found", a.limit))
            failed += 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
