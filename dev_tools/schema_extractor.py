"""
dev_tools/schema_extractor.py — Extracts project API schemas from C# source files.

Parses key source files using regex to build a JSON schema describing:
- NpcDialogueAgent: observations, actions, serialized fields, public events
- EffectDefinition / EffectCatalog: effect data contract
- NetworkDialogueService: public API, events, status enums

The output schema is written to dev_tools/schemas/project_api_schema.json
and can be read by Claude in future sessions to make accurate edits
without re-reading source files.

Usage (via run_dev_tools.py):
    python dev_tools/run_dev_tools.py schema
"""

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Optional

# ── Paths ──────────────────────────────────────────────────────────────────────
REPO_ROOT = Path(__file__).parent.parent.resolve()
SCHEMA_OUT = Path(__file__).parent / "schemas" / "project_api_schema.json"

TARGET_FILES = {
    "NpcDialogueAgent":      REPO_ROOT / "DevProject/Assets/ML-Agents/Scripts/NpcDialogueAgent.cs",
    "NetworkDialogueService": REPO_ROOT / "DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs",
    "EffectDefinition":       REPO_ROOT / "DevProject/Assets/Network_Game/Dialogue/Effects/EffectDefinition.cs",
    "EffectCatalog":          REPO_ROOT / "DevProject/Assets/Network_Game/Dialogue/Effects/EffectCatalog.cs",
    "NpcDialogueProfile":     REPO_ROOT / "DevProject/Assets/Network_Game/Dialogue/NpcDialogueProfile.cs",
}
# ──────────────────────────────────────────────────────────────────────────────


@dataclass
class FieldInfo:
    name: str
    type: str
    visibility: str
    has_serialize_field: bool
    tooltip: Optional[str] = None


@dataclass
class MethodInfo:
    name: str
    return_type: str
    params: str
    visibility: str
    is_override: bool = False


@dataclass
class EventInfo:
    name: str
    delegate_type: str
    visibility: str


@dataclass
class FileSchema:
    class_name: str
    namespace: str
    base_class: Optional[str]
    fields: List[FieldInfo] = field(default_factory=list)
    public_methods: List[MethodInfo] = field(default_factory=list)
    events: List[EventInfo] = field(default_factory=list)
    enums: List[str] = field(default_factory=list)
    observation_layout: List[str] = field(default_factory=list)
    action_layout: dict = field(default_factory=dict)
    notes: List[str] = field(default_factory=list)


def _extract_serialized_fields(text: str) -> List[FieldInfo]:
    """Extract [SerializeField] private fields and public fields."""
    fields = []

    # Pattern: optional [SerializeField] + optional [Min/Range/...] + access_mod + type + name
    pattern = re.compile(
        r'(\[SerializeField\])\s*'
        r'(?:\[[^\]]+\]\s*)*'
        r'(private|protected|public|internal)\s+'
        r'(?:readonly\s+)?'
        r'([\w<>\[\]\.]+)\s+'
        r'(m_\w+|\w+)\s*;',
        re.MULTILINE,
    )
    for m in pattern.finditer(text):
        fields.append(FieldInfo(
            name=m.group(4),
            type=m.group(3),
            visibility=m.group(2),
            has_serialize_field=True,
        ))

    # Also capture public fields (without [SerializeField])
    pub_pattern = re.compile(
        r'^[ \t]+public\s+(?:static\s+)?(?:readonly\s+)?([\w<>\[\]\.]+)\s+(\w+)\s*[;=]',
        re.MULTILINE,
    )
    existing_names = {f.name for f in fields}
    for m in pub_pattern.finditer(text):
        name = m.group(2)
        if name not in existing_names and not name[0].isupper():  # skip properties/methods
            fields.append(FieldInfo(
                name=name,
                type=m.group(1),
                visibility="public",
                has_serialize_field=False,
            ))

    return fields


def _extract_public_methods(text: str) -> List[MethodInfo]:
    """Extract public method signatures."""
    pattern = re.compile(
        r'public\s+(?:override\s+)?(?:static\s+)?'
        r'([\w<>\[\]\.]+)\s+'
        r'([A-Z]\w+)\s*'
        r'\(([^)]*)\)',
        re.MULTILINE,
    )
    methods = []
    seen = set()
    for m in pattern.finditer(text):
        name = m.group(2)
        if name in seen or name in ("class", "enum", "interface"):
            continue
        seen.add(name)
        is_override = "override" in text[max(0, m.start()-20):m.start()]
        methods.append(MethodInfo(
            name=name,
            return_type=m.group(1),
            params=m.group(3).strip(),
            visibility="public",
            is_override=is_override,
        ))
    return methods


def _extract_events(text: str) -> List[EventInfo]:
    """Extract static and instance events."""
    pattern = re.compile(
        r'(public|internal|protected)\s+(?:static\s+)?'
        r'event\s+([\w<>]+)\s+(\w+)\s*;',
        re.MULTILINE,
    )
    events = []
    for m in pattern.finditer(text):
        events.append(EventInfo(
            name=m.group(3),
            delegate_type=m.group(2),
            visibility=m.group(1),
        ))
    return events


def _extract_enums(text: str) -> List[str]:
    """Extract enum names defined in the file."""
    pattern = re.compile(r'(?:public|private|internal)\s+enum\s+(\w+)', re.MULTILINE)
    return [m.group(1) for m in pattern.finditer(text)]


def _extract_namespace(text: str) -> str:
    m = re.search(r'namespace\s+([\w\.]+)', text)
    return m.group(1) if m else ""


def _extract_base_class(text: str, class_name: str) -> Optional[str]:
    m = re.search(rf'class\s+{class_name}\s*:\s*([\w\.]+)', text)
    return m.group(1) if m else None


def _extract_observation_comments(text: str) -> List[str]:
    """Extract the inline observation layout from NpcDialogueAgent comments."""
    pattern = re.compile(r'///\s+\[(\d+)\]\s+(.+)', re.MULTILINE)
    observations = {}
    for m in pattern.finditer(text):
        idx = int(m.group(1))
        desc = m.group(2).strip()
        observations[idx] = desc
    return [observations[i] for i in sorted(observations.keys())]


def _build_npc_agent_schema(text: str) -> dict:
    """Build richer schema for NpcDialogueAgent with observation/action layout."""
    obs = _extract_observation_comments(text)

    # Extract discrete action count from attribute or comment
    action_match = re.search(r'Discrete Branches\s*=\s*(\d+)[^\n]*Branch 0 Size\s*=\s*(\d+)', text)
    actions = {
        "type": "discrete",
        "branches": int(action_match.group(1)) if action_match else 1,
        "branch_0_size": int(action_match.group(2)) if action_match else 3,
        "action_meanings": {"0": "idle", "1": "engage/continue", "2": "end conversation"},
    }

    return {
        "observations": obs,
        "space_size": len(obs),
        "actions": actions,
    }


def extract_file_schema(class_name: str, file_path: Path) -> Optional[dict]:
    if not file_path.exists():
        return None

    text = file_path.read_text(encoding="utf-8", errors="replace")
    schema = FileSchema(
        class_name=class_name,
        namespace=_extract_namespace(text),
        base_class=_extract_base_class(text, class_name),
        fields=_extract_serialized_fields(text),
        public_methods=_extract_public_methods(text),
        events=_extract_events(text),
        enums=_extract_enums(text),
    )

    # Enrich NpcDialogueAgent with observation layout
    if class_name == "NpcDialogueAgent":
        extra = _build_npc_agent_schema(text)
        schema.observation_layout = extra.get("observations", [])
        schema.action_layout = extra.get("actions", {})
        schema.notes.append(f"Space Size: {extra.get('space_size', '?')}")

    result = {
        "class": class_name,
        "namespace": schema.namespace,
        "base_class": schema.base_class,
        "serialized_fields": [
            {"name": f.name, "type": f.type, "visibility": f.visibility}
            for f in schema.fields
        ],
        "public_methods": [
            {"name": m.name, "return_type": m.return_type, "params": m.params,
             "is_override": m.is_override}
            for m in schema.public_methods[:20]  # cap to avoid noise
        ],
        "events": [
            {"name": e.name, "delegate": e.delegate_type, "visibility": e.visibility}
            for e in schema.events
        ],
        "enums": schema.enums,
    }

    if schema.observation_layout:
        result["observation_layout"] = schema.observation_layout
    if schema.action_layout:
        result["action_layout"] = schema.action_layout
    if schema.notes:
        result["notes"] = schema.notes

    return result


def run_schema_cli() -> bool:
    """Entry point called by run_dev_tools.py."""
    schemas = {}
    missing = []

    for class_name, file_path in TARGET_FILES.items():
        print(f"[Schema] Extracting: {class_name} from {file_path.name}...", end=" ")
        result = extract_file_schema(class_name, file_path)
        if result:
            schemas[class_name] = result
            field_count  = len(result.get("serialized_fields", []))
            method_count = len(result.get("public_methods", []))
            print(f"OK ({field_count} fields, {method_count} methods)")
        else:
            print(f"SKIPPED (file not found: {file_path})")
            missing.append(class_name)

    SCHEMA_OUT.parent.mkdir(parents=True, exist_ok=True)
    SCHEMA_OUT.write_text(
        json.dumps(schemas, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    print(f"\n[Schema] Extracted {len(schemas)} class schemas -> {SCHEMA_OUT}")
    if missing:
        print(f"[Schema] Skipped (not found): {', '.join(missing)}")
    return len(schemas) > 0
