"""
dev_tools/asmdef_scanner.py - Scans all .asmdef files in Assets/ and Packages/,
builds a dependency map, and writes to dev_tools/schemas/assembly_map.json.

No LM Studio needed - pure JSON parsing.

Usage (via run_dev_tools.py):
    python dev_tools/run_dev_tools.py asmdef
"""

import json
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

# --- Paths ---
REPO_ROOT = Path(__file__).parent.parent.resolve()
SCAN_ROOTS = [
    REPO_ROOT / "DevProject/Assets",
    REPO_ROOT / "DevProject/Packages",
]
ASSEMBLY_MAP_OUT = Path(__file__).parent / "schemas" / "assembly_map.json"

# Directories to skip when scanning (Samples~ are not real project code)
SKIP_DIRS = {"Samples~", "Library", ".git"}


def _should_skip(path: Path) -> bool:
    return any(part in SKIP_DIRS for part in path.parts)


def scan_asmdefs(scan_roots=None, include_tests: bool = True) -> Dict[str, dict]:
    """
    Scan all .asmdef files and return a flat dict: assembly_name -> info.

    Each entry includes:
      name, path, rootNamespace, references, includePlatforms,
      autoReferenced, allowUnsafeCode, defineConstraints,
      precompiledReferences, is_editor_only, is_test, referenced_by
    """
    if scan_roots is None:
        scan_roots = SCAN_ROOTS

    assemblies: Dict[str, dict] = {}

    for root in scan_roots:
        if not root.exists():
            continue
        for asmdef_path in sorted(root.rglob("*.asmdef")):
            if _should_skip(asmdef_path):
                continue
            try:
                # Try utf-8 first; fall back to utf-8-sig to handle BOM
                try:
                    text = asmdef_path.read_text(encoding="utf-8")
                except UnicodeDecodeError:
                    text = asmdef_path.read_text(encoding="utf-8-sig")
                data = json.loads(text)
            except Exception as e:
                print(f"[AsmDef] WARNING: could not parse {asmdef_path}: {e}")
                continue

            name = data.get("name", asmdef_path.stem)
            try:
                rel_path = str(asmdef_path.relative_to(REPO_ROOT)).replace("\\", "/")
            except ValueError:
                rel_path = str(asmdef_path).replace("\\", "/")

            platforms = data.get("includePlatforms", [])
            constraints = data.get("defineConstraints", [])
            is_test = "UNITY_INCLUDE_TESTS" in constraints

            if not include_tests and is_test:
                continue

            assemblies[name] = {
                "name": name,
                "path": rel_path,
                "rootNamespace": data.get("rootNamespace", ""),
                "references": data.get("references", []),
                "precompiledReferences": data.get("precompiledReferences", []),
                "includePlatforms": platforms,
                "autoReferenced": data.get("autoReferenced", True),
                "allowUnsafeCode": data.get("allowUnsafeCode", False),
                "defineConstraints": constraints,
                "is_editor_only": "Editor" in platforms,
                "is_test": is_test,
                "referenced_by": [],  # filled below
            }

    # Build reverse reference map
    for name, info in assemblies.items():
        for ref in info["references"]:
            if ref in assemblies:
                assemblies[ref]["referenced_by"].append(name)

    # Sort for stable output
    for info in assemblies.values():
        info["referenced_by"] = sorted(info["referenced_by"])

    return assemblies


def build_map_output(assemblies: Dict[str, dict]) -> dict:
    """Split into project vs package assemblies and add metadata."""
    project = {k: v for k, v in assemblies.items()
               if v["path"].startswith("DevProject/Assets/")}
    packages = {k: v for k, v in assemblies.items()
                if v["path"].startswith("DevProject/Packages/")}

    return {
        "_meta": {
            "generated": datetime.now().isoformat(),
            "total": len(assemblies),
            "project_count": len(project),
            "package_count": len(packages),
            "scan_roots": [str(r) for r in SCAN_ROOTS],
        },
        "project": project,
        "packages": packages,
    }


def run_asmdef_cli() -> bool:
    """Entry point called by run_dev_tools.py."""
    print("[AsmDef] Scanning .asmdef files in Assets/ and Packages/...")

    assemblies = scan_asmdefs()
    output = build_map_output(assemblies)

    ASSEMBLY_MAP_OUT.parent.mkdir(parents=True, exist_ok=True)
    ASSEMBLY_MAP_OUT.write_text(
        json.dumps(output, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    project = output["project"]
    packages = output["packages"]
    print(f"[AsmDef] Project assemblies: {len(project)}, Package assemblies: {len(packages)}")
    print(f"[AsmDef] Written -> {ASSEMBLY_MAP_OUT}")

    _print_assembly_graph(project, packages, label="Project")
    _print_assembly_graph(packages, project, label="Packages", compact=True)

    # Report any unresolved references
    issues = check_missing_references(assemblies)
    if issues:
        print(f"\n[AsmDef] WARNING: {len(issues)} unresolved reference(s):")
        for issue in issues:
            print(f"  {issue['assembly']} -> MISSING: {issue['missing_ref']}")
    else:
        print("\n[AsmDef] All project assembly references resolved OK.")

    return True


def _print_assembly_graph(
    primary: dict,
    other: dict,
    label: str,
    compact: bool = False,
) -> None:
    """Print assembly graph for a group. compact=True shows one line per assembly."""
    all_known = set(primary.keys()) | set(other.keys())
    print(f"\n[AsmDef] {label} assembly dependency graph ({len(primary)} assemblies):")
    for name, info in sorted(primary.items(), key=lambda x: x[0]):
        tags = []
        if info["is_editor_only"]:
            tags.append("EDITOR")
        if info["is_test"]:
            tags.append("TEST")
        if not info["autoReferenced"]:
            tags.append("no-autoref")
        tag_str = f" [{', '.join(tags)}]" if tags else ""

        refs = info["references"]

        if compact:
            # One-liner for packages
            refs_str = f"  refs=[{', '.join(refs[:3])}{'...' if len(refs) > 3 else ''}]" if refs else ""
            used_by_str = f"  used-by=[{', '.join(info['referenced_by'][:2])}{'...' if len(info['referenced_by']) > 2 else ''}]" if info["referenced_by"] else ""
            print(f"  {name}{tag_str}{refs_str}{used_by_str}")
        else:
            print(f"  {name}{tag_str}")
            if refs:
                internal = [r for r in refs if r in primary]
                cross = [r for r in refs if r in other]
                external = [r for r in refs if r not in all_known]
                if internal:
                    print(f"    -> (project)  {', '.join(internal)}")
                if cross:
                    print(f"    -> (package)  {', '.join(cross)}")
                if external:
                    print(f"    -> (builtin)  {', '.join(external)}")
            if info["referenced_by"]:
                print(f"    <- (used by)  {', '.join(info['referenced_by'])}")


def find_assembly_for_file(cs_path: str, assemblies: Optional[Dict[str, dict]] = None) -> Optional[str]:
    """
    Given a .cs file path, return the assembly it belongs to by finding the
    nearest .asmdef in the same or parent directories.
    """
    if assemblies is None:
        assemblies = scan_asmdefs()

    path = Path(cs_path).resolve()
    # Build a map: asmdef_dir -> assembly_name
    dir_map = {}
    for name, info in assemblies.items():
        asmdef_path = (REPO_ROOT / info["path"]).resolve()
        dir_map[str(asmdef_path.parent)] = name

    # Walk up from the .cs file's directory
    current = path.parent
    while True:
        if str(current) in dir_map:
            return dir_map[str(current)]
        parent = current.parent
        if parent == current:
            break
        current = parent

    return None  # falls into default assembly (Assembly-CSharp)


def _collect_library_cache_names() -> set:
    """Collect assembly names from Library/PackageCache (names only, no full parse)."""
    cache_root = REPO_ROOT / "DevProject/Library/PackageCache"
    names = set()
    if not cache_root.exists():
        return names
    for asmdef_path in cache_root.rglob("*.asmdef"):
        if "Samples~" in str(asmdef_path):
            continue
        try:
            try:
                text = asmdef_path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                text = asmdef_path.read_text(encoding="utf-8-sig")
            data = json.loads(text)
            name = data.get("name", asmdef_path.stem)
            names.add(name)
        except Exception:
            pass
    return names


def check_missing_references(assemblies: Optional[Dict[str, dict]] = None) -> List[dict]:
    """
    Check all project assemblies for references to assemblies that don't exist anywhere.
    Returns a list of {assembly, missing_ref} dicts.

    Note: Library/PackageCache names are checked to avoid false positives for
    installed packages (e.g. Unity.RenderPipelines.Core.Runtime).
    """
    if assemblies is None:
        assemblies = scan_asmdefs()

    # All known assembly names (Assets + Packages)
    all_known = set(assemblies.keys())

    # Also collect names from Library/PackageCache
    all_known |= _collect_library_cache_names()

    # Well-known Unity built-in assemblies (not in .asmdef files)
    builtin = {
        "UnityEngine", "UnityEditor", "UnityEngine.UI", "UnityEngine.TestRunner",
        "UnityEditor.TestRunner", "Unity.TextMeshPro", "Unity.Cinemachine",
        "Unity.AI.Navigation", "Unity.InputSystem", "Unity.Addressables",
        "Unity.ResourceManager", "MCPForUnity.Editor", "MCPForUnity.Runtime",
        "Unity.ML-Agents", "Unity.ML-Agents.Extensions", "Unity.ML-Agents.Editor",
        "Unity.InferenceEngine", "Unity.ML-Agents.CommunicatorObjects",
        "Unity.PerformanceTesting", "Unity.Collections", "Unity.Burst",
        "Unity.Jobs", "Unity.Mathematics", "Unity.Profiling.Core",
    }

    project_assemblies = {k: v for k, v in assemblies.items()
                          if v["path"].startswith("DevProject/Assets/")}

    issues = []
    for name, info in project_assemblies.items():
        for ref in info["references"]:
            if ref not in all_known and ref not in builtin:
                issues.append({
                    "assembly": name,
                    "missing_ref": ref,
                    "asmdef_path": info["path"],
                })

    return issues
