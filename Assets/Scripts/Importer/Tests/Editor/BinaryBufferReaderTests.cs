using NUnit.Framework;
using OpenFarCry.Importer;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class BinaryBufferReaderTests
    {
        [Test]
        public unsafe void ReadBasicTypes_WorksCorrectly()
        {
            byte[] data = new byte[64];
            fixed (byte* p = data)
            {
                *(int*)(p + 0) = 123456;
                *(uint*)(p + 4) = 0xDEADBEEF;
                *(float*)(p + 8) = 3.14159f;
                *(ushort*)(p + 12) = 0xFACE;
                *(byte*)(p + 14) = 0xAA;

                var r = new BinaryBufferReader(p, data.Length);
                Assert.That(r.ReadInt32(), Is.EqualTo(123456));
                Assert.That(r.ReadUInt32(), Is.EqualTo(0xDEADBEEF));
                Assert.That(r.ReadSingle(), Is.EqualTo(3.14159f).Within(1e-5f));
                Assert.That(r.ReadUInt16(), Is.EqualTo(0xFACE));
                Assert.That(r.ReadByte(), Is.EqualTo(0xAA));
            }
        }

        [Test]
        public unsafe void Align_WorksCorrectly()
        {
            byte[] data = new byte[16];
            fixed (byte* p = data)
            {
                var r = new BinaryBufferReader(p, data.Length);
                r.ReadByte();
                Assert.That(r.Offset, Is.EqualTo(1));
                r.Align(4);
                Assert.That(r.Offset, Is.EqualTo(4));
                r.ReadByte();
                r.Align(8);
                Assert.That(r.Offset, Is.EqualTo(8));
            }
        }

        [Test]
        public unsafe void ReadStruct_WorksCorrectly()
        {
            byte[] data = new byte[32];
            var expected = new TestStruct { A = 10, B = 20.5f, C = 30 };
            
            fixed (byte* p = data)
            {
                *(TestStruct*)p = expected;
                var r = new BinaryBufferReader(p, data.Length);
                var actual = r.ReadStruct<TestStruct>();
                
                Assert.That(actual.A, Is.EqualTo(expected.A));
                Assert.That(actual.B, Is.EqualTo(expected.B).Within(1e-5f));
                Assert.That(actual.C, Is.EqualTo(expected.C));
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct TestStruct
        {
            public int A;
            public float B;
            public int C;
        }
    }
}
