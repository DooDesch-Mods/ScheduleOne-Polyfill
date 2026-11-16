"""Open the Dealers app like a player and check AdvancedDealing's dead-drop pickers.

Checks (PASS/FAIL each):
  fix      - the advanceddealing-selector-title line is in the log (skipped with --without-fix)
  build    - AdvancedDealing logged "UI elements created" and no DeadDropSelector.BuildUI exception
  app      - the phone opens and the Dealers app takes the click
  cash     - the "Cash Dead Drop" row is on the dealer page
  product  - the "Product Dead Drop" row is on the dealer page
  picker   - clicking the cash row opens the dead-drop list

--without-fix flips build/cash/product/picker to expect the broken state, for the evidence pair.

Usage: python -I tests/advanced_dealing_check.py --slot mptest [--without-fix]
"""
import argparse
import os
import sys
import time

BRIDGE = os.environ.get("S1_BRIDGE", r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge")
sys.path.insert(0, BRIDGE)
import s1  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

FIX_LINE = "[fix] advanceddealing-selector-title: the dead-drop pickers look up"
BUILT_LINE = "[AdvancedDealing] UI elements created"
BROKEN_FRAME = "AdvancedDealing.UI.DeadDropSelector.BuildUI"
CASH = "Advanced Dealing - Cash Dead Drop"
PRODUCT = "Advanced Dealing - Product Dead Drop"


def find(g, query):
    res = g.call("ui_find", {"query": query})
    if not res.get("ok"):
        print("[ad] warn: ui_find %r failed: %s" % (query, res.get("error")))
        return []
    return res.get("result", {}).get("elements", [])


def click(g, query, index=0):
    res = g.call("ui_click", {"query": query, "index": index})
    if not res.get("ok"):
        print("[ad] warn: ui_click %r failed: %s" % (query, res.get("error")))
        return False
    r = res.get("result", {})
    print("[ad] info: ui_click %r -> clicked=%s hit=%s" % (query, r.get("clicked"), r.get("hit")))
    return bool(r.get("clicked"))


def shot(g, name):
    path = g.shot()
    print("[ad] info: screenshot %s: %s" % (name, path))
    return path


def report(failed, name, ok, detail):
    print("%s %s: %s" % ("PASS" if ok else "FAIL", name, detail))
    return failed + (0 if ok else 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", required=True)
    p.add_argument("--without-fix", action="store_true")
    p.add_argument("--app", default="Dealers", help="label of the phone app icon")
    a = p.parse_args()
    fixed = not a.without_fix

    g = s1.Game(a.slot)
    ok, _ = g.wait_ready(timeout=300)
    if not ok:
        print("FAIL setup: the save never came up")
        return 1
    # AdvancedDealing builds its UI in a coroutine after the load; give it a moment.
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline and not g.log(grep=BUILT_LINE, lines=1) and not g.log(grep=BROKEN_FRAME, lines=1):
        time.sleep(2)

    failed = 0
    if fixed:
        lines = g.log(grep=FIX_LINE, lines=1)
        failed = report(failed, "fix", bool(lines), lines[0].strip() if lines else "no fix line")
    built = bool(g.log(grep=BUILT_LINE, lines=1))
    broken = g.log(grep=BROKEN_FRAME, lines=3)
    for line in broken:
        print("  log| " + line[:300])
    if fixed:
        failed = report(failed, "build", built and not broken,
                        "UI built=%s, BuildUI exceptions=%d" % (built, len(broken)))
    else:
        failed = report(failed, "build", bool(broken) and not built,
                        "expected the BuildUI exception: built=%s, exceptions=%d" % (built, len(broken)))

    res = g.call("phone", {"open": True})
    if not res.get("ok"):
        print("FAIL app: the phone did not open: %s" % res.get("error"))
        return 1
    time.sleep(1.5)
    opened = click(g, a.app)
    if not opened:
        labels = sorted({(e.get("label") or "") for e in find(g, "")})
        print("[ad] info: clickable labels on screen: %s" % ", ".join(x for x in labels if x)[:600])
    failed = report(failed, "app", opened, "clicked %r" % a.app)
    time.sleep(1.5)

    cash = find(g, CASH)
    product = find(g, PRODUCT)
    for e in cash + product:
        print("[ad] info: row %s label=%r" % (e.get("path"), e.get("label")))
    shot(g, "dealer page")
    if fixed:
        failed = report(failed, "cash", len(cash) >= 1, "%d row(s)" % len(cash))
        failed = report(failed, "product", len(product) >= 1, "%d row(s)" % len(product))
    else:
        failed = report(failed, "cash", not cash, "expected no row, found %d" % len(cash))
        failed = report(failed, "product", not product, "expected no row, found %d" % len(product))

    if cash:
        click(g, CASH)
        time.sleep(1.5)
        entries = find(g, "CashDeadDropSelector")
        for e in entries[:6]:
            print("[ad] info: entry label=%r" % e.get("label"))
        shot(g, "cash picker")
        failed = report(failed, "picker", len(entries) >= 2,
                        "%d entries in the cash dead-drop list (None plus the dead drops)" % len(entries))
    elif fixed:
        failed = report(failed, "picker", False, "no cash row to click")

    g.call("phone", {"open": False})
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
