#!/usr/bin/env python3
"""Runs the Core tests with code coverage of the permission engine (SCalenderPlus.Core.Permissions) and fails
unless every branch and line of it is covered (docs/architecture/permissions.md §8, docs/development/workflow.md).

Usage: python3 backend/scripts/engine-coverage.py [--no-build] [--configuration Release]
"""
import argparse
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests" / "SCalenderPlus.Core.Tests"
SETTINGS = PROJECT / "permission-engine.coverage.xml"
REPORT = "permission-engine.cobertura.xml"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--configuration", "-c", default="Release")
    args = parser.parse_args()

    output_dir = PROJECT / "bin" / args.configuration / "net10.0" / "TestResults"
    report = output_dir / REPORT
    report.unlink(missing_ok=True)

    command = ["dotnet", "test", "--project", str(PROJECT), "--configuration", args.configuration]
    if args.no_build:
        command.append("--no-build")
    command += ["--results-directory", str(output_dir), "--coverage", "--coverage-output-format", "cobertura", "--coverage-output", REPORT,
                "--coverage-settings", str(SETTINGS)]
    result = subprocess.run(command, check=False, cwd=ROOT.parent)  # global.json there selects Microsoft.Testing.Platform
    if result.returncode != 0:
        return result.returncode
    if not report.exists():
        print(f"::error::coverage report {report} was not written")
        return 1

    root = ET.parse(report).getroot()
    lines_valid, branches_valid = int(root.get("lines-valid")), int(root.get("branches-valid"))
    lines_covered, branches_covered = int(root.get("lines-covered")), int(root.get("branches-covered"))
    print(f"Permission engine coverage: lines {lines_covered}/{lines_valid}, branches {branches_covered}/{branches_valid}")

    if lines_valid == 0 or branches_valid == 0:
        print("::error::no permission engine code was measured; check permission-engine.coverage.xml")
        return 1

    gaps = []
    for cls in root.iter("class"):
        for line in cls.iter("line"):
            condition = line.get("condition-coverage") or "100%"
            if line.get("hits") == "0" or not condition.startswith("100%"):
                gaps.append(f"{cls.get('filename')}:{line.get('number')} hits={line.get('hits')} branches={condition}")
    if gaps:
        print("::error::the permission engine must have 100 % line and branch coverage; uncovered:")
        print("\n".join(sorted(set(gaps))))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
