"""license.txt -> license.rtf, the wizard's licence page (WixUILicenseRtf). Run by build.ps1 before each build."""
import pathlib
import sys

here = pathlib.Path(__file__).parent
text = (here / "license.txt").read_text(encoding="utf-8")


def escape(line: str) -> str:
    out = []
    for ch in line:
        if ch in "\\{}":
            out.append("\\" + ch)
        elif ord(ch) > 127:
            out.append("\\u%d?" % ord(ch))
        else:
            out.append(ch)
    return "".join(out)


headings = {"Street Corsa", "MIT License", "Third-party software"}
body = []
for paragraph in text.strip().split("\n\n"):
    line = paragraph.replace("\n", " ").strip()
    body.append(("\\b " + escape(line) + "\\b0" if line in headings else escape(line)) + "\\par\\par")

rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Tahoma;}}\\viewkind4\\f0\\fs16\n" + "\n".join(body) + "\n}\n"
(here / "license.rtf").write_text(rtf, encoding="ascii")
sys.stdout.write("license.rtf written\n")
