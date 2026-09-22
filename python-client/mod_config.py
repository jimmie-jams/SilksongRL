"""Reading and writing the mod's BepInEx config, so the launcher can pick the encounter.

Edited in place with a line scan rather than configparser, which discards every comment on write -
and BepInEx keeps its setting documentation in those comments. Also how parallel instances will
differ later, since each gets its own copy of BepInEx/config.
"""

import re
from pathlib import Path
from typing import Dict, Optional, Tuple

# Friendlier spellings -> the mod's TargetBoss values. Mirrors CreateEncounter in RLManager.cs.
ENCOUNTERS: Dict[str, str] = {
    "lace_1": "Lace_1",
    "lace1": "Lace_1",
    "lace": "Lace_1",
    "lace_2": "Lace_2",
    "lace2": "Lace_2",
    "savage_beastfly": "Savage_Beastfly",
    "savagebeastfly": "Savage_Beastfly",
    "beastfly": "Savage_Beastfly",
}

ENCOUNTER_CHOICES = ("Lace_1", "Lace_2", "Savage_Beastfly")


def normalize_encounter(name: str) -> str:
    """Map a user-typed encounter name onto the mod's TargetBoss value."""
    key = name.strip().lower().replace(" ", "_").replace("-", "_")
    if key in ENCOUNTERS:
        return ENCOUNTERS[key]
    raise ValueError(
        f"Unknown encounter '{name}'. Expected one of: {', '.join(ENCOUNTER_CHOICES)}"
    )


class ModConfig:
    """The mod's silksongrl.cfg."""

    def __init__(self, path: Path):
        self.path = path

    @property
    def exists(self) -> bool:
        return self.path.is_file()

    def read(self, section: str, key: str) -> Optional[str]:
        if not self.exists:
            return None
        current_section = None
        for line in self.path.read_text(encoding="utf-8", errors="replace").splitlines():
            stripped = line.strip()
            header = re.match(r"^\[(.+)\]$", stripped)
            if header:
                current_section = header.group(1)
                continue
            if current_section != section or stripped.startswith("#") or "=" not in stripped:
                continue
            name, _, value = stripped.partition("=")
            if name.strip() == key:
                return value.strip()
        return None

    def set_values(self, values: Dict[Tuple[str, str], str]) -> Dict[Tuple[str, str], str]:
        """
        Set (section, key) -> value, leaving comments, ordering and other settings untouched.
        Returns only what changed. Missing keys are appended; a missing file is created minimal,
        and BepInEx fills in the rest on next launch.
        """
        if not values:
            return {}

        if not self.exists:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            self.path.write_text(_render_minimal_config(values), encoding="utf-8")
            print(f"[ModConfig] Created {self.path}")
            return dict(values)

        lines = self.path.read_text(encoding="utf-8", errors="replace").splitlines()
        remaining = dict(values)
        changed: Dict[Tuple[str, str], str] = {}
        current_section = None

        for index, line in enumerate(lines):
            stripped = line.strip()
            header = re.match(r"^\[(.+)\]$", stripped)
            if header:
                current_section = header.group(1)
                continue
            if current_section is None or stripped.startswith("#") or "=" not in stripped:
                continue
            name, _, old_value = stripped.partition("=")
            target = (current_section, name.strip())
            if target not in remaining:
                continue
            new_value = remaining.pop(target)
            if old_value.strip() != new_value:
                lines[index] = f"{name.strip()} = {new_value}"
                changed[target] = new_value

        # Anything the file did not already define gets appended under its section.
        for (section, key), value in remaining.items():
            lines = _append_under_section(lines, section, key, value)
            changed[(section, key)] = value

        if changed:
            self.path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        return changed


def _append_under_section(lines, section: str, key: str, value: str):
    """Insert "key = value" at the end of a section, adding the section if it is missing."""
    section_start = None
    for index, line in enumerate(lines):
        if line.strip() == f"[{section}]":
            section_start = index
            break

    if section_start is None:
        if lines and lines[-1].strip():
            lines.append("")
        lines.extend([f"[{section}]", "", f"{key} = {value}"])
        return lines

    # Walk to the line before the next section header.
    insert_at = len(lines)
    for index in range(section_start + 1, len(lines)):
        if re.match(r"^\[(.+)\]$", lines[index].strip()):
            insert_at = index
            break
    while insert_at > section_start + 1 and not lines[insert_at - 1].strip():
        insert_at -= 1

    # Blank line between entries, matching BepInEx's own layout.
    lines.insert(insert_at, f"{key} = {value}")
    if insert_at > section_start + 1:
        lines.insert(insert_at, "")
    return lines


def _render_minimal_config(values: Dict[Tuple[str, str], str]) -> str:
    sections: Dict[str, Dict[str, str]] = {}
    for (section, key), value in values.items():
        sections.setdefault(section, {})[key] = value

    out = ["## Settings file written by the SilksongRL launcher",
           "## BepInEx will fill in the remaining settings and their descriptions on next launch.",
           ""]
    for section, entries in sections.items():
        out.append(f"[{section}]")
        out.append("")
        for key, value in entries.items():
            out.append(f"{key} = {value}")
        out.append("")
    return "\n".join(out)
