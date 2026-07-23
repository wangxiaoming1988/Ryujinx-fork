using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using System;
using System.Collections.Generic;
using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Graphics.Shader.Translation
{
    class HelperFunctionManager
    {
        private const int FoldedLinearTextureGutterX = 1;
        private const int FoldedLinearTextureGutterY = 1;

        private readonly List<Function> _functionList;
        private readonly Dictionary<int, int> _functionIds;
        private readonly ShaderStage _stage;

        public HelperFunctionManager(List<Function> functionList, ShaderStage stage)
        {
            _functionList = functionList;
            _functionIds = new Dictionary<int, int>();
            _stage = stage;
        }

        public int AddFunction(Function function)
        {
            int functionId = _functionList.Count;
            _functionList.Add(function);

            return functionId;
        }

        public int GetOrCreateFunctionId(HelperFunctionName functionName)
        {
            if (_functionIds.TryGetValue((int)functionName, out int functionId))
            {
                return functionId;
            }

            Function function = GenerateFunction(functionName);
            functionId = AddFunction(function);
            _functionIds.Add((int)functionName, functionId);

            return functionId;
        }

        public int GetOrCreateFunctionId(HelperFunctionName functionName, int id)
        {
            int key = (int)functionName | (id << 16);

            if (_functionIds.TryGetValue(key, out int functionId))
            {
                return functionId;
            }

            Function function = GenerateFunction(functionName, id);
            functionId = AddFunction(function);
            _functionIds.Add(key, functionId);

            return functionId;
        }

        public int GetOrCreateShuffleFunctionId(HelperFunctionName functionName, int subgroupSize)
        {
            if (_functionIds.TryGetValue((int)functionName, out int functionId))
            {
                return functionId;
            }

            Function function = GenerateShuffleFunction(functionName, subgroupSize);
            functionId = AddFunction(function);
            _functionIds.Add((int)functionName, functionId);

            return functionId;
        }

        private Function GenerateFunction(HelperFunctionName functionName)
        {
            return functionName switch
            {
                HelperFunctionName.ConvertDoubleToFloat => GenerateConvertDoubleToFloatFunction(),
                HelperFunctionName.ConvertFloatToDouble => GenerateConvertFloatToDoubleFunction(),
                HelperFunctionName.TexelFetchScale => GenerateTexelFetchScaleFunction(),
                HelperFunctionName.TextureSizeUnscale => GenerateTextureSizeUnscaleFunction(),
                HelperFunctionName.FoldedTextureCoordX => GenerateFoldedTextureCoordXFunction(),
                HelperFunctionName.FoldedTextureCoordY => GenerateFoldedTextureCoordYFunction(),
                HelperFunctionName.FoldedTexelFetchCoordX => GenerateFoldedTexelFetchCoordXFunction(),
                HelperFunctionName.FoldedTexelFetchCoordY => GenerateFoldedTexelFetchCoordYFunction(),
                HelperFunctionName.BufferTexture2DNearestIndexInt => GenerateBufferTexture2DNearestIndexIntFunction(),
                HelperFunctionName.BufferTexture2DNearestIndex => GenerateBufferTexture2DNearestIndexFunction(),
                HelperFunctionName.BufferTexture2DBilinearIndices => GenerateBufferTexture2DBilinearIndicesFunction(),
                HelperFunctionName.BufferTexture2DSize => GenerateBufferTexture2DSizeFunction(),
                HelperFunctionName.PagedTexture2DNearestCoordsInt => GeneratePagedTexture2DNearestCoordsIntFunction(),
                HelperFunctionName.PagedTexture2DNearestCoords => GeneratePagedTexture2DNearestCoordsFunction(),
                _ => throw new ArgumentException($"Invalid function name {functionName}"),
            };
        }

        private static Function GenerateConvertDoubleToFloatFunction()
        {
            EmitterContext context = new();

            Operand valueLow = Argument(0);
            Operand valueHigh = Argument(1);

            Operand mantissaLow = context.BitwiseAnd(valueLow, Const(((1 << 22) - 1)));
            Operand mantissa = context.ShiftRightU32(valueLow, Const(22));

            mantissa = context.BitwiseOr(mantissa, context.ShiftLeft(context.BitwiseAnd(valueHigh, Const(0xfffff)), Const(10)));
            mantissa = context.BitwiseOr(mantissa, context.ConditionalSelect(mantissaLow, Const(1), Const(0)));

            Operand exp = context.BitwiseAnd(context.ShiftRightU32(valueHigh, Const(20)), Const(0x7ff));
            Operand sign = context.ShiftRightS32(valueHigh, Const(31));

            Operand resultSign = context.ShiftLeft(sign, Const(31));

            Operand notZero = context.BitwiseOr(mantissa, exp);

            Operand lblNotZero = Label();

            context.BranchIfTrue(lblNotZero, notZero);

            context.Return(resultSign);

            context.MarkLabel(lblNotZero);

            Operand notNaNOrInf = context.ICompareNotEqual(exp, Const(0x7ff));

            mantissa = context.BitwiseOr(mantissa, Const(0x40000000));
            exp = context.ISubtract(exp, Const(0x381));

            // Note: Overflow cases are not handled here and might produce incorrect results.

            Operand roundBits = context.BitwiseAnd(mantissa, Const(0x7f));
            Operand roundBitsXor64 = context.BitwiseExclusiveOr(roundBits, Const(0x40));
            mantissa = context.ShiftRightU32(context.IAdd(mantissa, Const(0x40)), Const(7));
            mantissa = context.BitwiseAnd(mantissa, context.ConditionalSelect(roundBitsXor64, Const(~0), Const(~1)));

            exp = context.ConditionalSelect(mantissa, exp, Const(0));
            exp = context.ConditionalSelect(notNaNOrInf, exp, Const(0xff));

            Operand result = context.IAdd(context.IAdd(mantissa, context.ShiftLeft(exp, Const(23))), resultSign);

            context.Return(result);

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "ConvertDoubleToFloat", true, 2, 0);
        }

        private static Function GenerateConvertFloatToDoubleFunction()
        {
            EmitterContext context = new();

            Operand value = Argument(0);

            Operand mantissa = context.BitwiseAnd(value, Const(0x7fffff));
            Operand exp = context.BitwiseAnd(context.ShiftRightU32(value, Const(23)), Const(0xff));
            Operand sign = context.ShiftRightS32(value, Const(31));

            Operand notNaNOrInf = context.ICompareNotEqual(exp, Const(0xff));
            Operand expNotZero = context.ICompareNotEqual(exp, Const(0));
            Operand notDenorm = context.BitwiseOr(expNotZero, context.ICompareEqual(mantissa, Const(0)));

            exp = context.IAdd(exp, Const(0x380));

            Operand shiftDist = context.ISubtract(Const(32), context.FindMSBU32(mantissa));
            Operand normExp = context.ISubtract(context.ISubtract(Const(1), shiftDist), Const(1));
            Operand normMant = context.ShiftLeft(mantissa, shiftDist);

            exp = context.ConditionalSelect(notNaNOrInf, exp, Const(0x7ff));
            exp = context.ConditionalSelect(notDenorm, exp, normExp);
            mantissa = context.ConditionalSelect(expNotZero, mantissa, normMant);

            Operand resultLow = context.ShiftLeft(mantissa, Const(29));
            Operand resultHigh = context.ShiftRightU32(mantissa, Const(3));

            resultHigh = context.IAdd(resultHigh, context.ShiftLeft(exp, Const(20)));
            resultHigh = context.IAdd(resultHigh, context.ShiftLeft(sign, Const(31)));

            context.Copy(Argument(1), resultLow);
            context.Copy(Argument(2), resultHigh);
            context.Return();

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "ConvertFloatToDouble", false, 1, 2);
        }

        private static Function GenerateFunction(HelperFunctionName functionName, int id)
        {
            return functionName switch
            {
                HelperFunctionName.SharedAtomicMaxS32 => GenerateSharedAtomicSigned(id, isMin: false),
                HelperFunctionName.SharedAtomicMinS32 => GenerateSharedAtomicSigned(id, isMin: true),
                HelperFunctionName.SharedStore8 => GenerateSharedStore8(id),
                HelperFunctionName.SharedStore16 => GenerateSharedStore16(id),
                _ => throw new ArgumentException($"Invalid function name {functionName}"),
            };
        }

        private static Function GenerateSharedAtomicSigned(int id, bool isMin)
        {
            EmitterContext context = new();

            Operand wordOffset = Argument(0);
            Operand value = Argument(1);

            Operand result = GenerateSharedAtomicCasLoop(context, wordOffset, id, (memValue) =>
            {
                return isMin
                    ? context.IMinimumS32(memValue, value)
                    : context.IMaximumS32(memValue, value);
            });

            context.Return(result);

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, $"SharedAtomic{(isMin ? "Min" : "Max")}_{id}", true, 2, 0);
        }

        private static Function GenerateSharedStore8(int id)
        {
            return GenerateSharedStore(id, 8);
        }

        private static Function GenerateSharedStore16(int id)
        {
            return GenerateSharedStore(id, 16);
        }

        private static Function GenerateSharedStore(int id, int bitSize)
        {
            EmitterContext context = new();

            Operand offset = Argument(0);
            Operand value = Argument(1);

            Operand wordOffset = context.ShiftRightU32(offset, Const(2));
            Operand bitOffset = GetBitOffset(context, offset);

            GenerateSharedAtomicCasLoop(context, wordOffset, id, (memValue) =>
            {
                return context.BitfieldInsert(memValue, value, bitOffset, Const(bitSize));
            });

            context.Return();

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, $"SharedStore{bitSize}_{id}", false, 2, 0);
        }

        private static Function GenerateShuffleFunction(HelperFunctionName functionName, int subgroupSize)
        {
            return functionName switch
            {
                HelperFunctionName.Shuffle => GenerateShuffle(subgroupSize),
                HelperFunctionName.ShuffleDown => GenerateShuffleDown(subgroupSize),
                HelperFunctionName.ShuffleUp => GenerateShuffleUp(subgroupSize),
                HelperFunctionName.ShuffleXor => GenerateShuffleXor(subgroupSize),
                _ => throw new ArgumentException($"Invalid function name {functionName}"),
            };
        }

        private static Function GenerateShuffle(int subgroupSize)
        {
            EmitterContext context = new();

            Operand value = Argument(0);
            Operand index = Argument(1);
            Operand mask = Argument(2);

            Operand clamp = context.BitwiseAnd(mask, Const(0x1f));
            Operand segMask = context.BitwiseAnd(context.ShiftRightU32(mask, Const(8)), Const(0x1f));
            Operand minThreadId = context.BitwiseAnd(GenerateLoadSubgroupLaneId(context, subgroupSize), segMask);
            Operand maxThreadId = context.BitwiseOr(context.BitwiseAnd(clamp, context.BitwiseNot(segMask)), minThreadId);
            Operand srcThreadId = context.BitwiseOr(context.BitwiseAnd(index, context.BitwiseNot(segMask)), minThreadId);
            Operand valid = context.ICompareLessOrEqualUnsigned(srcThreadId, maxThreadId);

            context.Copy(Argument(3), valid);

            Operand result = context.Shuffle(value, GenerateSubgroupShuffleIndex(context, srcThreadId, subgroupSize));

            context.Return(context.ConditionalSelect(valid, result, value));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "Shuffle", true, 3, 1);
        }

        private static Function GenerateShuffleDown(int subgroupSize)
        {
            EmitterContext context = new();

            Operand value = Argument(0);
            Operand index = Argument(1);
            Operand mask = Argument(2);

            Operand clamp = context.BitwiseAnd(mask, Const(0x1f));
            Operand segMask = context.BitwiseAnd(context.ShiftRightU32(mask, Const(8)), Const(0x1f));
            Operand laneId = GenerateLoadSubgroupLaneId(context, subgroupSize);
            Operand minThreadId = context.BitwiseAnd(laneId, segMask);
            Operand maxThreadId = context.BitwiseOr(context.BitwiseAnd(clamp, context.BitwiseNot(segMask)), minThreadId);
            Operand srcThreadId = context.IAdd(laneId, index);
            Operand valid = context.ICompareLessOrEqualUnsigned(srcThreadId, maxThreadId);

            context.Copy(Argument(3), valid);

            Operand result = context.Shuffle(value, GenerateSubgroupShuffleIndex(context, srcThreadId, subgroupSize));

            context.Return(context.ConditionalSelect(valid, result, value));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "ShuffleDown", true, 3, 1);
        }

        private static Function GenerateShuffleUp(int subgroupSize)
        {
            EmitterContext context = new();

            Operand value = Argument(0);
            Operand index = Argument(1);
            Operand mask = Argument(2);

            Operand segMask = context.BitwiseAnd(context.ShiftRightU32(mask, Const(8)), Const(0x1f));
            Operand laneId = GenerateLoadSubgroupLaneId(context, subgroupSize);
            Operand minThreadId = context.BitwiseAnd(laneId, segMask);
            Operand srcThreadId = context.ISubtract(laneId, index);
            Operand valid = context.ICompareGreaterOrEqual(srcThreadId, minThreadId);

            context.Copy(Argument(3), valid);

            Operand result = context.Shuffle(value, GenerateSubgroupShuffleIndex(context, srcThreadId, subgroupSize));

            context.Return(context.ConditionalSelect(valid, result, value));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "ShuffleUp", true, 3, 1);
        }

        private static Function GenerateShuffleXor(int subgroupSize)
        {
            EmitterContext context = new();

            Operand value = Argument(0);
            Operand index = Argument(1);
            Operand mask = Argument(2);

            Operand clamp = context.BitwiseAnd(mask, Const(0x1f));
            Operand segMask = context.BitwiseAnd(context.ShiftRightU32(mask, Const(8)), Const(0x1f));
            Operand laneId = GenerateLoadSubgroupLaneId(context, subgroupSize);
            Operand minThreadId = context.BitwiseAnd(laneId, segMask);
            Operand maxThreadId = context.BitwiseOr(context.BitwiseAnd(clamp, context.BitwiseNot(segMask)), minThreadId);
            Operand srcThreadId = context.BitwiseExclusiveOr(laneId, index);
            Operand valid = context.ICompareLessOrEqualUnsigned(srcThreadId, maxThreadId);

            context.Copy(Argument(3), valid);

            Operand result = context.Shuffle(value, GenerateSubgroupShuffleIndex(context, srcThreadId, subgroupSize));

            context.Return(context.ConditionalSelect(valid, result, value));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "ShuffleXor", true, 3, 1);
        }

        private static Operand GenerateLoadSubgroupLaneId(EmitterContext context, int subgroupSize)
        {
            if (subgroupSize <= 32)
            {
                return context.Load(StorageKind.Input, IoVariable.SubgroupLaneId);
            }

            return context.BitwiseAnd(context.Load(StorageKind.Input, IoVariable.SubgroupLaneId), Const(0x1f));
        }

        private static Operand GenerateSubgroupShuffleIndex(EmitterContext context, Operand srcThreadId, int subgroupSize)
        {
            if (subgroupSize <= 32)
            {
                return srcThreadId;
            }

            return context.BitwiseOr(
                context.BitwiseAnd(context.Load(StorageKind.Input, IoVariable.SubgroupLaneId), Const(0x60)),
                srcThreadId);
        }

        private Function GenerateTexelFetchScaleFunction()
        {
            EmitterContext context = new();

            Operand input = Argument(0);
            Operand samplerIndex = Argument(1);
            Operand index = GetScaleIndex(context, samplerIndex);

            Operand scale = LoadRenderScaleComponent(context, index, 0);

            Operand scaleIsOne = context.FPCompareEqual(scale, ConstF(1f));
            Operand lblScaleNotOne = Label();

            context.BranchIfFalse(lblScaleNotOne, scaleIsOne);
            context.Return(input);
            context.MarkLabel(lblScaleNotOne);

            int inArgumentsCount;

            if (_stage == ShaderStage.Fragment)
            {
                Operand scaleIsLessThanZero = context.FPCompareLess(scale, ConstF(0f));
                Operand lblScaleGreaterOrEqualZero = Label();

                context.BranchIfFalse(lblScaleGreaterOrEqualZero, scaleIsLessThanZero);

                Operand negScale = context.FPNegate(scale);
                Operand inputScaled = context.FPMultiply(context.IConvertS32ToFP32(input), negScale);
                Operand fragCoordX = context.Load(StorageKind.Input, IoVariable.FragmentCoord, null, Const(0));
                Operand fragCoordY = context.Load(StorageKind.Input, IoVariable.FragmentCoord, null, Const(1));
                Operand fragCoord = context.ConditionalSelect(Argument(2), fragCoordY, fragCoordX);
                Operand inputBias = context.FPModulo(fragCoord, negScale);
                Operand inputWithBias = context.FPAdd(inputScaled, inputBias);

                context.Return(context.FP32ConvertToS32(inputWithBias));
                context.MarkLabel(lblScaleGreaterOrEqualZero);

                inArgumentsCount = 3;
            }
            else
            {
                inArgumentsCount = 2;
            }

            Operand inputScaled2 = context.FPMultiply(context.IConvertS32ToFP32(input), scale);

            context.Return(context.FP32ConvertToS32(inputScaled2));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "TexelFetchScale", true, inArgumentsCount, 0);
        }

        private Function GenerateTextureSizeUnscaleFunction()
        {
            EmitterContext context = new();

            Operand input = Argument(0);
            Operand samplerIndex = Argument(1);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand layoutMarker = LoadRenderScaleComponent(context, index, 1);
            Operand component = Argument(2);

            Operand foldedLayout = context.FPCompareLess(layoutMarker, ConstF(-1.5f));
            Operand lblOrdinaryLayout = Label();

            context.BranchIfFalse(lblOrdinaryLayout, foldedLayout);

            Operand pageCount = context.FPAbsolute(layoutMarker);
            Operand pagesInt = context.FP32ConvertToS32(pageCount);
            Operand logicalWidth = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 2));
            Operand pageHeight = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));
            Operand logicalHeight = context.IMultiply(pageHeight, pagesInt);

            context.Return(context.ConditionalSelect(
                context.ICompareEqual(component, Const(0)),
                logicalWidth,
                context.ConditionalSelect(context.ICompareEqual(component, Const(1)), logicalHeight, input)));
            context.MarkLabel(lblOrdinaryLayout);

            Operand scale = context.FPAbsolute(LoadRenderScaleComponent(context, index, 0));

            Operand scaleIsOne = context.FPCompareEqual(scale, ConstF(1f));
            Operand lblScaleNotOne = Label();

            context.BranchIfFalse(lblScaleNotOne, scaleIsOne);
            context.Return(input);
            context.MarkLabel(lblScaleNotOne);

            Operand inputUnscaled = context.FPDivide(context.IConvertS32ToFP32(input), scale);

            context.Return(context.FP32ConvertToS32(inputUnscaled));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "TextureSizeUnscale", true, 3, 0);
        }

        private Function GenerateFoldedTextureCoordXFunction()
        {
            EmitterContext context = new();

            Operand inputX = Argument(0);
            Operand inputY = Argument(1);
            Operand samplerIndex = Argument(2);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand layoutMarker = LoadRenderScaleComponent(context, index, 1);
            Operand foldedLayout = context.FPCompareLess(layoutMarker, ConstF(-1.5f));
            Operand lblFolded = Label();

            context.BranchIfTrue(lblFolded, foldedLayout);
            context.Return(inputX);
            context.MarkLabel(lblFolded);

            Operand logicalWidth = LoadRenderScaleComponent(context, index, 2);
            Operand pageHeight = LoadRenderScaleComponent(context, index, 3);
            Operand pageCount = context.FPAbsolute(layoutMarker);
            Operand logicalHeight = context.FPMultiply(pageHeight, pageCount);
            Operand yScaled = context.FPMultiply(inputY, logicalHeight);
            Operand page = context.FPMinimum(
                context.FPMaximum(context.FPFloor(context.FPDivide(yScaled, pageHeight)), ConstF(0f)),
                context.FPSubtract(pageCount, ConstF(1f)));
            Operand pageStrideWidth = context.FPAdd(logicalWidth, ConstF(FoldedLinearTextureGutterX * 2f));
            Operand hostWidth = context.FPMultiply(pageStrideWidth, pageCount);
            Operand hostX = context.FPAdd(
                context.FPAdd(context.FPMultiply(page, pageStrideWidth), ConstF(FoldedLinearTextureGutterX)),
                context.FPMultiply(inputX, logicalWidth));

            context.Return(context.FPDivide(hostX, hostWidth));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "FoldedTextureCoordX", true, 3, 0);
        }

        private Function GenerateFoldedTextureCoordYFunction()
        {
            EmitterContext context = new();

            Operand inputY = Argument(0);
            Operand samplerIndex = Argument(1);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand layoutMarker = LoadRenderScaleComponent(context, index, 1);
            Operand foldedLayout = context.FPCompareLess(layoutMarker, ConstF(-1.5f));
            Operand lblFolded = Label();

            context.BranchIfTrue(lblFolded, foldedLayout);
            context.Return(inputY);
            context.MarkLabel(lblFolded);

            Operand pageHeight = LoadRenderScaleComponent(context, index, 3);
            Operand pageCount = context.FPAbsolute(layoutMarker);
            Operand logicalHeight = context.FPMultiply(pageHeight, pageCount);
            Operand yScaled = context.FPMultiply(inputY, logicalHeight);
            Operand page = context.FPMinimum(
                context.FPMaximum(context.FPFloor(context.FPDivide(yScaled, pageHeight)), ConstF(0f)),
                context.FPSubtract(pageCount, ConstF(1f)));
            Operand localY = context.FPSubtract(yScaled, context.FPMultiply(page, pageHeight));
            Operand hostHeight = context.FPAdd(pageHeight, ConstF(FoldedLinearTextureGutterY * 2f));

            context.Return(context.FPDivide(
                context.FPAdd(localY, ConstF(FoldedLinearTextureGutterY)),
                hostHeight));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "FoldedTextureCoordY", true, 2, 0);
        }

        private Function GenerateFoldedTexelFetchCoordXFunction()
        {
            EmitterContext context = new();

            Operand inputX = Argument(0);
            Operand inputY = Argument(1);
            Operand samplerIndex = Argument(2);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand layoutMarker = LoadRenderScaleComponent(context, index, 1);
            Operand foldedLayout = context.FPCompareLess(layoutMarker, ConstF(-1.5f));
            Operand lblFolded = Label();

            context.BranchIfTrue(lblFolded, foldedLayout);
            context.Return(inputX);
            context.MarkLabel(lblFolded);

            Operand width = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 2));
            Operand pageHeight = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));
            Operand pagesInt = context.FP32ConvertToS32(context.FPAbsolute(layoutMarker));
            Operand guestHeight = context.IMultiply(pageHeight, pagesInt);
            Operand x = context.IMinimumS32(context.IMaximumS32(inputX, Const(0)), context.ISubtract(width, Const(1)));
            Operand y = context.IMinimumS32(context.IMaximumS32(inputY, Const(0)), context.ISubtract(guestHeight, Const(1)));
            Operand page = context.Add(Instruction.Divide, Local(), y, pageHeight);
            Operand pageStrideWidth = context.IAdd(width, Const(FoldedLinearTextureGutterX * 2));

            context.Return(context.IAdd(
                context.IAdd(x, context.IMultiply(page, pageStrideWidth)),
                Const(FoldedLinearTextureGutterX)));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "FoldedTexelFetchCoordX", true, 3, 0);
        }

        private Function GenerateFoldedTexelFetchCoordYFunction()
        {
            EmitterContext context = new();

            Operand inputY = Argument(0);
            Operand samplerIndex = Argument(1);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand layoutMarker = LoadRenderScaleComponent(context, index, 1);
            Operand foldedLayout = context.FPCompareLess(layoutMarker, ConstF(-1.5f));
            Operand lblFolded = Label();

            context.BranchIfTrue(lblFolded, foldedLayout);
            context.Return(inputY);
            context.MarkLabel(lblFolded);

            Operand pageHeight = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));
            Operand pagesInt = context.FP32ConvertToS32(context.FPAbsolute(layoutMarker));
            Operand guestHeight = context.IMultiply(pageHeight, pagesInt);
            Operand y = context.IMinimumS32(context.IMaximumS32(inputY, Const(0)), context.ISubtract(guestHeight, Const(1)));
            Operand page = context.Add(Instruction.Divide, Local(), y, pageHeight);
            Operand localY = context.ISubtract(y, context.IMultiply(page, pageHeight));

            context.Return(context.IAdd(localY, Const(FoldedLinearTextureGutterY)));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "FoldedTexelFetchCoordY", true, 2, 0);
        }

        private Function GenerateBufferTexture2DNearestIndexIntFunction()
        {
            EmitterContext context = new();

            Operand x = Argument(0);
            Operand y = Argument(1);
            Operand samplerIndex = Argument(2);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand width = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 1));
            Operand height = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 2));
            Operand stride = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));

            x = context.IMinimumS32(context.IMaximumS32(x, Const(0)), context.ISubtract(width, Const(1)));
            y = context.IMinimumS32(context.IMaximumS32(y, Const(0)), context.ISubtract(height, Const(1)));

            context.Return(context.IAdd(context.IMultiply(y, stride), x));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "BufferTexture2DNearestIndexInt", true, 3, 0);
        }

        private Function GenerateBufferTexture2DNearestIndexFunction()
        {
            EmitterContext context = new();

            Operand coordX = Argument(0);
            Operand coordY = Argument(1);
            Operand samplerIndex = Argument(2);
            Operand normalized = Argument(3);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand width = LoadRenderScaleComponent(context, index, 1);
            Operand height = LoadRenderScaleComponent(context, index, 2);
            Operand stride = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));
            Operand isNormalized = context.ICompareNotEqual(normalized, Const(0));

            Operand texelX = context.ConditionalSelect(isNormalized, context.FPMultiply(coordX, width), coordX);
            Operand texelY = context.ConditionalSelect(isNormalized, context.FPMultiply(coordY, height), coordY);

            Operand x = context.FP32ConvertToS32(context.FPFloor(texelX));
            Operand y = context.FP32ConvertToS32(context.FPFloor(texelY));
            Operand maxX = context.ISubtract(context.FP32ConvertToS32(width), Const(1));
            Operand maxY = context.ISubtract(context.FP32ConvertToS32(height), Const(1));

            x = context.IMinimumS32(context.IMaximumS32(x, Const(0)), maxX);
            y = context.IMinimumS32(context.IMaximumS32(y, Const(0)), maxY);

            context.Return(context.IAdd(context.IMultiply(y, stride), x));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "BufferTexture2DNearestIndex", true, 4, 0);
        }

        private Function GenerateBufferTexture2DBilinearIndicesFunction()
        {
            EmitterContext context = new();

            Operand coordX = Argument(0);
            Operand coordY = Argument(1);
            Operand samplerIndex = Argument(2);
            Operand normalized = Argument(3);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand width = LoadRenderScaleComponent(context, index, 1);
            Operand height = LoadRenderScaleComponent(context, index, 2);
            Operand stride = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 3));
            Operand isNormalized = context.ICompareNotEqual(normalized, Const(0));
            Operand texelX = context.ConditionalSelect(isNormalized, context.FPMultiply(coordX, width), coordX);
            Operand texelY = context.ConditionalSelect(isNormalized, context.FPMultiply(coordY, height), coordY);

            texelX = context.FPSubtract(texelX, ConstF(0.5f));
            texelY = context.FPSubtract(texelY, ConstF(0.5f));

            Operand floorX = context.FPFloor(texelX);
            Operand floorY = context.FPFloor(texelY);
            Operand x0 = context.FP32ConvertToS32(floorX);
            Operand y0 = context.FP32ConvertToS32(floorY);
            Operand x1 = context.IAdd(x0, Const(1));
            Operand y1 = context.IAdd(y0, Const(1));
            Operand maxX = context.ISubtract(context.FP32ConvertToS32(width), Const(1));
            Operand maxY = context.ISubtract(context.FP32ConvertToS32(height), Const(1));

            x0 = context.IMinimumS32(context.IMaximumS32(x0, Const(0)), maxX);
            x1 = context.IMinimumS32(context.IMaximumS32(x1, Const(0)), maxX);
            y0 = context.IMinimumS32(context.IMaximumS32(y0, Const(0)), maxY);
            y1 = context.IMinimumS32(context.IMaximumS32(y1, Const(0)), maxY);

            Operand row0 = context.IMultiply(y0, stride);
            Operand row1 = context.IMultiply(y1, stride);
            Operand index00 = context.IAdd(row0, x0);
            Operand index10 = context.IAdd(row0, x1);
            Operand index01 = context.IAdd(row1, x0);
            Operand index11 = context.IAdd(row1, x1);

            context.Copy(Argument(4), index10);
            context.Copy(Argument(5), index01);
            context.Copy(Argument(6), index11);
            context.Copy(Argument(7), context.FPSubtract(texelX, floorX));
            context.Copy(Argument(8), context.FPSubtract(texelY, floorY));
            context.Return(index00);

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "BufferTexture2DBilinearIndices", true, 4, 5);
        }

        private Function GenerateBufferTexture2DSizeFunction()
        {
            EmitterContext context = new();

            Operand samplerIndex = Argument(0);
            Operand component = Argument(1);
            Operand index = GetScaleIndex(context, samplerIndex);
            Operand width = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 1));
            Operand height = context.FP32ConvertToS32(LoadRenderScaleComponent(context, index, 2));

            context.Return(context.ConditionalSelect(context.ICompareEqual(component, Const(0)), width, height));

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "BufferTexture2DSize", true, 2, 0);
        }

        private static Function GeneratePagedTexture2DNearestCoordsIntFunction()
        {
            EmitterContext context = new();

            Operand x = Argument(0);
            Operand y = Argument(1);
            Operand width = Argument(2);
            Operand pageHeight = Argument(3);
            Operand pageCount = Argument(4);
            Operand guestHeight = context.IMultiply(pageHeight, pageCount);

            x = context.IMinimumS32(context.IMaximumS32(x, Const(0)), context.ISubtract(width, Const(1)));
            y = context.IMinimumS32(context.IMaximumS32(y, Const(0)), context.ISubtract(guestHeight, Const(1)));

            Operand pageIndex = context.FP32ConvertToS32(context.FPFloor(context.FPDivide(
                context.IConvertS32ToFP32(y),
                context.IConvertS32ToFP32(pageHeight))));
            Operand pageY = context.ISubtract(y, context.IMultiply(pageIndex, pageHeight));

            context.Copy(Argument(5), pageY);
            context.Copy(Argument(6), pageIndex);
            context.Return(x);

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "PagedTexture2DNearestCoordsInt", true, 5, 2);
        }

        private static Function GeneratePagedTexture2DNearestCoordsFunction()
        {
            EmitterContext context = new();

            Operand coordX = Argument(0);
            Operand coordY = Argument(1);
            Operand width = Argument(2);
            Operand pageHeight = Argument(3);
            Operand pageCount = Argument(4);
            Operand normalized = Argument(5);
            Operand guestHeight = context.IMultiply(pageHeight, pageCount);
            Operand isNormalized = context.ICompareNotEqual(normalized, Const(0));

            coordX = context.ConditionalSelect(
                isNormalized,
                context.FPMultiply(coordX, context.IConvertS32ToFP32(width)),
                coordX);
            coordY = context.ConditionalSelect(
                isNormalized,
                context.FPMultiply(coordY, context.IConvertS32ToFP32(guestHeight)),
                coordY);

            Operand x = context.FP32ConvertToS32(context.FPFloor(coordX));
            Operand y = context.FP32ConvertToS32(context.FPFloor(coordY));

            x = context.IMinimumS32(context.IMaximumS32(x, Const(0)), context.ISubtract(width, Const(1)));
            y = context.IMinimumS32(context.IMaximumS32(y, Const(0)), context.ISubtract(guestHeight, Const(1)));

            Operand pageIndex = context.FP32ConvertToS32(context.FPFloor(context.FPDivide(
                context.IConvertS32ToFP32(y),
                context.IConvertS32ToFP32(pageHeight))));
            Operand pageY = context.ISubtract(y, context.IMultiply(pageIndex, pageHeight));

            context.Copy(Argument(6), pageY);
            context.Copy(Argument(7), pageIndex);
            context.Return(x);

            return new Function(ControlFlowGraph.Create(context.GetOperations()).Blocks, "PagedTexture2DNearestCoords", true, 6, 2);
        }

        private Operand GetScaleIndex(EmitterContext context, Operand index)
        {
            switch (_stage)
            {
                case ShaderStage.Vertex:
                    Operand fragScaleCount = context.Load(StorageKind.ConstantBuffer, 0, Const((int)SupportBufferField.FragmentRenderScaleCount));
                    return context.IAdd(Const(1), context.IAdd(index, fragScaleCount));
                default:
                    return context.IAdd(Const(1), index);
            }
        }

        private static Operand LoadRenderScaleComponent(EmitterContext context, Operand index, int component)
        {
            return context.Load(StorageKind.ConstantBuffer, 0, Const((int)SupportBufferField.RenderScale), index, Const(component));
        }

        public static Operand GetBitOffset(EmitterContext context, Operand offset)
        {
            return context.ShiftLeft(context.BitwiseAnd(offset, Const(3)), Const(3));
        }

        private static Operand GenerateSharedAtomicCasLoop(EmitterContext context, Operand wordOffset, int id, Func<Operand, Operand> opCallback)
        {
            Operand lblLoopHead = Label();

            context.MarkLabel(lblLoopHead);

            Operand oldValue = context.Load(StorageKind.SharedMemory, id, wordOffset);
            Operand newValue = opCallback(oldValue);

            Operand casResult = context.AtomicCompareAndSwap(StorageKind.SharedMemory, id, wordOffset, oldValue, newValue);

            Operand casFail = context.ICompareNotEqual(casResult, oldValue);

            context.BranchIfTrue(lblLoopHead, casFail);

            return oldValue;
        }
    }
}
