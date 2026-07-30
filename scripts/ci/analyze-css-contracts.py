#!/usr/bin/env python3
"""Fail when the premium CSS ownership contract is violated."""
from __future__ import annotations
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSS_ROOT = ROOT / "InovaGed.Web/wwwroot/css"
VIEWS = ROOT / "InovaGed.Web/Views"
CANONICAL_SHELL = CSS_ROOT / "inovaged.shell.css"
CANONICAL_TOKENS = CSS_ROOT / "inovaged.tokens.css"
STRUCTURAL = (".app-shell", ".app-main", ".app-sidebar", ".app-topbar")
TOKEN_RE = re.compile(r"(--ig-[\w-]+)\s*:")
HEX_RE = re.compile(r"#[0-9a-fA-F]{6}\b")
ALLOWED_BRAND = {"#1d4ed8", "#2563eb", "#3b82f6", "#dbeafe", "#eff6ff", "#16a34a", "#22c55e", "#dcfce7", "#f0fdf4"}
errors: list[str] = []
owners: dict[str, list[Path]] = defaultdict(list)
tokens: dict[str, list[Path]] = defaultdict(list)

ACTIVE = [CSS_ROOT / name for name in ("inovaged.tokens.css", "inovaged.base.css", "inovaged.shell.css", "inovaged.components.css", "inovaged.utilities.css")] + sorted((CSS_ROOT / "pages").glob("*.css"))
for path in ACTIVE:
    text = path.read_text(encoding="utf-8")
    for selector in STRUCTURAL:
        if re.search(re.escape(selector) + r"(?![\w-])", text):
            owners[selector].append(path)
    for token in TOKEN_RE.findall(text):
        tokens[token].append(path)
    if "/pages/" in path.as_posix() and re.search(r"(^|[},])\s*(body|\.app-shell|\.app-sidebar|\.sidebar|\.app-topbar|\.topbar)\b", text, re.M):
        errors.append(f"page stylesheet owns global shell selector: {path.relative_to(ROOT)}")
    count = text.count("!important")
    if count > 2:
        errors.append(f"excessive !important ({count}): {path.relative_to(ROOT)}")

for selector, paths in owners.items():
    if paths != [CANONICAL_SHELL]:
        errors.append(f"{selector} must be owned only by {CANONICAL_SHELL.relative_to(ROOT)}; found: {', '.join(str(p.relative_to(ROOT)) for p in paths)}")
for token, paths in tokens.items():
    if paths != [CANONICAL_TOKENS]:
        errors.append(f"token {token} must have one owner; found: {', '.join(str(p.relative_to(ROOT)) for p in paths)}")

for view in [VIEWS / "Shared/_Layout.cshtml", VIEWS / "Shared/_LayoutAuth.cshtml"]:
    text = view.read_text(encoding="utf-8")
    if re.search(r'style\s*=\s*["\'][^"\']*(display|position|width|height|flex|grid|margin|padding)\s*:', text, re.I):
        errors.append(f"inline structural style: {view.relative_to(ROOT)}")

# Brand colors belong to the token catalog; semantic/page colors are audited separately.
for path in sorted(CSS_ROOT.glob("inovaged.*.css")):
    if path == CANONICAL_TOKENS:
        continue
    text = path.read_text(encoding="utf-8").lower()
    for color in sorted(ALLOWED_BRAND & set(HEX_RE.findall(text))):
        errors.append(f"brand color {color} must use a token: {path.relative_to(ROOT)}")

if errors:
    print("CSS contract violations:", *[f"\n- {error}" for error in errors])
    sys.exit(1)
print(f"CSS contracts valid: {len(ACTIVE)} stylesheets audited")
