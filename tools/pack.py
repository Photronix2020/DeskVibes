"""Packages a DeskVibes release.

    python tools\\pack.py 0.1.0            framework-dependent (needs the .NET 8 Desktop Runtime, ~1 MB)
    python tools\\pack.py 0.1.0 --full     self-contained (runs on any Windows 10/11 x64, much bigger)

Sets the version in the .csproj, runs the tests, publishes one DeskVibes.exe, zips it with README.txt as
DeskVibes-<version>.zip in the project folder (older zips there are removed) and keeps a copy in releases\\.
"""
import os, re, shutil, subprocess, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(ROOT, "src", "DeskVibes", "DeskVibes.csproj")


def run(*cmd):
    print(">", " ".join(cmd))
    r = subprocess.run(cmd, cwd=ROOT)
    if r.returncode != 0:
        sys.exit(f"failed: {' '.join(cmd)}")


def main():
    if len(sys.argv) < 2 or not re.fullmatch(r"\d+\.\d+\.\d+(-[\w.]+)?", sys.argv[1]):
        sys.exit(__doc__)
    ver = sys.argv[1]
    full = "--full" in sys.argv
    num = ver.split("-")[0]

    text = open(PROJ, encoding="utf-8").read()
    text = re.sub(r"<Version>[^<]*</Version>", f"<Version>{num}</Version>", text)
    open(PROJ, "w", encoding="utf-8").write(text)

    run("dotnet", "run", "--project", os.path.join("tests", "DeskVibes.Tests"), "-c", "Release")

    out = os.path.join(ROOT, "build", ver)
    shutil.rmtree(out, ignore_errors=True)
    run("dotnet", "publish", PROJ, "-c", "Release", "-r", "win-x64", "-o", out,
        "--self-contained", "true" if full else "false",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=none",
        *(["-p:EnableCompressionInSingleFile=true"] if full else []))

    for f in os.listdir(ROOT):
        if re.fullmatch(r"DeskVibes-.*\.zip", f):
            os.remove(os.path.join(ROOT, f))
    name = f"DeskVibes-{ver}{'-full' if full else ''}.zip"
    zpath = os.path.join(ROOT, name)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(os.path.join(out, "DeskVibes.exe"), "DeskVibes/DeskVibes.exe")
        z.write(os.path.join(ROOT, "README.md"), "DeskVibes/README.txt")
    os.makedirs(os.path.join(ROOT, "releases"), exist_ok=True)
    shutil.copy2(zpath, os.path.join(ROOT, "releases", name))
    print(f"\n{zpath}  ({os.path.getsize(zpath) // 1024} KB)")


if __name__ == "__main__":
    main()
