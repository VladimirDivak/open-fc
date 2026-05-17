using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace OpenFarCry.Importer
{
    /// <summary>
    /// Allocation-free binary reader for raw byte buffers.
    /// Optimized for use in Burst jobs and unsafe contexts.
    /// </summary>
    public unsafe struct BinaryBufferReader
    {
        private readonly byte* _ptr;
        private readonly int _length;
        private int _offset;

        public BinaryBufferReader(byte* ptr, int length)
        {
            _ptr = ptr;
            _length = length;
            _offset = 0;
        }

        public int Offset
        {
            get => _offset;
            set => _offset = value;
        }

        public int Length => _length;
        public bool IsAtEnd => _offset >= _length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Align(int alignment)
        {
            _offset = (_offset + alignment - 1) & ~(alignment - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(int bytes)
        {
            _offset += bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (_offset + 1 > _length) throw new IndexOutOfRangeException();
            return _ptr[_offset++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            if (_offset + 4 > _length) throw new IndexOutOfRangeException();
            int val = *(int*)(_ptr + _offset);
            _offset += 4;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            if (_offset + 4 > _length) throw new IndexOutOfRangeException();
            uint val = *(uint*)(_ptr + _offset);
            _offset += 4;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            if (_offset + 2 > _length) throw new IndexOutOfRangeException();
            ushort val = *(ushort*)(_ptr + _offset);
            _offset += 2;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadSingle()
        {
            if (_offset + 4 > _length) throw new IndexOutOfRangeException();
            float val = *(float*)(_ptr + _offset);
            _offset += 4;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ReadVector3()
        {
            return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ReadQuaternion()
        {
            return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Matrix4x4 ReadMatrix44()
        {
            // Row-major 4x4: row 0, row 1, row 2, row 3
            var m = Matrix4x4.identity;
            m.m00 = ReadSingle(); m.m01 = ReadSingle(); m.m02 = ReadSingle(); m.m03 = ReadSingle();
            m.m10 = ReadSingle(); m.m11 = ReadSingle(); m.m12 = ReadSingle(); m.m13 = ReadSingle();
            m.m20 = ReadSingle(); m.m21 = ReadSingle(); m.m22 = ReadSingle(); m.m23 = ReadSingle();
            m.m30 = ReadSingle(); m.m31 = ReadSingle(); m.m32 = ReadSingle(); m.m33 = ReadSingle();
            return m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadStruct<T>() where T : unmanaged
        {
            int size = sizeof(T);
            if (_offset + size > _length) throw new IndexOutOfRangeException();
            T val = *(T*)(_ptr + _offset);
            _offset += size;
            return val;
        }

        /// <summary>
        /// Returns a direct pointer to the current offset and advances the offset by bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* ReadBytesPtr(int bytes)
        {
            if (_offset + bytes > _length) throw new IndexOutOfRangeException();
            byte* res = _ptr + _offset;
            _offset += bytes;
            return res;
        }
    }
}
