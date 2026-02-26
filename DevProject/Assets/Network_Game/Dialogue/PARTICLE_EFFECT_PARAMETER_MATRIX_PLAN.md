# Particle Effects Parameter Matrix & LLM Prompt Structure Plan

**Created:** 2026-02-12  
**Purpose:** Enable LLMs to make creative, informed decisions about particle effect instantiation during NPC dialogue.

---

## Executive Summary

Your current system has solid foundations:
- `ParticleParameterExtractor` extracts 8 parameter dimensions from dialogue text
- `DialogueSceneEffectsController` applies dynamic effects (rain, shockwave, shield, waypoint, prefab powers)
- NPC profiles define base parameters per effect type
- Dynamic multipliers (1.35x strong, 0.72x weak) scale responses

**The Problem:** LLMs don't know what parameters exist or how to invoke them creatively. The NPC "powers" are underutilized because:
1. Limited semantic vocabulary in prompt context
2. No comprehensive parameter reference in LLM context
3. Effect keywords are too narrow (e.g., "rain burst" vs creative alternatives)
4. Prefab powers lack descriptive parameter hints

---

## Phase 1: Comprehensive Particle Effects Catalog

### 1.1 Existing Effects (Already Integrated)

| Effect Type | Parameters | Current Control |
|-------------|------------|-----------------|
| **Rain Burst** | radius, duration, particleCount, color, speed | `ApplyRainBurst()` |
| **Shockwave** | radius, force, upwardForce, visualDuration, color | `ApplyShockwave()` |
| **Shield Bubble** | radius, duration, color | `ApplyShieldBubble()` |
| **Waypoint Ping** | duration, color | `ApplyWaypointPing()` |
| **Wall Image** | width, height, lifetime, tint, posterTexture | `ApplyProceduralWallImage()` |
| **Prop Spawn** | primitiveType, count, scale, spacing, lifetime, color | `SpawnContextProps()` |
| **Bored Lighting** | color, intensity, transitionDuration | `ApplyBoredLighting()` |

### 1.2 Prefab Powers (Per-NPC)

**Storm Oracle:**
- `LightnigStormCloud.prefab` (Lightning Storm)
- `IceLance.prefab` (Ice Lance)

**Forge Keeper:**
- `FireBall.prefab` (Fire Blast)
- `ElectricalSparks.prefab` (Forge Sparks)

**Archivist:**
- `GroundFog.prefab` (Ground Fog)
- `FireFlies.prefab` (Guide Fireflies)

### 1.3 Available ParticlePack Effects (Not Yet Integrated)

```
Assets/Network_Game/ParticlePack/EffectExamples/
├── Fire & Explosion Effects/
│   ├── BigExplosion.prefab
│   ├── WildFire.prefab
│   └── PlasmaExplosionEffect.prefab
├── Water Effects/
│   └── Shower.prefab (already bound for rain)
├── Legacy Particles/
│   ├── RainEffect.prefab
│   ├── SparksEffect.prefab
│   ├── ElectricalSparksEffect.prefab
└── Misc Effects/
    ├── DustMotesEffect.prefab
    ├── SandSwirlsEffect.prefab
    ├── GoopSprayEffect.prefab
    └── GoopStreamEffect.prefab
```

---

## Phase 2: Semantic Parameter Mapping Matrix

### 2.1 Parameter Dimension Reference (For LLM Context)

```json
{
  "parameter_matrix": {
    "magnitude": {
      "description": "Overall power/size scale",
      "range": [0.6, 2.0],
      "strong_terms": ["massive", "enormous", "colossal", "devastating", "overwhelming"],
      "weak_terms": ["minor", "subtle", "gentle", "delicate", "faint"],
      "particle_property": "startSize * mainMultiplier"
    },
    "spatial": {
      "description": "Area of effect / spread",
      "range": [0.5, 2.5],
      "expand_terms": ["widespread", "expansive", "covering", "encompassing"],
      "contract_terms": ["focused", "narrow", "tight", "concentrated"],
      "particle_property": "shape.scale (Box), radius"
    },
    "temporal": {
      "description": "Duration of effect",
      "range_seconds": [0.5, 8.0],
      "extend_terms": ["lingering", "persistent", "sustained", "enduring"],
      "shorten_terms": ["brief", "momentary", "flash", "quick"],
      "particle_property": "main.duration, lifetime"
    },
    "density": {
      "description": "Particle count / emission rate",
      "range": [0.3, 3.0],
      "dense_terms": ["swarm", "deluge", "barrage", "myriad", "countless"],
      "sparse_terms": ["scattered", "few", "sprinkling", "isolated"],
      "particle_property": "emission.rateOverTime, maxParticles"
    },
    "velocity": {
      "description": "Speed of particle movement",
      "range": [0.2, 2.5],
      "fast_terms": ["rapid", "swift", "blazing", "hurricane", "onslaught"],
      "slow_terms": ["drifting", "lazy", "calm", "tranquil", "floating"],
      "particle_property": "main.startSpeed, velocityOverLifetime"
    },
    "force": {
      "description": "Physics impulse (shockwave only)",
      "range": [3.0, 15.0],
      "powerful_terms": ["devastating", "cataclysmic", "tremendous", "violent"],
      "gentle_terms": ["soft", "delicate", "nudging", "subtle"],
      "particle_property": "AddExplosionForce (physics)"
    },
    "luminance": {
      "description": "Brightness/glow intensity",
      "range": [0.5, 2.0],
      "bright_terms": ["blazing", "radiant", "glowing", "brilliant", "dazzling"],
      "dim_terms": ["fading", "dim", "waning", "subdued", "murky"],
      "particle_property": "main.startColor alpha, material emission"
    }
  }
}
```

### 2.2 Color Semantic Mapping

| Semantic Intent | Hex Value | Use Case |
|-----------------|-----------|----------|
| Electric Blue | #5999FF | Lightning, storm, tech |
| Cyan/Teal | #38F2E6 | Water, ice, calm |
| Crimson Red | #FF4D4D | Fire, danger, passion |
| Emerald Green | #59FF73 | Nature, healing, growth |
| Golden Yellow | #FFE640 | Light, hope, energy |
| Amber Orange | #FF9E33 | Warmth, warning, craft |
| Violet Purple | #BD73FF | Magic, mystery, wisdom |
| Silver White | #F2F8FF | Purity, spirit, ethereal |
| Obsidian Black | #1F1F29 | Void, shadow, death |

---

## Phase 3: Semantic Clusters for Creative Invocation

### 3.1 Elemental Cluster

| Element | Preferred Effects | Creative Invocations |
|---------|------------------|---------------------|
| **Fire/Flame** | FireBall, ElectricalSparks, BigExplosion | "ignite", "inferno", "blaze", "ember", "scorch", "flame" |
| **Ice/Frost** | IceLance, GroundFog, RainEffect | "freeze", "chill", "frost", "glacier", "cold", "shatter" |
| **Storm/Lightning** | LightnigStormCloud, ElectricalSparks | "thunder", "bolt", "crack", "tempest", "surge", "voltage" |
| **Water** | RainEffect, Shower, GoopSprayEffect | "drown", "torrent", "deluge", "mist", "spray", "flood" |
| **Earth** | DustMotesEffect, SandSwirlsEffect, EarthShatter | "quake", "rumble", "dust", "crumble", "stone" |
| **Nature** | FireFlies, GroundFog, DustMotesEffect | "bloom", "grow", "flourish", "verdant", "life" |

### 3.2 Emotional/Aesthetic Cluster

| Mood | Suggested Parameters | Color Suggestion |
|------|---------------------|-------------------|
| **Menacing** | high force (10-15), wide radius, fast | Red/Orange/Black |
| **Peaceful** | low density, slow velocity, gentle | Cyan/Green/Silver |
| **Mystical** | medium density, medium duration, glow | Purple/Gold/White |
| **Chaotic** | high density, random velocity, multi-color | Rainbow/Red/Orange |
| **Tragic** | low luminance, short duration, fade | Black/Blue/Gray |
| **Triumphant** | high luminance, long duration, wide | Gold/White/Bright Green |

### 3.3 NPC-Specific Power Vocabularies

**Storm Oracle:**
```
primary: weather, lightning, storms, wind, rain
creative: "call the tempest", "unleash fury", "channel the storm", 
          "rain destruction", "electrify the air", "wind blast"
parameters_to_consider: radius (storm spread), duration (persistence), 
                         luminance (lightning flash brightness)
```

**Forge Keeper:**
```
primary: fire, sparks, heat, metal, craft
creative: "forge anew", "metal storm", "ignite the forge",
          "sparks fly", "molten cascade", "heat wave"
parameters_to_consider: density (sparks count), velocity (spray speed),
                         force (blast impact)
```

**Archivist:**
```
primary: knowledge, memory, mist, spirits, light
creative: "whispers of the past", "memory fog", "guide the way",
          "ancient lights", "echo of wisdom", "ethereal mist"
parameters_to_consider: duration (lingering), luminance (ghostly glow),
                         spatial (fog spread)
```

---

## Phase 4: LLM Prompt Structure

### 4.1 System Prompt Addition

Add this to your NPC persona prompts:

```
=== EFFECT SYSTEM REFERENCE ===
You have access to a dynamic particle effect system. When describing 
NPC actions or responding to player requests, you may invoke effects 
to enhance immersion.

PARAMETER DIMENSIONS (use creatively):
- magnitude: size/scale (0.6x to 2.0x)
- spatial: area of effect (radius/width)
- temporal: duration (0.5s to 8s)
- density: particle count (sparse to overwhelming)
- velocity: speed (drifting to rapid)
- luminance: brightness (dim to blazing)
- force: physics impact (gentle to devastating)

ELEMENTAL VOCABULARY:
- Fire effects: ignite, blaze, inferno, ember, scorch, molten
- Ice effects: freeze, frost, chill, glacier, shatter
- Storm effects: thunder, bolt, tempest, surge, voltage
- Water effects: torrent, deluge, mist, spray, flood
- Nature effects: bloom, flourish, verdant, growth
- Mystic effects: ethereal, ghostly, ancient, whisper

EXAMPLES:
- "minor spark" = weak magnitude, sparse density
- "devastating fire storm" = max magnitude, high density, wide spatial
- "gentle falling snow" = weak magnitude, slow velocity, white color

When you describe actions, weave in effect invocations naturally.
Use color words to tint effects: blue, red, green, gold, purple, etc.
Use intensity words: massive, subtle, lingering, blazing, faint, etc.
```

### 4.2 Parameter Extraction Integration

Ensure your `ParticleParameterExtractor` receives the LLM's creative descriptions. The current implementation handles:
- ✅ Size (bigger/larger/huge vs tiny/smaller)
- ✅ Radius (wider/broader/expand vs narrow/tight)
- ✅ Duration (longer/lasting/sustain vs brief/short)
- ✅ Speed (faster/rapid/swift vs slow/slower)
- ✅ Count (more/many/dense vs fewer/less/sparse)
- ✅ Force (stronger/powerful/violent vs weaker/gentle)
- ✅ Intensity (bright/brighter/intense/glow vs dim/dimmer)
- ✅ Color (named colors extracted)

### 4.3 Missing Semantic Mappings to Add

```csharp
// Add to ParticleParameterExtractor.cs

// New: Elemental type extraction
private static readonly (string[] terms, string elementalType)[] s_ElementalMappings = new[]
{
    (new[] {"fire", "flame", "blaze", "inferno", "ember", "scorch", "molten"}, "fire"),
    (new[] {"ice", "frost", "freeze", "chill", "glacier", "cold"}, "ice"),
    (new[] {"lightning", "thunder", "bolt", "storm", "tempest", "surge", "electric"}, "storm"),
    (new[] {"water", "rain", "torrent", "deluge", "mist", "flood", "drown"}, "water"),
    (new[] {"nature", "forest", "growth", "bloom", "verdant", "life"}, "nature"),
    (new[] {"magic", "mystical", "ethereal", "ghostly", "ancient", "spirit"}, "mystic"),
};

// New: Emotional intensity
private static readonly (string[] terms, float multiplier)[] s_EmotionalIntensity = new[]
{
    (new[] {"menacing", "threatening", "dangerous", "fearful"}, 1.4f),
    (new[] {"peaceful", "calm", "tranquil", "serene"}, 0.6f),
    (new[] {"joyful", "triumphant", "celebration", "victory"}, 1.2f),
    (new[] {"sad", "tragic", "mournful", "grief"}, 0.7f),
    (new[] {"chaotic", "madness", "wild", "unpredictable"}, 1.5f),
};
```

---

## Phase 5: Expanded NPC Profile Keywords

### 5.1 Recommended Profile Updates

Add these keyword categories to each NPC's profile:

**Storm Oracle - Add to Rain Keywords:**
```
"call storm", "storm call", "weather change", "wind gust", 
"lightning strike", "thunder roll", "rain down", "deluge",
"tempest fury", "wind blast", "storm surge"
```

**Storm Oracle - Add to Shockwave Keywords:**
```
"thunder clap", "blast wave", "shock surge", "pressure wave",
"air burst", "force push", "kinetic blast"
```

**Forge Keeper - Add to Fire Prefab Keywords:**
```
"forge flame", "metal storm", "sparks fly", "ignite",
"fire cascade", "molten", "heat blast", "ember storm"
```

**Archivist - Add to Fog Prefab Keywords:**
```
"memory mist", "whisper fog", "ancient mist", "spirit fog",
"wisdom cloud", "ethereal veil", "misty memory"
```

---

## Phase 6: Implementation Checklist

### Priority 1: Quick Wins
- [ ] Add system prompt reference to persona prompts
- [ ] Expand NPC profile keywords with creative alternatives
- [ ] Test 3-5 creative invocations per NPC

### Priority 2: Core Enhancement
- [ ] Add elemental type extraction to `ParticleParameterExtractor`
- [ ] Add emotional intensity mapping
- [ ] Create prefab power semantic tagging

### Priority 3: Advanced Features
- [ ] Integrate additional ParticlePack prefabs (BigExplosion, WildFire, etc.)
- [ ] Add "combo" detection (e.g., fire + explosion = BigExplosion)
- [ ] Build visual demo scene with all effects labeled

---

## Appendix: Example Creative Dialogue Prompts

**Player:** "Show me something impressive!"

**Storm Oracle (current):** "Very well." → rain burst  
**Storm Oracle (enhanced):** "Witness the tempest's wrath!" → Lightning Storm, wide radius, high luminance, max magnitude, electric blue color

**Player:** "Can you create a peaceful atmosphere?"

**Archivist (current):** *no effect triggered*  
**Archivist (enhanced):** "Let ancient memories surround you." → Ground Fog, wide spatial, slow velocity, cyan/white color, peaceful emotional tint

**Player:** "I'm in danger! Help!"

**Forge Keeper (current):** "Stay behind me." → small shield  
**Forge Keeper (enhanced):** "The forge protects its own!" → Fire Blast + Shield Bubble combo, high force, red/orange, devastating intensity

---

## References

- `DialogueSceneEffectsController.cs` - Effect application logic
- `ParticleParameterExtractor.cs` - Parameter extraction from text
- `EFFECT_CAPABILITY_MATRIX.md` - Current capability documentation
- `NpcDialogueProfile` assets - Per-NPC keyword configuration
- `Behavior_Scene.unity` - Prefab power template registry

---

*This plan provides the semantic framework your LLMs need to make creative, informed particle effect decisions. Start with Phase 6 Priority 1 for immediate improvements, then iterate toward the full matrix.*
