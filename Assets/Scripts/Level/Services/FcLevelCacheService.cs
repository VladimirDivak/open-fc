using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    public sealed class FcLevelCacheService : MonoBehaviour
    {
        [SerializeField] string _levelScopeId;

        public string LevelScopeId => _levelScopeId;

        public void SetLevelScope(string levelName)
        {
            _levelScopeId = levelName;
        }

        void OnDestroy()
        {
            if (!string.IsNullOrEmpty(_levelScopeId))
            {
                FcEntityLoadService.Current?.CancelAllForScope(_levelScopeId);
                CgfRuntimeImporter.ReleaseLevelScope(_levelScopeId);
                CgfRuntimeImporter.TrimUnused();
                FcLevelRuntimeReportRegistry.Release(_levelScopeId);
            }
        }
    }
}
