"""Build a VPM package ZIP from the package files, without repository tooling."""
import argparse
import json
from pathlib import Path
import zipfile


def build(root: Path, tag: str | None = None) -> Path:
    package_root = root / "Packages" / "net.lynuwu.toolbox"
    manifest = json.loads((package_root / "package.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    if tag is not None and tag != f"v{version}":
        raise ValueError(f"Tag {tag!r} must match package version v{version}")
    filename = f"{manifest['name']}-{version}.zip"
    expected_url = f"https://github.com/meowvelyn/toolbox/releases/download/v{version}/{filename}"
    if manifest.get("url") != expected_url:
        raise ValueError(f"Set package.json url to {expected_url}")
    output = root / "dist" / filename
    output.parent.mkdir(exist_ok=True)
    paths = sorted(p for p in package_root.rglob("*") if p.is_file())
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in paths:
            archive.write(path, path.relative_to(package_root).as_posix())
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", help="Require a tag matching the package version")
    arguments = parser.parse_args()
    print(build(Path(__file__).resolve().parents[1], arguments.tag))
