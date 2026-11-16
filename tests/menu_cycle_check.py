"""Exit a loaded save to the main menu, load it again, and report the log lines written on the way.

Checks (PASS/FAIL each):
  scene  - the game reaches the Menu scene after Exit to Menu
  fix    - every --expect-menu pattern appears in the menu phase (the repair acted there)
  menu   - after Exit to Menu, no line matches any --forbid pattern
  reload - after loading the save again, every --expect pattern appears in the new lines

Usage: python -I tests/menu_cycle_check.py --slot mptest --save 5 --forbid "Manager.Initialize" \
           --expect-menu "[fix] autorestock-menu-initialize" --expect "AutoRestock manager initialized"
"""
import argparse
import os
import sys
import time

BRIDGE = os.environ.get("S1_BRIDGE", r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge")
sys.path.insert(0, BRIDGE)
import s1  # noqa: E402

# Mod log lines carry emoji; the Windows console code page cannot print them.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def size(path):
    return os.path.getsize(path) if path and os.path.exists(path) else 0


def read_from(path, offset):
    with open(path, "rb") as handle:
        handle.seek(offset)
        return handle.read().decode("utf-8", errors="replace").splitlines()


def all_seen(path, offset, patterns):
    lines = read_from(path, offset)
    return all(any(p in line for line in lines) for p in patterns)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", required=True)
    p.add_argument("--save", type=int, default=5)
    p.add_argument("--forbid", action="append", default=None)
    p.add_argument("--expect-menu", action="append", default=None)
    p.add_argument("--expect", action="append", default=None)
    p.add_argument("--menu-timeout", type=int, default=60)
    p.add_argument("--menu-quiet", type=int, default=10, help="seconds to keep reading after the menu is up")
    p.add_argument("--reload-timeout", type=int, default=90)
    a = p.parse_args()
    forbid = a.forbid or ["Manager.Initialize"]
    expect_menu = a.expect_menu or ["[fix] autorestock-menu-initialize: AutoRestock waits for a save"]
    expect = a.expect or ["AutoRestock manager initialized"]

    g = s1.Game(a.slot)
    ok, _ = g.wait_ready(timeout=300)
    if not ok:
        print("FAIL setup: the save never came up")
        return 1
    log = g.log_path()
    mark = size(log)
    failed = 0
    left = g.call("return_to_menu").get("ok")
    print("[cycle] info: return_to_menu: %s" % left)
    if not left:
        print("FAIL scene: return_to_menu was refused")
        return 1

    # Wait for the Menu scene, then for the expected lines or the quiet period, whichever ends later.
    deadline = time.monotonic() + a.menu_timeout
    scene = None
    menu_at = None
    while time.monotonic() < deadline:
        try:
            scene = g.health().get("scene")
        except s1.BridgeDown as e:
            print("[cycle] warn: health during menu wait: %s" % e)
        if scene == "Menu" and menu_at is None:
            menu_at = time.monotonic()
        if menu_at is not None and time.monotonic() - menu_at >= a.menu_quiet and all_seen(log, mark, expect_menu):
            break
        time.sleep(2)
    if scene == "Menu":
        print("PASS scene: Menu")
    else:
        failed += 1
        print("FAIL scene: %s after %d s, expected Menu" % (scene, a.menu_timeout))

    menu_lines = read_from(log, mark)
    print("[cycle] info: %d log lines in the menu phase" % len(menu_lines))
    for line in menu_lines:
        if "AutoRestock" in line or "Exception" in line or "[fix]" in line:
            print("  menu| " + line[:300])
    for want in expect_menu:
        if any(want in line for line in menu_lines):
            print("PASS fix: %r appeared in the menu" % want)
        else:
            failed += 1
            print("FAIL fix: %r missing in the menu" % want)
    hits = [line for line in menu_lines if any(f in line for f in forbid)]
    if hits:
        failed += 1
        print("FAIL menu: %d line(s) match %s" % (len(hits), forbid))
    else:
        print("PASS menu: no line matches %s" % forbid)

    mark = size(log)
    try:
        g.call("load_save_by_slot", {"slot": a.save}, timeout_ms=60000)
    except TimeoutError as e:
        print("[cycle] info: load still running: %s" % e)
    ok, _ = g.wait_ready(timeout=300)
    if not ok:
        print("FAIL reload: the save did not come up again")
        return 1
    deadline = time.monotonic() + a.reload_timeout
    while time.monotonic() < deadline and not all_seen(log, mark, expect):
        time.sleep(2)
    lines = read_from(log, mark)
    for line in lines:
        if "AutoRestock" in line or "Exception" in line:
            print("  load| " + line[:300])
    for want in expect:
        if any(want in line for line in lines):
            print("PASS reload: %r appeared" % want)
        else:
            failed += 1
            print("FAIL reload: %r missing" % want)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
