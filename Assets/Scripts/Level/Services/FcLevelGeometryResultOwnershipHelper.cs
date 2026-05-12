using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Level.Services
{
    static class FcLevelGeometryResultOwnershipHelper
    {
        public static void ReleaseOwnedResults(
            bool releaseImportResultsOnDestroy,
            FcLevelResourceService releaseService,
            CgfRuntimeImportResult importResult,
            IReadOnlyList<CgfRuntimeImportResult> lodResults)
        {
            if (!releaseImportResultsOnDestroy || releaseService == null)
                return;

            if (importResult != null)
                releaseService.ReleaseImportResult(importResult);

            if (lodResults == null || lodResults.Count == 0)
                return;

            for (int i = 0; i < lodResults.Count; i++)
            {
                var lodResult = lodResults[i];
                if (lodResult != null)
                    releaseService.ReleaseImportResult(lodResult);
            }
        }
    }
}
