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
                TargetLanguage.Spirv,
                ShaderStage.Compute,
                0x12340000,
                16,
                0);

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
                TargetLanguage.Spirv,
                ShaderStage.Compute,
                0x12340000,
                16,
                0);

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
        public void UnresolvedGlobalStoreDiagnosticIdentifiesShaderAndAddressExpression()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            BasicBlock block = new(9);
            Operand addressLow = Local();
            block.Append(new Operation(Instruction.Add, addressLow, Argument(2), Const(0x20)));
            block.Append(new Operation(
                Instruction.Store,
                StorageKind.GlobalMemory,
                null,
                addressLow,
                Argument(3),
                Const(0x55)));

            GlobalToStorage.RunPass(
                new HelperFunctionManager(new List<Function>(), ShaderStage.Fragment),
                [block],
                resourceManager,
                gpuAccessor,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                0x2ea930000,
                16,
                4);

            string message = gpuAccessor.Messages.Find(message => message.Contains("Failed to find storage buffer"));

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("stage=Fragment"));
            Assert.That(message, Does.Contain("shader=0x2EA930000"));
            Assert.That(message, Does.Contain("shaderHash=").And.Not.Contain("shaderHash=unavailable"));
            Assert.That(message, Does.Contain("function=4"));
            Assert.That(message, Does.Contain("block=9"));
            Assert.That(message, Does.Contain("storage=GlobalMemory"));
            Assert.That(message, Does.Contain("storageBindings=0[]"));
            Assert.That(message, Does.Contain("addressLow=(Add arg2,0x00000020)"));
            Assert.That(message, Does.Contain("addressHigh=arg3"));
            Assert.That(message, Does.Contain("data=[0x00000055]"));
        }

        [Test]
        public void ConstantBufferGlobalStoreLowersToStorageBufferWithoutFailure()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            BasicBlock block = new();
            block.Append(new Operation(
                Instruction.Store,
                StorageKind.GlobalMemory,
                null,
                Cbuf(2, 4),
                Cbuf(2, 5),
                Const(1)));

            GlobalToStorage.RunPass(
                new HelperFunctionManager(new List<Function>(), ShaderStage.Fragment),
                [block],
                resourceManager,
                gpuAccessor,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                0x1000,
                16,
                0);

            Assert.That(gpuAccessor.Messages, Has.None.Contains("Failed to find storage buffer"));
            Assert.That(
                block.Operations,
                Has.Some.Matches<Operation>(operation =>
                    operation.Inst == Instruction.Store &&
                    operation.StorageKind == StorageKind.StorageBuffer));
            Assert.That(resourceManager.GetStorageBufferDescriptors(), Has.Length.EqualTo(1));
        }

        [Test]
        public void UnresolvedPhiCycleDiagnosticIsBounded()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            BasicBlock block = new(3);
            Operand addressLow = Local();
            PhiNode phi = new(addressLow);
            phi.AddSource(block, addressLow);
            block.Append(phi);
            block.Append(new Operation(
                Instruction.Store,
                StorageKind.GlobalMemory,
                null,
                addressLow,
                Const(0),
                Const(1)));

            GlobalToStorage.RunPass(
                new HelperFunctionManager(new List<Function>(), ShaderStage.Fragment),
                [block],
                resourceManager,
                gpuAccessor,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                0x2000,
                16,
                0);

            string message = gpuAccessor.Messages.Find(message => message.Contains("Failed to find storage buffer"));

            Assert.That(message, Does.Contain("addressLow=(Phi cycle)"));
            Assert.That(message.Length, Is.LessThan(2048));
        }

        private sealed class RecordingGpuAccessor : IGpuAccessor
        {
            private readonly ulong[] _code = [0x0123456789abcdef, 0xfedcba9876543210];

            public List<string> Messages { get; } = [];

            public void Log(string message)
            {
                Messages.Add(message);
            }

            public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
            {
                return _code;
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
