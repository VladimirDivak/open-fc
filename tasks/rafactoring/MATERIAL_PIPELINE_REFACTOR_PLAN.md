# Material Pipeline Refactor Plan

## Status

2026-05-14:
- in progress (`Phase 3`)
- done: gloss texture slot plumbed through runtime resolve/preload/cache key
- done: baseline material classification helper + initial EditMode tests
- done: classifier wired into `CgfMaterialBuilder` routing
- done: `Phase 0` docs correction in `~/Documents/farcry-sources/docs/materials.md` for `CRYSHADER` vs `AlphaTest`
- in progress: family-specific URP states (alpha blend/modulate/spec/gloss/glow/glass landed; plants cutout fallback landed; refinement pending)
- done: glass family (`templglasscm`) now uses explicit transparent + smoothness + reflection-friendly URP preset and opacity-driven base alpha
- done: glow path now attempts diffuse-alpha emission mask extraction when source texture is readable
- done: UV-scroll runtime hook added (`templtextureshiftt05` detection + `CgfUvScrollRuntime` attachment)
- done: texture preload dedupe now tracks `markNonReadable`, allowing separate readable/non-readable variants for the same virtual path
- done: `Phase 4` first-pass level material integration (override resolution + runtime metadata tagging)
- in progress: `Phase 5` validation/cleanup
- done: support matrix + known gaps doc added (`tasks/rafactoring/MATERIAL_PIPELINE_SUPPORT_MATRIX.md`)
- done: docs sync in `~/Documents/farcry-sources/docs/materials.md` (section "Статус реализации в open-farcry")
- done: minimal brush override path wired (`brush.lst MaterialOverride` -> scene component fields -> runtime resolver from `materials.xml`)
- done: brush override resolver supports fallback by `MaterialId` when override name is missing/unmatched
- done: brush runtime now attaches resolved level material/surface metadata to visual root + collider nodes (`FcLevelMaterialMetadata`)

## Goal

Bring Far Cry 1 material handling in `open-farcry` from basic texture binding to shader-aware runtime mapping with clear parity targets against `~/Documents/farcry-sources/docs/materials.md`.

Primary outcome:
- parsed Cry material semantics are carried through to Unity URP materials
- common Far Cry shader families render closer to source behavior
- level material metadata stops being diagnostics-only and becomes usable by runtime/editor import paths where needed

Non-goals for first pass:
- terrain shader rewrite
- physically correct PBR conversion
- full editor asset authoring workflow for every generated material
- perfect parity for rare shader variants before the common families are stable

## Current State

What already exists:
- CGF parser reads MTL `0x0744/0x0745/0x0746`, including `ShaderName`, `SpecLevel`, `SpecShininess`, `Opacity`, `AlphaTest`, `Flags`, and texture names.
- Runtime material resolution handles `MTL_MULTI`, table-index fallback, and per-submesh assignment.
- Runtime texture loading handles DDS/TGA/BMP/JPG and now resolves/preloads normal/spec/opacity/gloss with linear/sRGB split and readable/non-readable variants.
- Material builder is classifier-driven and covers common families (`modelcommon`, `bumpdiffuse`, `bumpspec*`, `plants*`, `alphablend`, `decalmodulate`, `decalglowselfillum`, `glasscm`, `textureshiftt05`, `nodraw`) with URP state mapping.
- Level `materials.xml` data participates in runtime through explicit brush override resolution + fallback by `MaterialId` and runtime material/surface metadata tagging.

Main gaps:
- parity is still approximation-only for complex Cry shader behavior (especially transparent/reflection cases).
- manual in-Unity visual validation matrix is not fully closed yet.

## Source Of Truth

Treat these as primary references:
- `~/Documents/farcry-sources/docs/materials.md`
- `~/Documents/farcry-sources/docs/textures.md`
- `CryCommon/CryHeaders.h`
- `Cry3DEngine/CryStaticModel.cpp`
- current importer behavior in `Assets/Scripts/Importer/Cgf/`

Important rule:
- before extending behavior, update local docs so they match the verified importer understanding around `CRYSHADER` vs `AlphaTest`; otherwise future work will keep reintroducing the wrong assumption.

## Workstreams

### 1. Normalize the material model

Extend the importer-facing material model so builder logic does not need to guess from raw fields repeatedly.

Add a derived classification layer, likely near `CgfMaterialImportService` or as a dedicated helper:
- `IsNoDraw`
- `IsCutout`
- `IsTransparentAlphaBlend`
- `IsAdditive`
- `IsTwoSided`
- `UsesVertexColors`
- `UsesGlossFromDiffuseAlpha`
- `UsesGlowFromDiffuseAlpha`
- `UsesTransparencyFromDiffuseAlpha`
- `UsesGlossTexture`
- `UsesReflection`
- `UsesUvScroll`
- shader family enum such as `Diffuse`, `BumpDiffuse`, `BumpSpec`, `BumpSpecGlossAlpha`, `Plants`, `Bark`, `Glass`, `GlowDecal`, `ModulateDecal`, `UvScroll`, `Unknown`

Result:
- one deterministic classification path
- builder becomes data-driven instead of scattered conditionals

### 2. Finish texture-slot semantics

Promote currently parsed-but-unused inputs into resolved runtime data.

Required additions:
- include `GlossTextureName` in resolved texture payload and preload logic
- optionally expose whether diffuse/opacity/spec/gloss textures have alpha channels when relevant to material decisions
- keep diffuse in sRGB; keep normal/spec/gloss/opacity in linear

Verify naming/path resolution for:
- `_ddn.dds`
- `_bump.dds`
- `_spec.dds`
- extension fallback candidates from source paths

Result:
- builder has all material inputs that docs consider common, not just base/normal/spec/opacity subset

### 3. Upgrade URP material mapping

Refactor `CgfMaterialBuilder` from a minimal mapper into a shader-family mapper.

Implement in priority order:
1. `templmodelcommon`
2. `templbumpdiffuse`
3. `templbumpspec_hp`, `templbumpspec`
4. `templbumpspec_hp_glossalpha`, `templmodelbumpspec_hp_glossalpha`
5. `templbumpspec_glossalpha`
6. `templplants`, `templplants1`, `templplantsbark`
7. `templalphablend`
8. `templdecalglowselfillum`
9. `templdecalmodulate`
10. `templglasscm`
11. `templtextureshiftt05`
12. safe fallback for unknown families

Expected URP behavior:
- set workflow/specular properties instead of leaving constant smoothness `0.2`
- map `SpecLevel` and `SpecShininess` into URP-friendly spec/smoothness values
- use diffuse alpha as smoothness/emission/transparency depending on shader family
- use gloss texture when present
- keep `AlphaTest` as the cutout trigger unless source verification proves another case
- keep `_Cull=Off` for two-sided and plant materials
- support explicit alpha-blend path separate from cutout path
- support multiply-style decal blend state
- support glass-specific transparent smooth material preset

Result:
- most common object materials move from “loads” to “looks broadly correct”

### 4. Special runtime behaviors

Some Cry shader families need component/runtime support, not only material flags.

Add opt-in hooks for:
- UV scroll component for `templtextureshiftt05`
- emission-mask generation from diffuse alpha for glow/self-illum materials
- reflection-probe-friendly setup for glass-like materials

Constraint:
- do not attach per-instance components blindly in low-level builder code if it can be avoided; prefer explicit post-process hooks from the object/brush/entity assembly layer

Result:
- shader families with simple behavioral requirements stop being hard-blocked on custom shaders

### 5. Integrate level material metadata

Decide where `materials.xml` and level material-library refs actually belong.

Questions to resolve in code:
- are level material names only validation data, or do they override CGF-resolved materials for brushes/entities?
- should brush `MaterialOverride` from `brush.lst` resolve through parsed level libraries?
- should surface/material metadata be attached to colliders/entities for later audio/physics/gameplay use?

Minimum first-pass integration:
- create a resolver that can map level material names to parsed metadata and texture refs
- wire this resolver into places where explicit level material override already exists
- keep fallback behavior if level material data is missing or incomplete

Result:
- `materials.xml` stops being dead parsed data

### 6. Tests and verification

Add focused EditMode coverage before broad refactors land.

Needed tests:
- parser reads gloss/spec/alpha-test fields correctly for `0x0745/0x0746`
- material classification for representative shader names and flag combinations
- builder maps `templbumpdiffuse` to normal-enabled opaque URP material
- builder maps `templbumpspec_*` to specular/smoothness configuration
- builder maps plants to cutout + two-sided
- builder maps alpha-blend and additive as separate states
- builder maps nodraw without producing visible render state
- material cache key changes when gloss/spec inputs differ
- preload includes gloss textures once that slot is supported
- level material override resolution works for known `materials.xml` entries

Manual Unity validation set:
- opaque metal/rock prop
- glossy prop using diffuse alpha gloss
- vegetation with cutout and vertex colors
- glass object
- glow/decal case
- UV-scroll water/animated surface if sample asset exists
- brush with proxy/nodraw submeshes still stripped from visuals

## Execution Order

### Phase 0. Documentation correction
- update local material docs for verified `CRYSHADER` meaning
- note any remaining uncertainty explicitly

### Phase 1. Data plumbing
- add gloss texture resolution/preload
- add classification helper/types
- extend cache key inputs only where behavior truly differs

### Phase 2. Core builder rewrite
- refactor `CgfMaterialBuilder` into family-based mapping
- implement specular/smoothness handling
- keep fallback path stable for unknown materials

### Phase 3. Special-case families
- plants/bark
- alpha blend
- glass
- glow/modulate decals
- UV scroll hook

### Phase 4. Level material integration
- resolve explicit material overrides through parsed level metadata
- attach surface/material metadata where downstream systems can consume it

### Phase 5. Validation and cleanup
- add/adjust EditMode tests
- verify runtime samples in Unity
- update docs to reflect the final supported matrix and known gaps

## Suggested File Targets

Likely primary files:
- `Assets/Scripts/Importer/Cgf/CgfData.cs`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`
- `Assets/Scripts/Level/Data/FcLevelSupplementLoader.cs`
- `Assets/Scripts/Level/...` material override call sites
- `Assets/Scripts/Importer/Tests/Editor/CgfMaterialTextureBindingTests.cs`
- new EditMode tests for classification/builder behavior

## Risks

- URP Lit has limits; some Cry shader behavior will remain approximation-only.
- Diffuse alpha is overloaded in Cry; wrong family classification will produce visibly wrong smoothness/emission/transparency.
- Level material libraries may not map 1:1 to CGF materials; override precedence needs explicit rules.
- Material changes can silently break proxy/nodraw collider expectations if visual stripping and collider extraction drift apart.

## Done Criteria

Consider this refactor done for intended scope when:
- common Cry shader families listed above map deterministically to stable URP material states
- spec/gloss data materially affects the produced Unity material
- gloss texture slot is loaded, cached, and test-covered
- `materials.xml` participates in at least explicit override resolution
- docs no longer contradict importer behavior on cutout semantics
- focused EditMode tests cover the new routing
- Unity visual validation is completed for representative assets
