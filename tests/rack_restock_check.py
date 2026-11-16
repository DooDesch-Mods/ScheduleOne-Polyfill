"""Empty one storage-rack slot through Polyfill's debug command and check that a restock mod refilled it.

Checks (PASS/FAIL each):
  emptied - the slot holds nothing right after the empty command
  restock - the slot holds the same item again 4 s later
Needs a Debug Polyfill (polyfillrack) and AutoRestock.
Usage: python -I tests/rack_restock_check.py --slot mptest [--item pseudo] [--qty 5] [--rack largestoragerack]
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


def lines_of(answer):
    raw = (answer.get("result") or {}).get("lines") or []
    return [line.get("text", "") if isinstance(line, dict) else str(line) for line in raw]


def size(path):
    return os.path.getsize(path) if path and os.path.exists(path) else 0


def read_from(path, offset):
    with open(path, "rb") as handle:
        handle.seek(offset)
        return handle.read().decode("utf-8", errors="replace").splitlines()


def wait_for(path, offset, text, timeout):
    """The first log line after offset that contains text, or None after timeout seconds."""
    deadline = time.monotonic() + timeout
    while True:
        for line in read_from(path, offset):
            if text in line:
                return line
        if time.monotonic() >= deadline:
            return None
        time.sleep(2)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", required=True)
    p.add_argument("--item", default="pseudo")
    p.add_argument("--qty", type=int, default=5)
    p.add_argument("--rack", default="largestoragerack", help="text the rack's id contains")
    a = p.parse_args()

    g = s1.Game(a.slot)
    ok, _ = g.wait_ready(timeout=300)
    if not ok:
        print("FAIL setup: the save never came up")
        return 1
    log = g.log_path()
    started = wait_for(log, 0, "AutoRestock manager initialized", 60)
    if not started:
        print("FAIL setup: AutoRestock never logged 'AutoRestock manager initialized'")
        return 1
    print("  start| " + started[:200])

    for line in lines_of(g.run("polyfillrack show")):
        print("  show| " + line[:260])
    for line in lines_of(g.run("polyfillrack fill %s %d %s" % (a.item, a.qty, a.rack))):
        print("  fill| " + line[:260])

    shown = lines_of(g.run("polyfillrack show"))
    number = None
    for line in shown:
        m = re.search(r"polyfillrack: (\d+)\. %s.* slot \d+: %s x" % (re.escape(a.rack), re.escape(a.item)), line, re.I)
        if m:
            number = int(m.group(1))
    if number is None:
        print("FAIL setup: no %s slot holds %s after the fill" % (a.rack, a.item))
        return 1

    mark = size(log)
    out = lines_of(g.run("polyfillrack empty %d" % number, until="4 s later", max_wait_ms=12000))
    for line in out:
        if "polyfillrack" in line or "AutoRestock" in line or "Exception" in line:
            print("  empty| " + line[:300])

    failed = 0
    emptied = wait_for(log, mark, "polyfillrack: emptied, now", 5)
    if emptied and re.search(r": nothing x0", emptied):
        print("PASS emptied: " + emptied[:220])
    else:
        failed += 1
        print("FAIL emptied: " + (emptied[:220] if emptied else "no 'emptied' line"))

    after = wait_for(log, mark, "4 s later", 10)
    if after and re.search(r": %s x[1-9]" % re.escape(a.item), after):
        print("PASS restock: " + after[:220])
    else:
        failed += 1
        print("FAIL restock: " + (after[:220] if after else "no recheck line"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
