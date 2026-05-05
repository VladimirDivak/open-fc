using System;

namespace HTraceAO.Scripts.Extensions.CameraHistorySystem
{
    public class CameraHistorySystem<T> where T : struct, ICameraHistoryData
    {
        private const int InitialCameraCount = 4; // minimum 2
        private const int MaxCameraCountHard  = 16;
        private const int TTLFrames           = 5;

        private int _cameraHistoryIndex;
        private T[]   _cameraHistoryData = new T[InitialCameraCount];
        private int[] _usageCount        = new int[InitialCameraCount];
        private int[] _lastSeenFrame     = new int[InitialCameraCount];

        private int _lastPruneFrame = -1;


        // Returns true if camera is new this frame (slot was just created).
        public bool SyncCamera(int cameraHash, int frameId)
        {
            PruneDeadSlots(frameId);

            _cameraHistoryIndex = GetCameraHistoryDataIndex(cameraHash);

            if (_cameraHistoryIndex != -1)
            {
                _usageCount[_cameraHistoryIndex]++;
                _lastSeenFrame[_cameraHistoryIndex] = frameId;
                return false;
            }

            int targetSlot;

            int emptySlot = FindEmptySlot();
            if (emptySlot != -1)
            {
                targetSlot = emptySlot;
            }
            else if (_cameraHistoryData.Length < MaxCameraCountHard)
            {
                targetSlot = _cameraHistoryData.Length;
                Array.Resize(ref _cameraHistoryData, targetSlot + 1);
                Array.Resize(ref _usageCount, targetSlot + 1);
                Array.Resize(ref _lastSeenFrame, targetSlot + 1);
            }
            else
            {
                targetSlot = FindLeastUsedSlot();
                if (_cameraHistoryData[targetSlot] is IDisposable disposable)
                    disposable.Dispose();
            }

            _usageCount[targetSlot]    = 1;
            _lastSeenFrame[targetSlot] = frameId;
            _cameraHistoryIndex        = targetSlot;
            _cameraHistoryData[targetSlot] = new T();
            _cameraHistoryData[targetSlot].SetHash(cameraHash);
            return true;
        }

        private void PruneDeadSlots(int frameId)
        {
            if (frameId == _lastPruneFrame) return;
            _lastPruneFrame = frameId;

            for (int i = 0; i < _cameraHistoryData.Length; i++)
            {
                if (_cameraHistoryData[i].GetHash() == 0) continue;
                if (frameId - _lastSeenFrame[i] <= TTLFrames) continue;

                if (_cameraHistoryData[i] is IDisposable disposable)
                    disposable.Dispose();
                _cameraHistoryData[i] = default;
                _usageCount[i]        = 0;
                _lastSeenFrame[i]     = 0;
            }
        }

        private int GetCameraHistoryDataIndex(int cameraHash)
        {
            for (int i = 0; i < _cameraHistoryData.Length; i++)
            {
                if (_cameraHistoryData[i].GetHash() == cameraHash) return i;
            }
            return -1; // new camera
        }

        // Slot is empty if it was pruned (hash=0) and has no usage.
        // Excludes HDRP hash=0 sentinel slots which have usageCount > 0.
        private int FindEmptySlot()
        {
            for (int i = 0; i < _cameraHistoryData.Length; i++)
            {
                if (_cameraHistoryData[i].GetHash() == 0 && _usageCount[i] == 0) return i;
            }
            return -1;
        }

        // Evict the slot with the lowest usage count.
        // Halve all counts before comparison to prevent old high-counts from blocking eviction indefinitely.
        private int FindLeastUsedSlot()
        {
            for (int i = 0; i < _cameraHistoryData.Length; i++)
                _usageCount[i] >>= 1;

            int minSlot = 0;
            for (int i = 1; i < _cameraHistoryData.Length; i++)
            {
                if (_usageCount[i] < _usageCount[minSlot])
                    minSlot = i;
            }
            return minSlot;
        }

        public ref T GetCameraData()
        {
            return ref _cameraHistoryData[_cameraHistoryIndex];
        }

        public void SetCameraData(T data)
        {
            _cameraHistoryData[_cameraHistoryIndex] = data;
        }

        public void Cleanup()
        {
            for (int index = 0; index < _cameraHistoryData.Length; index++)
            {
                if (_cameraHistoryData[index] is IDisposable disposable)
                    disposable.Dispose();
                _cameraHistoryData[index] = default;
                _usageCount[index]        = 0;
                _lastSeenFrame[index]     = 0;
            }

            _lastPruneFrame = -1;

            if (_cameraHistoryData.Length > InitialCameraCount)
            {
                _cameraHistoryData = new T[InitialCameraCount];
                _usageCount        = new int[InitialCameraCount];
                _lastSeenFrame     = new int[InitialCameraCount];
            }
        }
    }
}
