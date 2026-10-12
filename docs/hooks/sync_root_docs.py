from pathlib import Path
import shutil


ROOT = Path(__file__).resolve().parents[2]
DOCS_DIR = ROOT / "docs"
GENERATED_DIR = DOCS_DIR / "_generated"
SOURCE_FILES = [
    "README.md",
    "PrzewodnikUżytkownika.md",
    "architektura.md",
    "plan.md",
    "CHANGELOG.md",
]

SOURCE_DIRECTORIES = [
    "config",
]


def _rewrite_readme_for_mkdocs(content: str) -> str:
    content = content.replace("(docs/brokerless-demo.png)", "(../brokerless-demo.png)")
    content = content.replace(
        "[Apache-2.0](LICENSE)",
        "[Apache-2.0](https://github.com/andrzejskutnik/hobiary-logpipe/blob/main/LICENSE)",
    )
    content = content.replace(
        "#jak-dodać-plugin-do-aplikacji",
        "#jak-dodac-plugin-do-aplikacji",
    )
    content = content.replace(
        "**[HobiaryLogDB](../hobiary-logdb)**",
        "**HobiaryLogDB**",
    )
    return content


def on_pre_build(*args, **kwargs):
    GENERATED_DIR.mkdir(parents=True, exist_ok=True)
    (GENERATED_DIR / "AGENTS.md").unlink(missing_ok=True)

    for file_name in SOURCE_FILES:
        source = ROOT / file_name
        target = GENERATED_DIR / file_name
        content = source.read_text(encoding="utf-8")
        if file_name == "README.md":
            content = _rewrite_readme_for_mkdocs(content)
        target.write_text(content, encoding="utf-8")

    for directory_name in SOURCE_DIRECTORIES:
        source_dir = ROOT / directory_name
        target_dir = GENERATED_DIR / directory_name
        if target_dir.exists():
            shutil.rmtree(target_dir)
        shutil.copytree(source_dir, target_dir)