using NUnit.Framework;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.Translation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ryujinx.Tests.Graphics.Shader
{
    public class NintendoSwitchSportsShaderTests
    {
        private static readonly ulong[] VertexShader =
        [
            0x0000000006070461,
            0x0000000000000000,
            0x0000000000000000,
            0x0000000000000000,
            0x0000000000000000,
            0x0000000080000000,
            0x0000000000000000,
            0x0000000000000000,
            0x0000000000000000,
            0x0000000000000000,
            0x003fd800e3e007f0,
            0x010000000017f002,
            0xefd87f802fc7ff00,
            0x4c18818004470000,
            0x041fc400f6a007f2,
            0x4c1008000457ff01,
            0xeed5200000070000,
            0xeedc200000470002,
            0x001fc400fe2007f1,
            0xeedc2000000700ff,
            0xeedc2000008700ff,
            0xeedc200000c700ff,
            0x001ffc00ffe007f1,
            0xeedc2000010700ff,
            0xe30000000007000f,
        ];

        [TestCase(false)]
        [TestCase(true)]
        public void IndirectGlobalStoresBindResolvedTargetsThroughFullTranslation(bool asCompute)
        {
            RecordingGpuAccessor gpuAccessor = new(VertexShader);
            TranslationOptions options = new(TargetLanguage.Spirv, TargetApi.Vulkan, TranslationFlags.DebugMode);
            TranslatorContext context = Translator.CreateContext(0, gpuAccessor, options);

            Assert.That(context.Stage, Is.EqualTo(ShaderStage.Vertex));
            Assert.That(context.Size, Is.EqualTo(200));

            ShaderProgram program = context.Translate(asCompute);
            string[] failures = gpuAccessor.Messages
                .Where(message => message.Contains("Failed to find storage buffer"))
                .ToArray();
            BufferDescriptor[] indirectTargets = program.Info.SBuffers
                .Where(descriptor => descriptor.Flags.HasFlag(BufferUsageFlags.Indirect))
                .ToArray();
            string spirvOutputPath = Environment.GetEnvironmentVariable("RYUJINX_TEST_SPIRV_OUTPUT");

            if (!string.IsNullOrEmpty(spirvOutputPath))
            {
                File.WriteAllBytes($"{spirvOutputPath}.{(asCompute ? "compute" : "vertex")}.spv", program.BinaryCode);
            }

            Assert.That(program.BinaryCode, Is.Not.Null.And.Not.Empty);
            Assert.That(program.Info.Stage, Is.EqualTo(ShaderStage.Vertex));
            Assert.That(failures, Is.Empty);
            Assert.That(indirectTargets, Has.Length.EqualTo(SupportBuffer.IndirectStorageTargetsPerStage));
            Assert.That(indirectTargets, Has.All.Matches<BufferDescriptor>(descriptor =>
                descriptor.SbCbSlot == 0 &&
                descriptor.SbCbOffset == 0x44 &&
                descriptor.Flags.HasFlag(BufferUsageFlags.Write)));
            Assert.That(indirectTargets.Select(descriptor => descriptor.IndirectIndex),
                Is.EqualTo(Enumerable.Range(0, SupportBuffer.IndirectStorageTargetsPerStage)));
        }

        private sealed class RecordingGpuAccessor : IGpuAccessor
        {
            private readonly ulong[] _code;

            public List<string> Messages { get; } = [];

            public RecordingGpuAccessor(ulong[] code)
            {
                _code = code;
            }

            public void Log(string message)
            {
                Messages.Add(message);
            }

            public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
            {
                return _code.AsSpan((int)(address / sizeof(ulong)));
            }

            public SetBindingPair CreateConstantBufferBinding(int index)
            {
                return new SetBindingPair(0, index);
            }

            public SetBindingPair CreateImageBinding(int count, bool isBuffer)
            {
                return new SetBindingPair(3, 0);
            }

            public SetBindingPair CreateStorageBufferBinding(int index)
            {
                return new SetBindingPair(1, index);
            }

            public SetBindingPair CreateTextureBinding(int count, bool isBuffer)
            {
                return new SetBindingPair(2, 0);
            }

            public int QuerySamplerArrayLengthFromPool()
            {
                return 1;
            }

            public int QueryTextureArrayLengthFromBuffer(int slot)
            {
                return 1;
            }

            public int QueryTextureArrayLengthFromPool()
            {
                return 1;
            }
        }
    }
}
