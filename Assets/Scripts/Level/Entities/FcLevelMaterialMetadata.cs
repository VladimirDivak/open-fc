using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Runtime metadata tag for level material/surface resolution on instantiated geometry.
    [DisallowMultipleComponent]
    public sealed class FcLevelMaterialMetadata : MonoBehaviour
    {
        [SerializeField] string _resolutionSource;
        [SerializeField] string _requestedOverrideName;
        [SerializeField] int _requestedMaterialId = -1;
        [SerializeField] string _materialName;
        [SerializeField] string _materialFullName;
        [SerializeField] string _materialShader;
        [SerializeField] bool _hasSurfaceType;
        [SerializeField] int _surfaceTypeId = -1;
        [SerializeField] string _surfaceTypeName;
        [SerializeField] string _surfaceTypeMaterial;
        [SerializeField] string _surfaceTypeDetailObject;
        [SerializeField] string[] _slotResolutionDiagnostics;

        public string ResolutionSource => _resolutionSource;
        public string RequestedOverrideName => _requestedOverrideName;
        public int RequestedMaterialId => _requestedMaterialId;
        public string MaterialName => _materialName;
        public string MaterialFullName => _materialFullName;
        public string MaterialShader => _materialShader;
        public bool HasSurfaceType => _hasSurfaceType;
        public int SurfaceTypeId => _surfaceTypeId;
        public string SurfaceTypeName => _surfaceTypeName;
        public string SurfaceTypeMaterial => _surfaceTypeMaterial;
        public string SurfaceTypeDetailObject => _surfaceTypeDetailObject;
        public string[] SlotResolutionDiagnostics => _slotResolutionDiagnostics;

        public void SetMetadata(FcLevelMaterialOverrideService.BrushMaterialMetadata metadata)
        {
            _resolutionSource = metadata.ResolutionSource ?? string.Empty;
            _requestedOverrideName = metadata.RequestedOverrideName ?? string.Empty;
            _requestedMaterialId = metadata.RequestedMaterialId;
            _materialName = metadata.MaterialName ?? string.Empty;
            _materialFullName = metadata.MaterialFullName ?? string.Empty;
            _materialShader = metadata.MaterialShader ?? string.Empty;
            _hasSurfaceType = metadata.HasSurfaceType;
            _surfaceTypeId = metadata.SurfaceTypeId;
            _surfaceTypeName = metadata.SurfaceTypeName ?? string.Empty;
            _surfaceTypeMaterial = metadata.SurfaceTypeMaterial ?? string.Empty;
            _surfaceTypeDetailObject = metadata.SurfaceTypeDetailObject ?? string.Empty;
        }

        public void SetSlotResolutionDiagnostics(string[] diagnostics)
        {
            _slotResolutionDiagnostics = diagnostics != null
                ? (string[])diagnostics.Clone()
                : null;
        }
    }
}
