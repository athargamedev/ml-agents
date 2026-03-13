# Unity WebGL Shader Optimization Plan

## Your Custom Shaders (6 total)

| Shader | Path |
|--------|------|
| Dissolve | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Misc Effects/Shaders/Dissolve.shadergraph` |
| HeatDisstortionWobble | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Misc Effects/Shaders/HeatDisstortionWobble.shadergraph` |
| Ice Shapes | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Magic Effects/Shaders/Ice Shapes.shadergraph` |
| Ice Ball | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Magic Effects/Shaders/Ice Ball.shadergraph` |
| Respawn | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Misc Effects/Shaders/Respawn.shadergraph` |
| DissolveLine | `DevProject/Assets/Network_Game/ParticlePack/EffectExamples/Misc Effects/Shaders/DissolveLine.shadergraph` |

---

## Issue 1: pow(f, e) Warning Fix

**Problem:** `pow(f, e)` fails for negative `f` values.

**Correct fix in Shader Graph:**
1. Open the shader
2. Find the **Power** node(s)
3. The issue is on the **Exponent (E)** input if using Fresnel (which can output negative)
4. Fix: Add **Absolute** node to the **Fresnel** output BEFORE connecting to Power:
   ```
   Fresnel Output → Absolute → Power Input
   ```

If that doesn't work, add Absolute to **both** Fresnel and any other inputs to Power node.

---

## WebGL Shader Optimization Checklist

### Phase 1: Shader Code (Your 6 Custom Shaders)

- [ ] **Remove pow() with negative bases** — use `abs()` or handle conditionally
- [ ] **Avoid dependent texture reads** — UVs should not depend on fragment position
- [ ] **Simplify math** — replace `pow(x, y)` with `x*x` where exponent is small integers
- [ ] **Use half precision** — use `half` instead of `float` where possible (URP)
- [ ] **Avoid branches** — use `lerp()` instead of `if` in fragment shaders

### Phase 2: URP Asset Settings

- [ ] **SRP Batcher** — Enable (default in URP)
- [ ] **GPU Instancing** — Enable on materials
- [ ] **Depth Texture** — Disable if not needed
- [ ] **Opaque Texture** — Disable if not using post-processing
- [ ] **Lit Shader Detail Level** — Set to "Fastest"

### Phase 3: WebGL Build Settings

- [ ] **Color Space** — Use **Linear** (or Gamma if targeting older devices)
- [ ] **Lightmap Encoding** — Normal Quality
- [ ] **Lightmap Modes** — Strip unused variants
- [ ] **Fog Modes** — Strip unused
- [ ] **Instancing Variants** — Strip Unused
- [ ] **Always Included Shaders** — REMOVE unused shaders from this list

### Phase 4: Texture Optimization

- [ ] **Max Texture Size** — 1024 or 2048 for WebGL (not 4096)
- [ ] **Compression** — Use DXT/BC for desktop, ASTC for mobile
- [ ] **Mipmap** — Enable for textures > 512
- [ ] **Read/Write** — Disable unless needed

---

## Recommended URP Settings for WebGL

```
URP Asset → Quality:
- Render Scale: 1.0 (or 0.9 for mobile)
- Anti Aliasing: Disabled or 2x
- Shadow Distance: 20 (lower than desktop)
- Shadow Cascades: 1 or 2
- LOD Cross Fade: Bayer Matrix

URP Asset → Lighting:
- Main Light: Per Pixel
- Cast Shadows: ON
- Additional Lights: 0 (or 1)
- Additional Light Shadows: OFF

URP Asset → Post Processing:
- HDR: OFF
- Anti Aliasing (Post): OFF
- Grading Mode: Low Dynamic Range
- LUT Size: 16 (minimum)
- Fast sRGB/Linear: ON
```

---

## Phase 5: Shader Variant Stripping (Critical for WebGL)

### Why It Matters
Each shader variant adds ~KB to build. WebGL has limited memory.

### Settings in Project Settings → Graphics
| Setting | Recommended |
|---------|-------------|
| Lightmap Modes | Automatic |
| Fog Modes | Automatic |
| Instancing Variants | Strip Unused |
| BatchRendererGroup Variants | Strip all |

### Always Included Shaders
**REMOVE** these if not used:
- Any Built-in Pipeline shaders
- Terrain shaders not used
- Legacy particles shaders

---

## Quick Shader Audit Commands

Search your shaders for problematic patterns:

```
# In shader files (.shader, .shadergraph JSON)
pow(
sin(
cos(
if (
```

Search in ShaderGraph JSON:
```bash
grep -r "m_Type" "*.shadergraph" | grep -i "power\|fresnel"
```

---

## ⚠️ Manual Fix Required: pow() Warning in Shader Graphs

### Why This Happens
- Fresnel Effect nodes and some math operations internally use `pow(f, e)`
- When Fresnel output is negative (can happen at grazing angles), pow() returns undefined results
- This triggers the compiler warning on D3D11 (desktop WebGL)

### Shaders with Power Nodes (need checking)
- `Ice Shapes.shadergraph` - Has Power node ⚠️ WARNINGS
- `Ice Ball.shadergraph` - Has 3 Power nodes
- Others (Dissolve, HeatDisstortionWobble, Respawn, DissolveLine) - No Power node ✅

### Manual Fix in Unity Editor

**For Ice Shapes.shadergraph:**
1. Open Shader Graph in Unity
2. Find the **Power** node (search in graph: look for "Power")
3. Check the **Input (A)** slot - this is likely connected to a Fresnel or similar
4. Add an **Absolute** node between the source and Power:
   ```
   [Fresnel Output] → [Absolute Node] → [Power Input A]
   ```
5. Save the shader graph
6. Rebuild to verify warning is gone

**Alternative fix (Saturate):**
If Absolute doesn't work, use **Saturate** node instead:
```
[Source] → [Saturate] → [Power]
```

---

## Testing

## Testing

1. **Build size** — Target < 50MB for WebGL
2. **Memory** — Target < 512MB GPU memory
3. **Frame rate** — 30+ FPS minimum on mid-range devices
4. **Load time** — < 10 seconds on broadband

---

## References

- [Unity WebGL Optimization Docs](https://docs.unity3d.com/6000.2/Documentation/Manual/web-optimization-graphics.html)
- [URP Performance Guide](https://docs.unity3d.com/6000.1/Documentation/Manual/urp/optimize-for-better-performance.html)
- [WebGL Performance Considerations](https://docs.unity3d.com/6000.2/Documentation/Manual/webgl-performance.html)
