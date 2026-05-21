# Water Planar Reflection — Optimization & Improvement Plan

Date: 2026-05-21
Status: not started — proposed after initial planar reflection landed.

## Context

The initial planar-reflection ocean is functional (see assembly `OpenFarCry.Rendering.Water` under `Assets/Scripts/Rendering/Water/`, shader `Assets/Shaders/Water/FarCryWater.shader`, and `FcLevelSceneBuilder.BuildWaterPlane`). It uses URP 17.4's `RenderPipeline.SubmitRenderRequest(SingleCameraRequest)` from a `ScriptableRendererFeature` subscribed to `RenderPipelineManager.beginCameraRendering`, with an overridden `worldToCameraMatrix` (because reflected basis is left-handed → quaternion cannot represent), oblique near-plane clip, `GL.invertCulling`, and a tier-driven settings asset.

What is currently weak:

- The reflection camera renders **every frame, full scene**, even when water is offscreen, even when nothing moved. On large Far Cry levels with 60+ vegetation types, brushes and decals the cost is roughly **a second main camera per game camera**.
- No stencil masking — reflection RT is sampled blindly even where the screen has no water; cost-irrelevant but quality leaks (sky pollution edges).
- No frustum/visibility cull of the water surface itself — if the camera looks away from the sea, mirror cam still renders.
- Single global RT, single mirror cam — breaks split-screen / Editor multi-camera setups.
- Reflection cam inherits shadow distance, LOD bias, and frame settings from main cam; no per-reflection downgrade beyond shadows + post toggle.
- No SSPR (screen-space) fallback for cases where mirror cam is cheap but planar geometry is occluded.
- Visual quality is sparse: no Gerstner / FFT waves, no caustics, no foam (intersection or shoreline), no underwater post, no Beer-Lambert color absorption, no proper sun specular GGX.
- No `Volume` integration — per-area control (different ocean color in different missions) is impossible without manual settings swaps.
- No profiler markers — current cost invisible in Frame Debugger / Profiler.
- No editor preview of the reflection RT — debugging is blind.

Intended outcome: keep what works, drive per-frame cost down by 40–70% on coastal Far Cry levels (Training, Pier, Catacombs), unlock visual fidelity (caustics, foam, underwater), and make multi-camera / split-screen viable. Cross-platform robustness improves (mobile / integrated GPU path becomes meaningful, not just a stub).

## Current State Snapshot

Files in scope (do not touch the asmdefs):

- `Assets/Scripts/Rendering/Water/FcWaterSettings.cs`
- `Assets/Scripts/Rendering/Water/FcWaterQualityTier.cs`
- `Assets/Scripts/Rendering/Water/FcWaterSurface.cs` (+ `FcWaterRegistry`, `FcReflectionCameraTag`)
- `Assets/Scripts/Rendering/Water/FcPlanarReflectionRendererFeature.cs`
- `Assets/Scripts/Rendering/Water/FcWaterRuntimeBootstrap.cs`
- `Assets/Scripts/Rendering/Water/Editor/FcWaterSettingsEditor.cs`
- `Assets/Scripts/Rendering/Water/Editor/FcWaterSpawnMenu.cs`
- `Assets/Scripts/Rendering/Water/Editor/FcWaterLayerInstaller.cs`
- `Assets/Shaders/Water/FarCryWater.shader`
- `Assets/Resources/FcWaterSettings.asset`

## Industry Best Practices — Reference

Distilled from URP/HDRP samples, Unity Boat Attack project, Crytek tech-blog (CryEngine 3 ocean), Frostbite ocean talks (Wakass), `Acerola` planar reflection write-ups, Inigo Quilez water shader notes, and Sea of Thieves GDC 2018.

1. **Mirror camera per main camera, RT per camera** — never share a single global RT across Game + Scene view + split-screen.
2. **Frustum-cull water surface first** — if the water plane bounding rect is outside the main cam frustum, skip reflection render entirely.
3. **Stencil-mask water area** — render water to stencil during the main pass; reflection RT only needs to be valid where stencil = 1. Lets aggressive blur / artifact masking without polluting sky.
4. **Cull water LAYER from mirror cam** — recursive self-reflection is the #1 visual artifact (already done).
5. **Tier-down LODs in reflection** — `QualitySettings.lodBias` halved, shadow distance ¼, no skinned animation update for off-camera, no decals, no transparent queue.
6. **Half / quarter-res RT with bilateral upscale** — typical AAA targets ½ res with depth-aware upscale; mirror cam never benefits from main-cam resolution.
7. **HDR RT format `RGB111110Float`** — already done; sweet spot HDR vs bandwidth.
8. **Disable MSAA on reflection RT** — already implicitly (we set `antiAliasing = 1`).
9. **Disable post processing & shadows in mirror cam** — already done; further: disable volumetric fog, lens flares, lights cookies.
10. **Skip mirror cam render when main cam is below water plane** — already done; complement with underwater post.
11. **Temporal reuse — re-render only when something moved** — last camera matrix + last-frame world bounds; skip if delta < ε.
12. **Mip-bias for roughness** — already done (trilinear, `_Roughness` shader prop, `autoGenerateMips`).
13. **Screen-space planar reflections (SSPR) as fallback** — Ray-marches the existing scene color/depth buffer; near-zero cost. Use as a fallback when planar fails (e.g., camera too close to water, oblique clip clipping everything) or as a separate quality tier between Medium and High.
14. **Pre-bake static reflection to Cubemap** — for static cameras (cinematics, menus) bake once, sample as the fallback.
15. **Multi-surface support** — instead of `FcWaterRegistry.FindClosest`, render one RT per visible surface (rare on Far Cry, but pier + interior pool scenes exist).
16. **Volume system integration** — per-mission ocean color/roughness/distortion via URP `VolumeComponent` overrides, blending across triggers.
17. **Underwater post-process** — when camera Y < waterY, apply blue tint, blur, caustics; switch water shader to "from below" branch (cull front faces, refraction goes outward not inward).
18. **Caustics projection** — light cookie or screen-space caustic decal driven by Gerstner/FFT height map.
19. **Foam masks** — three sources combined: intersection depth-difference (shoreline foam), wave crest height threshold (whitecaps), object intersection (wake foam).
20. **Gerstner waves (vertex offset) + tessellation** — 4-5 stacked Gerstner waves give convincing ocean swell; tessellation in URP 17 via `UnityTess` HLSL include or compute-driven mesh.
21. **Beer-Lambert depth absorption** — refraction color tinted as `color * exp(-extinction * depth)` per channel — physically correct, kills the "bright cyan tropical bay" cliché.
22. **GGX sun specular** — replace current Blinn-Phong sun glint with GGX BRDF using `_Roughness`; ties roughness slider to both reflection blur and sun glint width.
23. **`Volume Sample Reflection Probe` fallback respects probe blending** — current shader does single probe sample; better path uses URP `EvaluateBRDF`.
24. **Profiler markers** — wrap mirror cam render in `ProfilingScope("FcWater.PlanarReflection")` for Frame Debugger.
25. **Editor preview** — `FcWaterSettingsEditor` shows the live RT as a thumbnail (`EditorGUI.DrawPreviewTexture`).
26. **Disable reflection in Editor Scene view by default** — Scene cam moving constantly = constant reflection re-render; opt-in toggle.
27. **`cullingMatrix` override required** (already done) — without it culling uses transform, transform basis ≠ overridden view matrix.
28. **Format fallback** — `SystemInfo.SupportsRenderTextureFormat(RGB111110Float) ? : ARGB32`.
29. **Reflection RT pooling** — when multiple cams render, share a pool keyed on `(cam, resolution, hdr)` instead of leaking RTs.
30. **`Camera.CopyFrom` pitfalls** — copies projection but not transform; copies `cullingMatrix` only if overridden; reset both each frame.

## Phase Board

Phases ordered by ROI (perf wins first, then visual, then tooling). Each is independent and revertable.

- [ ] P1 — Frustum cull + visibility skip (cheapest, biggest win on inland missions)
- [ ] P2 — Temporal reuse (skip render when main cam stationary)
- [ ] P3 — Per-camera RT pool + per-camera mirror cam
- [ ] P4 — Reflection cam LOD/shadow/decal downgrade
- [ ] P5 — Stencil mask water area
- [ ] P6 — SSPR alternative path (new tier between Medium and High)
- [ ] P7 — Volume system integration (per-area overrides)
- [ ] P8 — Underwater rendering path (post + shader branch)
- [ ] P9 — Shoreline + intersection + crest foam
- [ ] P10 — Beer-Lambert depth absorption + GGX sun specular
- [ ] P11 — Gerstner waves (vertex offset, no tess)
- [ ] P12 — Caustics projection
- [ ] P13 — Editor preview + profiler markers + Scene-view opt-in
- [ ] P14 — Reflection bake mode (static cubemap capture for cinematics)
- [ ] P15 — Tessellation displacement (highest fidelity, optional)

---

## P1 — Frustum Cull + Visibility Skip

**Goal**: Skip reflection render when no water surface is visible to the source camera.

**Files**: `FcPlanarReflectionRendererFeature.cs`, `FcWaterSurface.cs`.

**Tasks**:
- [ ] Add `Bounds WorldBounds` to `FcWaterSurface` (computed from MeshRenderer.bounds at OnEnable, recomputed on transform change).
- [ ] In `OnBeginCameraRendering`, build frustum planes via `GeometryUtility.CalculateFrustumPlanes(srcCam)`; call `GeometryUtility.TestPlanesAABB(planes, surface.WorldBounds)` — if false, return early without touching mirror cam.
- [ ] Combine with existing "no surface registered" + "camera below water" guards.

**Success criteria**: On an interior mission (no visible water), reflection render count per main camera = 0 in Frame Debugger. On a coastal mission with camera looking away from sea, count = 0.

---

## P2 — Temporal Reuse

**Goal**: Skip rebuilding the reflection RT when neither the camera nor any contributing transform has changed.

**Files**: `FcPlanarReflectionRendererFeature.cs`, new `FcReflectionCache.cs`.

**Tasks**:
- [ ] Per source camera, cache last-frame `worldToCameraMatrix` + `projectionMatrix` hash.
- [ ] If current frame matches within ε (< 0.001 in matrix entries) AND `Time.frameCount > lastRenderFrame`, skip render and let the global `_FcWaterReflectionTex` retain previous content.
- [ ] Expose `FcWaterSettings.TemporalReuse = true` toggle (default on); off for debugging.
- [ ] Optional later: track per-frame dirty flags for scene contents (any FcWaterSurface moved, lights changed) — out of scope for first pass.

**Success criteria**: Camera idle in main menu / pause → reflection render count = 0 after first frame. Camera moving = 1 per frame as today.

---

## P3 — Per-Camera RT Pool + Per-Camera Mirror Cam

**Goal**: Make Scene view + Game view + split-screen render correct, non-flickering reflections without sharing state.

**Files**: `FcPlanarReflectionRendererFeature.cs`.

**Tasks**:
- [ ] Replace single `_reflectionCam` + `_rt` fields with `Dictionary<Camera, (Camera mirrorCam, RenderTexture rt)>` keyed by source camera.
- [ ] On `OnBeginCameraRendering`, get-or-create the entry for `srcCam`.
- [ ] Garbage collect entries whose source cam was destroyed (check `cam == null`).
- [ ] Bind the per-camera RT to `_FcWaterReflectionTex` via `CommandBuffer.SetGlobalTexture` scoped to the source camera's render pass (use `ScriptableRenderPass` to scope it) — not a process-wide `Shader.SetGlobalTexture` which would clobber across cameras.
- [ ] On `Dispose`, iterate and release all entries.

**Success criteria**: Open Scene + Game view; both show their own correct reflections. No flicker between frames where the "other" camera renders.

---

## P4 — Reflection Cam LOD/Shadow/Decal Downgrade

**Goal**: Render reflection cheaper than main cam regardless of tier.

**Files**: `FcPlanarReflectionRendererFeature.cs`, `FcWaterSettings.cs`.

**Tasks**:
- [ ] Settings: add `ReflectionLodBias` (default 2.0), `ReflectionMaxShadowDistance` (default 0 = no shadows), `ReflectionMaximumLODLevel` (default 1), `ReflectionExcludesDecals` (default true).
- [ ] In `CopyCameraData`, after `CopyFrom`, set `_reflectionCam.useOcclusionCulling = false`, scale lod bias on URP `UniversalAdditionalCameraData` if available else `QualitySettings.lodBias *= 0.5f` wrap.
- [ ] Set `dstData.requiresDepthTexture = false`, `dstData.requiresOpaqueTexture = false`.
- [ ] Optional: subscribe a `Volume` override for the reflection cam scope to disable expensive `VolumeComponent`s (fog density, AO, SSGI).

**Success criteria**: Frame Debugger shows reflection cam draw call count ~50–70% of main cam draw count on the same scene.

---

## P5 — Stencil Mask Water Area

**Goal**: Avoid sky-edge bleed and let aggressive blur not pollute outside the water silhouette.

**Files**: `FarCryWater.shader`, new `FcWaterStencilPass.cs`, `FcPlanarReflectionRendererFeature.cs`.

**Tasks**:
- [ ] Add a stencil write pass on the water shader: `Stencil { Ref 16 Comp Always Pass Replace }` in a dummy depth-only first sub-pass.
- [ ] Optionally clip the planar reflection sampling to stencil = 16 via a second `ScriptableRenderPass` that re-samples reflection RT into a scratch RT only where stencil matches.
- [ ] Document the chosen stencil bit (16) in CLAUDE.md.

**Success criteria**: Above the horizon line where sky meets water, no sky-color smearing through to water reflection.

---

## P6 — SSPR Path

**Goal**: Add a screen-space planar reflection mode as a quality tier (between Medium and High) and as a fallback when planar fails.

**Files**: new `Assets/Scripts/Rendering/Water/FcSsprRendererFeature.cs`, `FcSsprCompute.compute`, `FarCryWater.shader` (new keyword `_FC_WATER_SSPR_ON`).

**Tasks**:
- [ ] Implement standard SSPR algorithm: project each opaque pixel to its reflection on the water plane, write color into RT via `InterlockedMin` on a uint atomic buffer encoding (depth, color).
- [ ] Resolve atomic buffer to RGB RT; pass to shader.
- [ ] New tier `MediumPlus` (or rename Medium to "Probe", add "Sspr" tier).
- [ ] Shader keyword branch reads SSPR RT when `_FC_WATER_SSPR_ON`.
- [ ] Documented limitation: SSPR cannot reflect what's offscreen (no horizon mountains if camera tilts away).

**Success criteria**: On `Medium-Sspr`, water shows screen-space reflection of visible scene with no extra cam render; frame time within 0.3 ms on RTX 3060.

---

## P7 — Volume System Integration

**Goal**: Per-area ocean color/roughness/distortion via URP Volume system.

**Files**: new `FcWaterVolumeComponent.cs`, `FcWaterRuntimeBootstrap.cs`.

**Tasks**:
- [ ] `FcWaterVolumeComponent : VolumeComponent` exposing `ColorParameter ShallowColor`, `ColorParameter DeepColor`, `ClampedFloatParameter Roughness`, `ClampedFloatParameter DistortionStrength`, etc.
- [ ] In `FcWaterRuntimeBootstrap.Update` (move out of pure `[InitializeOnLoadMethod]` to per-frame), sample `VolumeManager.instance.stack.GetComponent<FcWaterVolumeComponent>()`; push values via `Shader.SetGlobal*` if overridden.
- [ ] Settings asset stays as the fallback / default when no Volume overrides.

**Success criteria**: Drop a `Box Volume` near a Far Cry lagoon, set `ShallowColor` override → ocean color blends smoothly when camera crosses the trigger.

---

## P8 — Underwater Rendering Path

**Goal**: Visual coherence when camera dips below the water plane.

**Files**: `FarCryWater.shader`, new `FcUnderwaterPostFeature.cs`, new shader `FcUnderwaterPost.shader`.

**Tasks**:
- [ ] Water shader: add `Cull Off` and a `_IsUnderwater` keyword branch driven globally by `FcWaterRuntimeBootstrap`; from-below path uses inverted Fresnel, no planar reflection, samples skybox via cubemap below-horizon.
- [ ] Post-process feature: full-screen blit applied when source cam below water plane; tints blue, applies depth-based fog, optional caustics overlay.
- [ ] `FcWaterRuntimeBootstrap` per-frame sets global keyword `_FC_WATER_UNDERWATER_ON` based on `Camera.main.transform.position.y vs nearestSurface.WaterLevelY`.

**Success criteria**: Camera dive under water → screen tints blue, water surface from below shows clean cull, no inverted refraction artifacts.

---

## P9 — Foam (Shoreline + Intersection + Crest)

**Goal**: Replace the current mirror-clean shoreline with proper foam masks.

**Files**: `FarCryWater.shader`, `FcWaterSettings.cs`.

**Tasks**:
- [ ] Add `_FoamTexture` (panning gradient noise, ships with skill default).
- [ ] Shoreline foam: `1 - saturate(waterDepth / _FoamShoreDistance)` masked over panning `_FoamTexture`.
- [ ] Intersection foam: object/water depth-difference threshold (cam-space).
- [ ] Crest foam: `saturate((waveHeight - _CrestThreshold) / _CrestSoftness)` — needs vertex height (delivered by P11 Gerstner).
- [ ] Settings exposes `FoamShoreDistance`, `FoamColor`, `FoamCrestThreshold`.

**Success criteria**: White foam line at beach contact; foam halo around partially-submerged brushes (rocks, boat hulls).

---

## P10 — Beer-Lambert Depth Absorption + GGX Sun Specular

**Goal**: Physically motivated underwater color + proper sun glint shape.

**Files**: `FarCryWater.shader`.

**Tasks**:
- [ ] Replace current `lerp(shallow, deep, depthT)` with per-channel exponential extinction: `tint = exp(-_ExtinctionRGB * waterDepth)`; final refraction = sceneColor * tint.
- [ ] Replace Blinn-Phong sun glint with GGX: `D = GGX(NdotH, roughness)`; tied to `_Roughness` slider.
- [ ] Add `_ExtinctionRGB` (vector) to settings + shader; default `(0.4, 0.18, 0.12)` for tropical.

**Success criteria**: Shallow water tints toward green/cyan, deep water absorbs to black, sun glint widens as `_Roughness` increases.

---

## P11 — Gerstner Waves (Vertex Offset)

**Goal**: Real ocean swell without tessellation.

**Files**: `FarCryWater.shader`, `FcLevelSceneBuilder.BuildWaterPlane` (subdivide plane), `FcWaterSettings.cs`.

**Tasks**:
- [ ] Replace primitive Plane (200 verts) with a tessellated grid (configurable `WaveGridResolution`, default 128×128).
- [ ] Vertex shader: 4 stacked Gerstner waves (direction, wavelength, amplitude, steepness) summed; output displaced position + analytical normal.
- [ ] Settings: `WaveCount` (1–8), per-wave `direction/wavelength/amplitude/steepness` array.
- [ ] Pass wave height to fragment shader for crest foam (P9).

**Success criteria**: Coastal mission shows visible swell motion at 60 fps on RTX 3060.

---

## P12 — Caustics Projection

**Goal**: Light-cookie-driven caustics on submerged surfaces.

**Files**: new `FcCausticsRendererFeature.cs`, `FcCaustics.shader`, settings additions.

**Tasks**:
- [ ] Renderer feature: full-screen pass after opaques, samples scene depth + reconstructs world pos, computes "depth below water plane", samples caustics texture (panning + chromatic offset for RGB split) projected from sun direction.
- [ ] Mask by depth (only visible up to `_CausticsMaxDepth`).
- [ ] Settings: `CausticsTexture` (default ships with skill), `CausticsScale`, `CausticsIntensity`, `CausticsMaxDepth`.

**Success criteria**: Underwater terrain shows moving caustic patterns near the surface.

---

## P13 — Editor Preview + Profiler Markers + Scene-View Opt-In

**Goal**: Make the system inspectable + don't burn cycles in Scene view.

**Files**: `FcWaterSettingsEditor.cs`, `FcPlanarReflectionRendererFeature.cs`.

**Tasks**:
- [ ] In `FcWaterSettingsEditor.OnInspectorGUI`, draw the active reflection RT via `EditorGUI.DrawPreviewTexture` (use reflection to access `_rt` or expose via static).
- [ ] Wrap `_reflectionCam.Render` (or `SubmitRenderRequest`) in `using (new ProfilingScope(cmd, _profilingSampler))` — `_profilingSampler = new ProfilingSampler("FcWater.PlanarReflection")`.
- [ ] Add `EnableInSceneView` toggle to settings (default `false`); guard the early-out.

**Success criteria**: Inspector shows live RT thumbnail. Frame Debugger shows the named scope. Scene view runs at 60 fps idle.

---

## P14 — Reflection Bake Mode

**Goal**: Capture static reflection to a Cubemap for cinematics / menus.

**Files**: new `Assets/Scripts/Rendering/Water/Editor/FcWaterBakeWindow.cs`, `FcWaterSettings.cs`.

**Tasks**:
- [ ] Editor window: "Bake Static Reflection" — captures the reflection cam to a 6-face cubemap at given resolution, writes to `Assets/Shaders/Water/<level>_Reflection.cubemap`.
- [ ] Settings: `UseBakedReflection` toggle + `BakedReflectionCubemap` slot.
- [ ] Renderer feature: when `UseBakedReflection`, skip mirror cam entirely; bind baked cube to `_FcWaterFallbackCubemap` instead.

**Success criteria**: After bake, mirror cam render count = 0 even on Ultra tier; reflection still looks correct for stationary scenes.

---

## P15 — Tessellation Displacement

**Goal**: Optional Ultra-tier vertex displacement via hardware tessellation.

**Files**: `FarCryWater.shader` (tess sub-shader), `FcWaterSettings.cs`.

**Tasks**:
- [ ] Add `#pragma require tessellation` sub-shader path with `_TessellationFactor` and `_TessellationDistance`.
- [ ] Vertex displacement uses Gerstner from P11 evaluated per generated vertex.
- [ ] Tier `Ultra` enables tessellation; `High` and below use baked vertex grid.

**Success criteria**: Ultra tier on RTX 3060 sustains 60 fps with visible per-pixel wave detail near camera.

---

## Out of Scope (Defer to Future Plans)

- FFT-based ocean (Tessendorf / Phillips spectrum) — overkill for Far Cry visual baseline.
- Multi-bounce reflections (reflection of reflection of reflection).
- Wet ground / wet decal system on terrain edges.
- AI integration (water as nav-mesh obstacle).
- Boat physics buoyancy from water surface height.

## Files

Existing files modified across phases:
- `Assets/Scripts/Rendering/Water/FcPlanarReflectionRendererFeature.cs`
- `Assets/Scripts/Rendering/Water/FcWaterSettings.cs`
- `Assets/Scripts/Rendering/Water/FcWaterSurface.cs`
- `Assets/Scripts/Rendering/Water/FcWaterRuntimeBootstrap.cs`
- `Assets/Scripts/Rendering/Water/Editor/FcWaterSettingsEditor.cs`
- `Assets/Shaders/Water/FarCryWater.shader`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs` (P11 grid subdivision)
- `CLAUDE.md` (P5 stencil bit reservation; P8 underwater path)

New files added:
- `Assets/Scripts/Rendering/Water/FcReflectionCache.cs` (P2)
- `Assets/Scripts/Rendering/Water/FcWaterVolumeComponent.cs` (P7)
- `Assets/Scripts/Rendering/Water/FcSsprRendererFeature.cs` + `.compute` (P6)
- `Assets/Scripts/Rendering/Water/FcUnderwaterPostFeature.cs` + shader (P8)
- `Assets/Scripts/Rendering/Water/FcCausticsRendererFeature.cs` + shader (P12)
- `Assets/Scripts/Rendering/Water/Editor/FcWaterBakeWindow.cs` (P14)

## Verification

Per-phase success criteria above. End-to-end after each phase:

1. **Coastal level (Training)** — visual check: planar reflection present, no flicker, no recursion.
2. **Inland level (Catacombs)** — no water visible → Frame Debugger confirms 0 mirror cam renders (P1).
3. **Cinematic** — set camera stationary → reflection render count drops to 0 after frame 1 (P2).
4. **Multi-camera** — open Scene + Game view → both show correct reflections, no flicker (P3).
5. **Underwater** — fly camera below water → blue tint, caustics, water from below looks clean (P8/P12).
6. **Frame time budget** — on RTX 3060 at 1080p: ≤ 1.2 ms for full Ultra path including all P9–P12 features.
7. **Mobile path** — Switch / iPhone GPU: Sspr tier (P6) sustains 30 fps; planar tier skipped automatically by tier resolver.
8. **Profiler** — `FcWater.PlanarReflection` scope visible in Frame Debugger and Profiler; per-phase cost attributable (P13).

## Recommendation

Land P1–P5 in one sprint — they are pure perf / robustness, no visual risk, all revertable. Then P7 (Volume) for content authoring flexibility. P8–P12 are visual polish, schedule them per level / artist need. P6 (SSPR) only if mobile/low-end targets become real. P14–P15 are nice-to-have, defer until base is stable.
