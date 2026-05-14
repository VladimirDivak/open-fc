# Material Pipeline Support Matrix

Date: 2026-05-14

## Scope

This document summarizes the current runtime support level for Far Cry material families in `open-farcry`, based on importer + level integration changes delivered in the material pipeline refactor.

Legend:
- `done`: implemented and test-covered in EditMode (or covered by deterministic code path)
- `partial`: implemented as approximation, with known behavior gaps
- `pending`: not implemented in current pass

## Shader Family Coverage

| Cry shader family | Runtime mapping status | Current Unity mapping |
| --- | --- | --- |
| `templmodelcommon` | done | URP Lit opaque baseline, base color/base map |
| `templbumpdiffuse` | done | URP Lit opaque + normal map |
| `templbumpspec`, `templbumpspec_hp` | done | URP Lit spec workflow + spec color + smoothness mapping |
| `templbumpspec*_glossalpha`, `templmodelbumpspec_hp_glossalpha` | done | Spec workflow + smoothness-from-alpha / gloss map routing |
| `templplants`, `templplants1`, `templplantsbark` | done | Cutout fallback + two-sided culling off |
| `templalphablend` | done | Transparent alpha-blend state + opacity-driven base alpha |
| `templdecalmodulate` | done | Modulate blend state (`DstColor * Src`) |
| `templdecalglowselfillum` | done | Emission enabled; diffuse-alpha mask extraction when readable |
| `templglasscm` | partial | Transparent + smoothness=1 + reflection-friendly URP flags; approximation, fallback alpha=0.35 when source opacity is missing |
| `templtextureshiftt05` | done | Classified + runtime UV scroll component attached |
| `nodraw` | done | Invisible material path + visual stripping/collider parity paths |
| Unknown shaders | done | Safe fallback to URP Lit baseline |

## Texture Slot Coverage

| Slot | Status | Notes |
| --- | --- | --- |
| Diffuse/Base | done | sRGB |
| Normal | done | linear |
| Specular | done | linear |
| Opacity | done | linear, used as fallback base texture when base missing |
| Gloss | done | linear, preload + dedupe + cache key wired |

Additional behavior:
- Texture preload dedupe distinguishes `markNonReadable` variants.
- Glow path can request readable diffuse for emission-mask generation.

## Level Material Integration Coverage

| Capability | Status | Notes |
| --- | --- | --- |
| `brush.lst` `MaterialOverride` resolution | done | Resolved through `materials.xml` by name/fullname |
| Fallback by `MaterialId` | done | Uses parsed materials array index |
| Fallback texture-path material build | done | Override string can be treated as texture path |
| Surface type metadata resolution | done | Surface matched by material name/fullname or direct override key |
| Runtime metadata attachment | done | `FcLevelMaterialMetadata` on brush visual root + collider nodes |

## Known Gaps

1. Transparent/reflection behavior remains URP approximation (no Cry custom shader parity).
2. Material override is currently brush-focused; non-brush entity override policy is not expanded in this pass.
3. Final visual parity still requires manual in-Unity scene validation across representative assets.

## Validation Checklist

- EditMode tests:
  - `CgfMaterialTextureBindingTests`
  - `CgfMaterialClassifierTests`
  - `CgfGeometryPreloadDedupeTests`
  - `FcLevelMaterialOverrideServiceTests`
- Manual Unity checks:
  - opaque rock/metal prop
  - glossalpha prop
  - vegetation cutout/two-sided
  - glass object
  - glow decal
  - UV-scroll sample surface
