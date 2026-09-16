# -*- coding: utf-8 -*-
"""
Extracts icons from the design references and generates the WPF geometry dictionary
(src/MusicScanIntegrity.App/Resources/Icons.xaml).

Two sources:

* `Icons.dc.html` — the full icon set. All stroked, viewBox 0 0 20 20. WPF
  prefers one Path.Data string per icon, so <circle> and <rect> become
  equivalent arc segments and <path d="..."> is taken as is: WPF path markup
  is compatible with SVG.
* `MusicScan.dc.html` — the application mock-up. Statuses inside the 20×20
  coloured square and the filter chips are drawn WITHOUT the ring, which only
  blurs the glyph at 13–14 px. These four bare glyphs are emitted as separate
  `mark-*` icons so they are never hand-drawn.

The sources are working design files and are not committed. Icons.xaml is
already generated; this script is only needed when the design changes — put
the sources into `Референсы и дизайн/` at the repository root.

Usage:  python tools/extract-icons.py
"""
from __future__ import annotations

import html
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REFERENCES = ROOT / "Референсы и дизайн"
SOURCE = REFERENCES / "Icons.dc.html"
LAYOUT = REFERENCES / "MusicScan.dc.html"
TARGET = ROOT / "src" / "MusicScanIntegrity.App" / "Resources" / "Icons.xaml"


def num(value: float) -> str:
    text = f"{value:.4f}".rstrip("0").rstrip(".")
    return text if text not in ("", "-0") else "0"


def circle_to_path(cx: float, cy: float, r: float) -> str:
    # Two half arcs make a closed circle in a single figure.
    return (f"M{num(cx - r)},{num(cy)}"
            f"A{num(r)},{num(r)} 0 0 1 {num(cx + r)},{num(cy)}"
            f"A{num(r)},{num(r)} 0 0 1 {num(cx - r)},{num(cy)}Z")


def rect_to_path(x: float, y: float, w: float, h: float, rx: float) -> str:
    rx = min(rx, w / 2, h / 2)
    if rx <= 0:
        return f"M{num(x)},{num(y)}H{num(x + w)}V{num(y + h)}H{num(x)}Z"
    return (f"M{num(x + rx)},{num(y)}"
            f"H{num(x + w - rx)}"
            f"A{num(rx)},{num(rx)} 0 0 1 {num(x + w)},{num(y + rx)}"
            f"V{num(y + h - rx)}"
            f"A{num(rx)},{num(rx)} 0 0 1 {num(x + w - rx)},{num(y + h)}"
            f"H{num(x + rx)}"
            f"A{num(rx)},{num(rx)} 0 0 1 {num(x)},{num(y + h - rx)}"
            f"V{num(y + rx)}"
            f"A{num(rx)},{num(rx)} 0 0 1 {num(x + rx)},{num(y)}Z")


def attr(tag: str, name: str, default: str = "0") -> str:
    match = re.search(rf'\b{name}="([^"]*)"', tag)
    return match.group(1) if match else default


def svg_to_geometry(svg_inner: str) -> str:
    parts: list[str] = []
    for tag in re.findall(r"<(?:path|circle|rect)\b[^>]*>", svg_inner):
        if tag.startswith("<path"):
            parts.append(reset_origin(attr(tag, "d", "").strip()))
        elif tag.startswith("<circle"):
            parts.append(circle_to_path(
                float(attr(tag, "cx")), float(attr(tag, "cy")), float(attr(tag, "r"))))
        else:
            parts.append(rect_to_path(
                float(attr(tag, "x")), float(attr(tag, "y")),
                float(attr(tag, "width")), float(attr(tag, "height")),
                float(attr(tag, "rx"))))
    return " ".join(p for p in parts if p)


def reset_origin(d: str) -> str:
    """Resets the current point to the origin before a relative path.

    In SVG every <path> starts from (0,0), but Path.Data joins all paths into one
    string, so a relative "m" would continue from the previous path's end and
    shift parts of the icon. An explicit "M0,0" resets the current point; the
    empty figure draws nothing.
    """
    return "M0,0 " + d if d[:1].islower() else d


# An icon card in the reference: <svg>...</svg> followed by name and caption.
# The name follows the svg directly (six-column grid) or sits inside a wrapper
# <div> (status block), hence the optional wrapper group. The inner group
# refuses nested <svg> on purpose; otherwise the lazy match merges adjacent
# icons into one.
CARD_RE = re.compile(
    r"<svg\b(?P<svgattrs>[^>]*)>(?P<inner>(?:(?!</?svg\b).)*?)</svg>"
    r"(?:\s*<div[^>]*>)?\s*"
    r'<div style="font-size:12(?:\.5)?px;font-weight:650[^"]*">(?P<name>[a-z0-9\-]+)</div>\s*'
    r'<div style="font-size:11px[^"]*">(?P<title>[^<]*)</div>',
    re.DOTALL)


# Bare status glyphs from the mock-up: `const D = { ok: "...", ... }`.
MARKS_RE = re.compile(r"const D = \{(?P<body>.*?)\};", re.DOTALL)
MARK_RE = re.compile(r'(?P<key>[a-z]+):\s*"(?P<d>[^"]+)"')
MARK_NAMES = {"ok": "mark-ok", "err": "mark-broken",
              "warn": "mark-warning", "skip": "mark-skipped"}
MARK_TITLES = {"ok": "В порядке, знак без кружка",
               "err": "Повреждён, знак без кружка",
               "warn": "Предупреждение, знак без кружка",
               "skip": "Пропущен, знак без кружка"}


def read_marks() -> dict[str, tuple[str, str, bool]]:
    """The four status glyphs as drawn in the application mock-up."""
    if not LAYOUT.exists():
        print(f"Не найден макет: {LAYOUT}", file=sys.stderr)
        return {}

    block = MARKS_RE.search(LAYOUT.read_text(encoding="utf-8"))
    if block is None:
        print("В макете не найден набор знаков статуса.", file=sys.stderr)
        return {}

    found: dict[str, tuple[str, str, bool]] = {}
    for match in MARK_RE.finditer(block.group("body")):
        key = match.group("key")
        if key in MARK_NAMES:
            found[MARK_NAMES[key]] = (
                reset_origin(match.group("d").strip()), MARK_TITLES[key], False)
    return found


def main() -> int:
    if not SOURCE.exists():
        print(f"Не найден референс: {SOURCE}", file=sys.stderr)
        return 1

    source = SOURCE.read_text(encoding="utf-8")
    icons: dict[str, tuple[str, str, bool]] = {}
    for match in CARD_RE.finditer(source):
        name = match.group("name")
        geometry = svg_to_geometry(match.group("inner"))
        if not geometry:
            continue
        filled = 'fill="var(--fg)"' in match.group("svgattrs") or 'fill="none"' not in match.group("svgattrs")
        icons[name] = (geometry, html.unescape(match.group("title")).strip(), filled)

    if not icons:
        print("Иконки не найдены — изменился формат референса.", file=sys.stderr)
        return 1

    icons.update(read_marks())

    lines = [
        '<!--',
        '    Icon geometry. GENERATED by tools/extract-icons.py from the design references',
        '    (Icons.dc.html, plus the mark-* status glyphs from MusicScan.dc.html).',
        '    Do not edit by hand; regenerate with: python tools/extract-icons.py',
        '-->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
        '',
    ]
    for name in sorted(icons):
        geometry, _, _ = icons[name]
        lines.append(f'    <sys:String x:Key="Icon.{name}">{html.escape(geometry)}</sys:String>')
    lines.append('')
    filled_names = sorted(n for n, (_, _, f) in icons.items() if f)
    lines.append('    <!-- Icons drawn filled rather than stroked. -->')
    lines.append(f'    <sys:String x:Key="Icon.FilledKinds">{",".join(filled_names)}</sys:String>')
    lines.append('')
    lines.append('</ResourceDictionary>')

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Извлечено иконок: {len(icons)} -> {TARGET.relative_to(ROOT)}")
    print("  заливкой:", ", ".join(filled_names) or "нет")
    print("  список:", ", ".join(sorted(icons)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
