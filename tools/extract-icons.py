# -*- coding: utf-8 -*-
"""
Извлекает иконки из референсов и генерирует XAML-словарь геометрий для WPF
(src/MusicScanIntegrity.App/Resources/Icons.xaml).

Источников два:

* `Icons.dc.html` — набор иконок целиком. Все они контурные (stroke),
  viewBox 0 0 20 20. В WPF удобнее хранить одну строку Path.Data на иконку,
  поэтому <circle> и <rect> переводятся в эквивалентные дуговые сегменты,
  а <path d="..."> берётся как есть: мини-язык путей WPF совместим с SVG.
* `MusicScan.dc.html` — макет самой программы. В нём статусы внутри
  цветного квадрата 20×20 и внутри плашек-фильтров нарисованы БЕЗ кружка:
  на 13–14 px кружок только замыливает знак. Эти четыре «голых» знака
  выносятся отдельными иконками `mark-*`, чтобы не рисовать их руками.

Запуск:  python tools/extract-icons.py
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
    # Две полудуги — замкнутая окружность одной фигурой.
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
    """Возвращает отсчёт к началу координат перед относительным путём.

    В SVG каждый <path> отсчитывается от (0,0), но в Path.Data все пути идут
    одной строкой, и относительная команда «m» считалась бы от конца предыдущего
    пути — части иконки уезжали бы в сторону. Явный «M0,0» ставит текущую точку
    в начало координат; пустая фигура при этом ничего не рисует.
    """
    return "M0,0 " + d if d[:1].islower() else d


# Карточка иконки в референсе: <svg>...</svg>, следом имя и подпись.
# Имя лежит либо сразу после svg (сетка в 6 колонок), либо внутри соседней
# <div>-обёртки (блок статусов) — отсюда необязательная группа обёртки.
# Группа inner намеренно не пускает внутрь себя вложенные <svg>: иначе
# нежадный поиск склеивает несколько соседних иконок в одну.
CARD_RE = re.compile(
    r"<svg\b(?P<svgattrs>[^>]*)>(?P<inner>(?:(?!</?svg\b).)*?)</svg>"
    r"(?:\s*<div[^>]*>)?\s*"
    r'<div style="font-size:12(?:\.5)?px;font-weight:650[^"]*">(?P<name>[a-z0-9\-]+)</div>\s*'
    r'<div style="font-size:11px[^"]*">(?P<title>[^<]*)</div>',
    re.DOTALL)


# Голые знаки статуса из макета программы: `const D = { ok: "...", ... }`.
MARKS_RE = re.compile(r"const D = \{(?P<body>.*?)\};", re.DOTALL)
MARK_RE = re.compile(r'(?P<key>[a-z]+):\s*"(?P<d>[^"]+)"')
MARK_NAMES = {"ok": "mark-ok", "err": "mark-broken",
              "warn": "mark-warning", "skip": "mark-skipped"}
MARK_TITLES = {"ok": "В порядке, знак без кружка",
               "err": "Повреждён, знак без кружка",
               "warn": "Предупреждение, знак без кружка",
               "skip": "Пропущен, знак без кружка"}


def read_marks() -> dict[str, tuple[str, str, bool]]:
    """Четыре знака статуса, какими их рисует макет программы."""
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
        '    Геометрии иконок. Файл СГЕНЕРИРОВАН из "Референсы и дизайн" (Icons.dc.html',
        '    и знаки статуса mark-* из MusicScan.dc.html) скриптом tools/extract-icons.py —',
        '    руками не править, иконки не рисовать',
        '    "по мотивам" (UI_SPEC.md, раздел 10). Пересоздать: python tools/extract-icons.py',
        '-->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
        '',
    ]
    for name in sorted(icons):
        geometry, title, filled = icons[name]
        kind = "заливка" if filled else "контур"
        lines.append(f'    <!-- {name} — {title} ({kind}) -->')
        lines.append(f'    <sys:String x:Key="Icon.{name}">{html.escape(geometry)}</sys:String>')
    lines.append('')
    filled_names = sorted(n for n, (_, _, f) in icons.items() if f)
    lines.append('    <!-- Иконки, которые рисуются заливкой, а не обводкой (UI_SPEC.md, раздел 10). -->')
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
