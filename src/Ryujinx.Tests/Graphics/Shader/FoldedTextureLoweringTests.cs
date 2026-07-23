using NUnit.Framework;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation;
using Ryujinx.Graphics.Shader.Translation.Transforms;
using System;
using System.Collections.Generic;
using System.Linq;
using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Tests.Graphics.Shader
{
    public class FoldedTextureLoweringTests
    {
        [Test]
        public void IntegerTexture2DSampleKeepsImageTargetAndAddsFoldedCoordinateHelpers()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            SetBindingPair textureBinding = resourceManager.GetTextureOrImageBinding(
                Instruction.TextureSample,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.IntCoords,
                cbufSlot: -1,
                handle: 8);
            TextureOperation sample = new(
                Instruction.TextureSample,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.IntCoords,
                textureBinding.SetIndex,
                textureBinding.Binding,
                compIndex: 1,
                [Local()],
                [Argument(0), Argument(1)]);
            BasicBlock block = new();
            block.Append(sample);
            List<Function> functions = [];
            HelperFunctionManager hfm = new(functions, ShaderStage.Fragment);
            FeatureFlags usedFeatures = FeatureFlags.None;
            TransformContext context = new(
                hfm,
                [block],
                definitions: null,
                resourceManager,
                gpuAccessor,
                TargetApi.Vulkan,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                shaderAddress: 0,
                shaderSize: 0,
                functionId: 0,
                ref usedFeatures);

            TexturePass.RunPass(context, block.Operations.First);

            Assert.Multiple(() =>
            {
                Assert.That(sample.Type, Is.EqualTo(SamplerType.Texture2D));
                Assert.That(sample.Flags.HasFlag(TextureFlags.PagedTexture2D), Is.False);
                Assert.That(sample.Flags.HasFlag(TextureFlags.BufferTexture2D), Is.False);
                Assert.That(functions.Select(function => function.Name), Does.Contain("FoldedTexelFetchCoordX"));
                Assert.That(functions.Select(function => function.Name), Does.Contain("FoldedTexelFetchCoordY"));
                Assert.That(
                    resourceManager.GetTextureDescriptors(includeArrays: false).Single().Flags.HasFlag(TextureUsageFlags.NeedsScaleValue),
                    Is.True);
            });
        }

        [Test]
        public void IntegerTexture2DSampleWithArrayBindingStillAddsFoldedCoordinateHelpers()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            SetBindingPair textureBinding = resourceManager.GetTextureOrImageBinding(
                Instruction.TextureSample,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.IntCoords,
                cbufSlot: -1,
                handle: 8,
                arrayLength: 2);
            TextureOperation sample = new(
                Instruction.TextureSample,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.IntCoords,
                textureBinding.SetIndex,
                textureBinding.Binding,
                compIndex: 1,
                [Local()],
                [Argument(0), Argument(1)]);
            BasicBlock block = new();
            block.Append(sample);
            List<Function> functions = [];
            HelperFunctionManager hfm = new(functions, ShaderStage.Fragment);
            FeatureFlags usedFeatures = FeatureFlags.None;
            TransformContext context = new(
                hfm,
                [block],
                definitions: null,
                resourceManager,
                gpuAccessor,
                TargetApi.Vulkan,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                shaderAddress: 0,
                shaderSize: 0,
                functionId: 0,
                ref usedFeatures);

            TexturePass.RunPass(context, block.Operations.First);

            Assert.Multiple(() =>
            {
                Assert.That(sample.Type, Is.EqualTo(SamplerType.Texture2D));
                Assert.That(functions.Select(function => function.Name), Does.Contain("FoldedTexelFetchCoordX"));
                Assert.That(functions.Select(function => function.Name), Does.Contain("FoldedTexelFetchCoordY"));
                Assert.That(resourceManager.IsArrayOfTexturesOrImages(textureBinding.Binding, isImage: false), Is.True);
                Assert.That(
                    resourceManager.GetTextureDescriptors(includeArrays: true).Single().Flags.HasFlag(TextureUsageFlags.NeedsScaleValue),
                    Is.True);
            });
        }

        [Test]
        public void Texture2DArraySampleAddsFoldedCoordinateHelpersAndPreservesLayerCoordinate()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            SetBindingPair textureBinding = resourceManager.GetTextureOrImageBinding(
                Instruction.TextureSample,
                SamplerType.Texture2D | SamplerType.Array,
                TextureFormat.Unknown,
                TextureFlags.None,
                cbufSlot: -1,
                handle: 8);
            Operand layer = Argument(2);
            TextureOperation sample = new(
                Instruction.TextureSample,
                SamplerType.Texture2D | SamplerType.Array,
                TextureFormat.Unknown,
                TextureFlags.None,
                textureBinding.SetIndex,
                textureBinding.Binding,
                compIndex: 1,
                [Local()],
                [Argument(0), Argument(1), layer]);
            BasicBlock block = new();
            block.Append(sample);
            List<Function> functions = [];
            HelperFunctionManager hfm = new(functions, ShaderStage.Fragment);
            FeatureFlags usedFeatures = FeatureFlags.None;
            TransformContext context = new(
                hfm,
                [block],
                definitions: null,
                resourceManager,
                gpuAccessor,
                TargetApi.Vulkan,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                shaderAddress: 0,
                shaderSize: 0,
                functionId: 0,
                ref usedFeatures);

            TexturePass.RunPass(context, block.Operations.First);

            int functionIdX = functions.FindIndex(function => function.Name == "FoldedTextureCoordX");
            int functionIdY = functions.FindIndex(function => function.Name == "FoldedTextureCoordY");
            Operation[] foldedCalls = block.Operations
                .OfType<Operation>()
                .Where(operation => operation.Inst == Instruction.Call &&
                    operation.GetSource(0).Type == OperandType.Constant &&
                    (operation.GetSource(0).Value == functionIdX || operation.GetSource(0).Value == functionIdY))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(functionIdX, Is.Not.Negative);
                Assert.That(functionIdY, Is.Not.Negative);
                Assert.That(foldedCalls, Has.Length.EqualTo(2));
                Assert.That(sample.GetSource(0), Is.EqualTo(foldedCalls.Single(call => call.GetSource(0).Value == functionIdX).Dest));
                Assert.That(sample.GetSource(1), Is.EqualTo(foldedCalls.Single(call => call.GetSource(0).Value == functionIdY).Dest));
                Assert.That(sample.GetSource(2), Is.EqualTo(layer));
                Assert.That(
                    resourceManager.GetTextureDescriptors(includeArrays: false).Single().Flags.HasFlag(TextureUsageFlags.NeedsScaleValue),
                    Is.True);
            });
        }

        [Test]
        public void MultiDestTextureSizeUsesMatchingFoldedComponentIndex()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Fragment, gpuAccessor);
            SetBindingPair textureBinding = resourceManager.GetTextureOrImageBinding(
                Instruction.TextureQuerySize,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.None,
                cbufSlot: -1,
                handle: 8);
            TextureOperation query = new(
                Instruction.TextureQuerySize,
                SamplerType.Texture2D,
                TextureFormat.Unknown,
                TextureFlags.None,
                textureBinding.SetIndex,
                textureBinding.Binding,
                compIndex: 0,
                [Local(), Local()],
                [Const(0)]);
            BasicBlock block = new();
            block.Append(query);
            List<Function> functions = [];
            HelperFunctionManager hfm = new(functions, ShaderStage.Fragment);
            FeatureFlags usedFeatures = FeatureFlags.None;
            TransformContext context = new(
                hfm,
                [block],
                definitions: null,
                resourceManager,
                gpuAccessor,
                TargetApi.Vulkan,
                TargetLanguage.Spirv,
                ShaderStage.Fragment,
                shaderAddress: 0,
                shaderSize: 0,
                functionId: 0,
                ref usedFeatures);

            TexturePass.RunPass(context, block.Operations.First);

            int functionId = functions.FindIndex(function => function.Name == "TextureSizeUnscale");
            int[] componentArguments = block.Operations
                .OfType<Operation>()
                .Where(operation => operation.Inst == Instruction.Call &&
                    operation.GetSource(0).Type == OperandType.Constant &&
                    operation.GetSource(0).Value == functionId)
                .Select(operation => operation.GetSource(3).Value)
                .ToArray();

            Assert.That(functionId, Is.Not.Negative);
            Assert.That(componentArguments, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void ImageStoreUsesFoldedIntegerCoordinates()
        {
            RecordingGpuAccessor gpuAccessor = new();
            ResourceManager resourceManager = new(ShaderStage.Compute, gpuAccessor);
            SetBindingPair imageBinding = resourceManager.GetTextureOrImageBinding(
                Instruction.ImageStore,
                SamplerType.Texture2D,
                TextureFormat.R8Unorm,
                TextureFlags.None,
                cbufSlot: -1,
                handle: 8);
            TextureOperation store = new(
                Instruction.ImageStore,
                SamplerType.Texture2D,
                TextureFormat.R8Unorm,
                TextureFlags.None,
                imageBinding.SetIndex,
                imageBinding.Binding,
                compIndex: 0,
                null,
                [Argument(0), Argument(1), Argument(2)]);
            BasicBlock block = new();
            block.Append(store);
            List<Function> functions = [];
            HelperFunctionManager hfm = new(functions, ShaderStage.Compute);
            FeatureFlags usedFeatures = FeatureFlags.None;
            TransformContext context = new(
                hfm,
                [block],
                definitions: null,
                resourceManager,
                gpuAccessor,
                TargetApi.Vulkan,
                TargetLanguage.Spirv,
                ShaderStage.Compute,
                shaderAddress: 0,
                shaderSize: 0,
                functionId: 0,
                ref usedFeatures);

            TexturePass.RunPass(context, block.Operations.First);

            int functionIdX = functions.FindIndex(function => function.Name == "FoldedTexelFetchCoordX");
            int functionIdY = functions.FindIndex(function => function.Name == "FoldedTexelFetchCoordY");
            Operation[] foldedCalls = block.Operations
                .OfType<Operation>()
                .Where(operation => operation.Inst == Instruction.Call &&
                    operation.GetSource(0).Type == OperandType.Constant &&
                    (operation.GetSource(0).Value == functionIdX || operation.GetSource(0).Value == functionIdY))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(functionIdX, Is.Not.Negative);
                Assert.That(functionIdY, Is.Not.Negative);
                Assert.That(foldedCalls, Has.Length.EqualTo(2));
                Assert.That(store.GetSource(0), Is.EqualTo(foldedCalls.Single(call => call.GetSource(0).Value == functionIdX).Dest));
                Assert.That(store.GetSource(1), Is.EqualTo(foldedCalls.Single(call => call.GetSource(0).Value == functionIdY).Dest));
                Assert.That(
                    resourceManager.GetImageDescriptors(includeArrays: false).Single().Flags.HasFlag(TextureUsageFlags.ImageStore),
                    Is.True);
                Assert.That(
                    resourceManager.GetImageDescriptors(includeArrays: false).Single().Flags.HasFlag(TextureUsageFlags.NeedsScaleValue),
                    Is.True);
            });
        }

        private sealed class RecordingGpuAccessor : IGpuAccessor
        {
            public void Log(string message)
            {
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
