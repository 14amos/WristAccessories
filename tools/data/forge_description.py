# forge_description.py <README.md> <out.md>: the README as a TABBED SPT Forge description (user 2026-10-27).
# Forge tab syntax (as SPT mods use it on GitHub): a heading with {.tabset} opens the tabs, each heading one level below is a tab,
# {.endtabset} closes them. Pictures and repo files are linked absolutely (raw.githubusercontent / github.com).
import re, sys
README, OUT = sys.argv[1:3]; REPO = "14amos/WristAccessories"
s = open(README, encoding="utf-8").read()
s = s.replace("](docs/images/", f"](https://raw.githubusercontent.com/{REPO}/main/docs/images/")
s = s.replace("see `LICENSE`", f"see [LICENSE](https://github.com/{REPO}/blob/main/LICENSE)").replace("see `tools/README.md`", f"see [tools/README.md](https://github.com/{REPO}/blob/main/tools/README.md)")
parts = re.split(r"^## (.+)$", s, flags=re.M)
intro = parts[0].split("\n", 1)[1].strip()
sec = {parts[i].strip(): parts[i + 1].strip() for i in range(1, len(parts), 2)}
tabs = [
    ("Description", intro + "\n\n#### What it adds\n" + sec["What it adds"]),
    ("Pictures", sec["Pictures"]),
    ("Installation", "#### Requirements\n" + sec["Requirements"] + "\n\n#### Install\n" + sec["Install"] + "\n\n#### Uninstall\n" + sec["Uninstall"]),
    ("Recommended Mods", sec["Recommended mods"]),
    ("Compatibility", sec["Compatibility"] + "\n\n#### Troubleshooting\n" + sec["Troubleshooting"]),
    ("Credits & License", sec["Credits"] + "\n\n#### License\n" + sec["License"] + f"\n\n#### Source code\nhttps://github.com/{REPO}\n\n" + sec["Building from source"]),
]
open(OUT, "w", encoding="utf-8").write("## WristAccessories {.tabset}\n\n" + "\n\n".join(f"### {t}\n\n{b}" for t, b in tabs) + "\n\n{.endtabset}\n")
print("written", OUT)
