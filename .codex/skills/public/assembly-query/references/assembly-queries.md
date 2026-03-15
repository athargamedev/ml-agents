# Assembly Queries Reference

## Contents
- List All Assemblies
- Get Assembly Info
- Find Dependents
- Find Symbols in an Assembly
- Analyze Package Dependency Hotspots
- Query Symbols by Assembly
- Payload Fields

## List All Assemblies (First-Party vs Packages)
```python
from qdrant_client import QdrantClient

client = QdrantClient("localhost", port=6333)

collections = [c.name for c in client.get_collections().collections]
if "unity_assemblies" not in collections:
    print("unity_assemblies collection not found. Run indexer with --analyze-usage")
    raise SystemExit(1)

results, _ = client.scroll(
    "unity_assemblies",
    limit=500,
    with_payload=["name", "is_package", "references", "file_path"],
)

first_party = [r for r in results if not r.payload.get("is_package")]
packages = [r for r in results if r.payload.get("is_package")]

print(f"=== First-Party Assemblies ({len(first_party)}) ===")
for r in first_party:
    refs = len(r.payload.get("references", []))
    print(f"  {r.payload['name']} ({refs} deps)")

print(f"\n=== Package Assemblies ({len(packages)}) ===")
for r in sorted(packages, key=lambda x: x.payload["name"])[:30]:
    print(f"  {r.payload['name']}")
if len(packages) > 30:
    print(f"  ... and {len(packages) - 30} more")
```

## Get Assembly Info
```python
from qdrant_client import QdrantClient
from qdrant_client.models import Filter, FieldCondition, MatchValue

assembly_name = "ASSEMBLY_NAME"

client = QdrantClient("localhost", port=6333)
results, _ = client.scroll(
    "unity_assemblies",
    scroll_filter=Filter(
        must=[FieldCondition(key="name", match=MatchValue(value=assembly_name))]
    ),
    limit=1,
    with_payload=True,
)

if results:
    p = results[0].payload
    print(f"Assembly: {p['name']}")
    print(f"Path: {p.get('file_path', 'N/A')}")
    print(f"Is Package: {p.get('is_package', False)}")
    print(f"\nReferences ({len(p.get('references', []))}):")
    for ref in p.get("references", []):
        print(f"  - {ref}")
    print("\nDefine Symbols:")
    for sym in p.get("define_symbols", []):
        print(f"  - {sym}")
else:
    print(f"Assembly not found: {assembly_name}")
```

## Find Dependents (What Uses This Assembly)
```python
from qdrant_client import QdrantClient
from qdrant_client.models import Filter, FieldCondition, MatchAny

target = "TARGET_ASSEMBLY"

client = QdrantClient("localhost", port=6333)
results, _ = client.scroll(
    "unity_assemblies",
    scroll_filter=Filter(
        must=[FieldCondition(key="references", match=MatchAny(any=[target]))]
    ),
    limit=100,
    with_payload=["name", "is_package"],
)

print(f"Assemblies that depend on {target}:")
for r in results:
    pkg = " (package)" if r.payload.get("is_package") else ""
    print(f"  - {r.payload['name']}{pkg}")
```

## Find Symbols in an Assembly
```python
from qdrant_client import QdrantClient
from qdrant_client.models import Filter, FieldCondition, MatchValue

assembly = "Assembly-CSharp"

client = QdrantClient("localhost", port=6333)
results, _ = client.scroll(
    "unity_codebase",
    scroll_filter=Filter(
        must=[FieldCondition(key="assembly_name", match=MatchValue(value=assembly))]
    ),
    limit=50,
    with_payload=["name", "kind", "namespace"],
)

print(f"Symbols in {assembly}:")
for r in results:
    p = r.payload
    print(f"  {p['kind']}: {p.get('namespace', '')}.{p['name']}")
```

## Analyze Package Dependency Hotspots
```python
from qdrant_client import QdrantClient

client = QdrantClient("localhost", port=6333)
results, _ = client.scroll(
    "unity_assemblies",
    limit=500,
    with_payload=["name", "is_package", "references"],
)

ref_counts = {}
for r in results:
    for ref in r.payload.get("references", []):
        ref_counts[ref] = ref_counts.get(ref, 0) + 1

print("Most Referenced Assemblies:")
for name, count in sorted(ref_counts.items(), key=lambda x: -x[1])[:20]:
    print(f"  {count:3d} refs: {name}")
```

## Query Symbols by Assembly
```python
from qdrant_client.models import Filter, FieldCondition, MatchValue

filter = Filter(
    must=[
        FieldCondition(key="assembly_name", match=MatchValue(value="Unity.Behavior")),
        FieldCondition(key="kind", match=MatchValue(value="class")),
    ]
)
```

## Payload Fields
- unity_assemblies: name, file_path, is_package, references, define_symbols, include_platforms, exclude_platforms
- unity_codebase: assembly_name, is_package
