#!/usr/bin/env python3
"""Reject a browser run that did not produce independently verifiable evidence."""
import argparse, json, pathlib, sys

parser = argparse.ArgumentParser()
parser.add_argument("root", nargs="?", default="InovaGed.UiTests")
args = parser.parse_args()
root = pathlib.Path(args.root)
manifest = root / "Reports" / "executions.jsonl"
failures = []
records = []
if manifest.exists():
    for number, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), 1):
        try: records.append(json.loads(line))
        except json.JSONDecodeError as error: failures.append(f"manifest line {number}: {error}")
else: failures.append("execution manifest is missing (application/browser did not start)")

screenshots = [p for p in (root / "Screenshots" / "actual").glob("*.png") if p.stat().st_size > 0]
comparisons = [r for r in records if r.get("comparison") is True]
checks = {
    "tests": (len(records), 20), "screenshots": (len(screenshots), 30),
    "comparisons": (len(comparisons), 20),
    "pages": (len({r.get('page') for r in records}), 8),
    "viewports": (len({r.get('viewport') for r in records}), 5),
    "profiles": (len({r.get('profile') for r in records}), 3),
}
for label, (actual, minimum) in checks.items():
    if actual < minimum: failures.append(f"{label}: expected at least {minimum}, got {actual}")
if not (root / "Reports" / "login.occurred").is_file(): failures.append("real login marker is missing")
missing = [r.get("screenshot") for r in records if not pathlib.Path(root, r.get("screenshot", "")).is_file()]
if missing: failures.append(f"manifest references {len(missing)} missing screenshots")
print(json.dumps({k: v[0] for k, v in checks.items()}, indent=2, sort_keys=True))
if failures:
    print("UI evidence rejected:\n- " + "\n- ".join(failures), file=sys.stderr)
    sys.exit(1)
