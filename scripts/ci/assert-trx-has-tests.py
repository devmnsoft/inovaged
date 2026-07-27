#!/usr/bin/env python3
"""Fail a CI test step when its TRX is missing, empty, or reports failures."""

from __future__ import annotations

import argparse
import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def counters(path: Path) -> dict[str, int]:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        raise ValueError(f"cannot read TRX {path}: {error}") from error

    element = root.find(".//{*}Counters")
    if element is None:
        raise ValueError(f"TRX {path} has no Counters element")

    def number(name: str) -> int:
        value = element.get(name)
        if value is None:
            raise ValueError(f"TRX {path} Counters has no {name!r} attribute")
        try:
            return int(value)
        except ValueError as error:
            raise ValueError(f"TRX {path} has invalid {name}={value!r}") from error

    return {
        "total": number("total"),
        "executed": number("executed"),
        "passed": number("passed"),
        "failed": number("failed"),
        "skipped": number("notExecuted"),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("patterns", nargs="+", help="TRX paths or glob patterns")
    arguments = parser.parse_args()
    paths = sorted({Path(item) for pattern in arguments.patterns for item in glob.glob(pattern, recursive=True)})
    if not paths:
        print("error: no TRX matched: " + ", ".join(arguments.patterns), file=sys.stderr)
        return 2

    aggregate = {key: 0 for key in ("total", "executed", "passed", "failed", "skipped")}
    try:
        for path in paths:
            values = counters(path)
            print(f"{path}: " + " ".join(f"{key}={value}" for key, value in values.items()))
            for key, value in values.items():
                aggregate[key] += value
    except ValueError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    print("aggregate: " + " ".join(f"{key}={value}" for key, value in aggregate.items()))
    if aggregate["total"] <= 0 or aggregate["executed"] <= 0:
        print("error: the test selection executed zero tests", file=sys.stderr)
        return 1
    if aggregate["failed"] > 0:
        print("error: the TRX reports failed tests", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
