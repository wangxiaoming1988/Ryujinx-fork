using NUnit.Framework;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation;
using Ryujinx.Graphics.Shader.Translation.Optimizations;
using System;
using System.Collections.Generic;
using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Tests.Graphics.Shader
{
    public class GlobalToStorageTests
    {
        [Test]
        public void UnresolvedGlobalStoreDoesNotSurviveTranslation()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Compute, gpuAccessor);
            BasicBlock block = new();
            Operation store = new(
                Instruction.Store,
                StorageKind.GlobalMemory,
                null,
                Argument(0),
                Const(1));
            block.Append(store);

            GlobalToStorage.RunPass(
                new HelperFunctionManager(new List<Function>(), ShaderStage.Compute),
                [block],
                resourceManager,
                gpuAccessor,
                TargetLanguage.Spirv);

            foreach (INode node in block.Operations)
            {
                if (node is Operation operation)
                {
                    Assert.That(operation.Inst, Is.Not.EqualTo(Instruction.Load));
                    Assert.That(operation.Inst, Is.Not.EqualTo(Instruction.Store));
                }
            }

            Assert.That(gpuAccessor.Messages, Has.Some.Contains("Failed to find storage buffer"));
        }

        [Test]
        public void UnresolvedGlobalLoadDoesNotSurviveTranslation()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Compute, gpuAccessor);
            BasicBlock block = new();
            Operation load = new(
                Instruction.Load,
                StorageKind.GlobalMemory,
                Local(),
                Argument(0));
            block.Append(load);

            GlobalToStorage.RunPass(
                new HelperFunctionManager(new List<Function>(), ShaderStage.Compute),
                [block],
                resourceManager,
                gpuAccessor,
                TargetLanguage.Spirv);

            foreach (INode node in block.Operations)
            {
                if (node is Operation operation)
                {
                    Assert.That(operation.Inst, Is.Not.EqualTo(Instruction.Load));
                    Assert.That(operation.Inst, Is.Not.EqualTo(Instruction.Store));
                }
            }

            Assert.That(gpuAccessor.Messages, Has.Some.Contains("Failed to find storage buffer"));
        }

        private sealed class RecordingGpuAccessor : IGpuAccessor
        {
            public List<string> Messages { get; } = [];

            public void Log(string message)
            {
                Messages.Add(message);
            }

            public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
            {
                return ReadOnlySpan<ulong>.Empty;
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
