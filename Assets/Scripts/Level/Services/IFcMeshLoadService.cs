using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenFarCry.Level.Services
{
    public interface IFcMeshLoadService
    {
        UniTask<LoadedMeshArtifact> LoadAsync(FcMeshLoadRequest request, CancellationToken ct = default);
    }
}
