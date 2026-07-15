using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation.Optimizations;
using System.Collections.Generic;

using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Graphics.Shader.Translation.Transforms
{
    class GeometryToCompute : ITransformPass
    {
        public static bool IsEnabled(IGpuAccessor gpuAccessor, ShaderStage stage, TargetLanguage targetLanguage, FeatureFlags usedFeatures)
        {
            return usedFeatures.HasFlag(FeatureFlags.VtgAsCompute);
        }

        public static LinkedListNode<INode> RunPass(TransformContext context, LinkedListNode<INode> node)
        {
            if (context.Definitions.Stage != ShaderStage.Geometry)
            {
                return node;
            }

            Operation operation = (Operation)node.Value;

            LinkedListNode<INode> newNode = node;

            switch (operation.Inst)
            {
                case Instruction.EmitVertex:
                    newNode = GenerateEmitVertex(context.Definitions, context.ResourceManager, node);
                    break;
                case Instruction.EndPrimitive:
                    newNode = GenerateEndPrimitive(context.Definitions, context.ResourceManager, node);
                    break;
                case Instruction.Load:
                    if (operation.StorageKind == StorageKind.Input)
                    {
                        IoVariable ioVariable = (IoVariable)operation.GetSource(0).Value;

                        if (TryGetInputOffset(context, node, operation, out Operand inputOffset, out Operand primVertex))
                        {
                            Operand vertexElemOffset = GenerateVertexOffset(context.ResourceManager, node, inputOffset, primVertex);

                            newNode = node.List.AddBefore(node, new Operation(
                                Instruction.Load,
                                StorageKind.StorageBuffer,
                                operation.Dest,
                                new[] { Const(context.ResourceManager.Reservations.VertexOutputStorageBufferBinding), Const(0), vertexElemOffset }));
                        }
                        else
                        {
                            switch (ioVariable)
                            {
                                case IoVariable.InvocationId:
                                    newNode = GenerateInvocationId(node, operation.Dest);
                                    break;
                                case IoVariable.PrimitiveId:
                                    newNode = GeneratePrimitiveId(context.ResourceManager, node, operation.Dest);
                                    break;
                                case IoVariable.GlobalId:
                                case IoVariable.SubgroupEqMask:
                                case IoVariable.SubgroupGeMask:
                                case IoVariable.SubgroupGtMask:
                                case IoVariable.SubgroupLaneId:
                                case IoVariable.SubgroupLeMask:
                                case IoVariable.SubgroupLtMask:
                                    // Those are valid or expected for geometry shaders.
                                    break;
                                default:
                                    context.GpuAccessor.Log($"Invalid input \"{ioVariable}\" during geometry compute conversion " +
                                        $"(sources={FormatSources(operation)}, iaIndexing={context.Definitions.IaIndexing}).");
                                    operation.TurnIntoCopy(Const(0));
                                    break;
                            }
                        }
                    }
                    else if (operation.StorageKind == StorageKind.Output)
                    {
                        if (TryGetOutputOffset(context, node, operation, out Operand outputOffset))
                        {
                            newNode = node.List.AddBefore(node, new Operation(
                                Instruction.Load,
                                StorageKind.LocalMemory,
                                operation.Dest,
                                new[] { Const(context.ResourceManager.LocalVertexDataMemoryId), outputOffset }));
                        }
                        else
                        {
                            context.GpuAccessor.Log($"Invalid output load \"{(IoVariable)operation.GetSource(0).Value}\" during geometry compute conversion " +
                                $"(sources={FormatSources(operation)}, oaIndexing={context.Definitions.OaIndexing}).");
                            operation.TurnIntoCopy(Const(0));
                        }
                    }

                    break;
                case Instruction.Store:
                    if (operation.StorageKind == StorageKind.Output)
                    {
                        if (TryGetOutputOffset(context, node, operation, out Operand outputOffset))
                        {
                            Operand value = operation.GetSource(operation.SourcesCount - 1);

                            newNode = node.List.AddBefore(node, new Operation(
                                Instruction.Store,
                                StorageKind.LocalMemory,
                                (Operand)null,
                                new[] { Const(context.ResourceManager.LocalVertexDataMemoryId), outputOffset, value }));
                        }
                        else
                        {
                            context.GpuAccessor.Log($"Invalid output store \"{(IoVariable)operation.GetSource(0).Value}\" during geometry compute conversion " +
                                $"(sources={FormatSources(operation)}, oaIndexing={context.Definitions.OaIndexing}).");
                            operation.Detach();
                            node.Value = new CommentNode("Dropped unmapped geometry output during compute conversion.");
                        }
                    }

                    break;
            }

            if (newNode != node)
            {
                Utils.DeleteNode(node, operation);
            }

            return newNode;
        }

        private static LinkedListNode<INode> GenerateEmitVertex(ShaderDefinitions definitions, ResourceManager resourceManager, LinkedListNode<INode> node)
        {
            int vbOutputBinding = resourceManager.Reservations.GeometryVertexOutputStorageBufferBinding;
            int ibOutputBinding = resourceManager.Reservations.GeometryIndexOutputStorageBufferBinding;
            int stride = resourceManager.Reservations.OutputSizePerInvocation;

            Operand outputPrimVertex = IncrementLocalMemory(node, resourceManager.LocalGeometryOutputVertexCountMemoryId);
            Operand baseVertexOffset = GenerateBaseOffset(
                resourceManager,
                node,
                definitions.MaxOutputVertices * definitions.ThreadsPerInputPrimitive,
                definitions.ThreadsPerInputPrimitive);
            Operand outputBaseVertex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, outputBaseVertex, new[] { baseVertexOffset, outputPrimVertex }));

            Operand outputPrimIndex = IncrementLocalMemory(node, resourceManager.LocalGeometryOutputIndexCountMemoryId);
            Operand baseIndexOffset = GenerateBaseOffset(
                resourceManager,
                node,
                definitions.GetGeometryOutputIndexBufferStride(),
                definitions.ThreadsPerInputPrimitive);
            Operand outputBaseIndex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, outputBaseIndex, new[] { baseIndexOffset, outputPrimIndex }));

            node.List.AddBefore(node, new Operation(
                Instruction.Store,
                StorageKind.StorageBuffer,
                null,
                new[] { Const(ibOutputBinding), Const(0), outputBaseIndex, outputBaseVertex }));

            Operand baseOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, baseOffset, new[] { outputBaseVertex, Const(stride) }));

            LinkedListNode<INode> newNode = node;

            for (int offset = 0; offset < stride; offset++)
            {
                Operand vertexOffset;

                if (offset > 0)
                {
                    vertexOffset = Local();
                    node.List.AddBefore(node, new Operation(Instruction.Add, vertexOffset, new[] { baseOffset, Const(offset) }));
                }
                else
                {
                    vertexOffset = baseOffset;
                }

                Operand value = Local();
                node.List.AddBefore(node, new Operation(
                    Instruction.Load,
                    StorageKind.LocalMemory,
                    value,
                    new[] { Const(resourceManager.LocalVertexDataMemoryId), Const(offset) }));

                newNode = node.List.AddBefore(node, new Operation(
                    Instruction.Store,
                    StorageKind.StorageBuffer,
                    null,
                    new[] { Const(vbOutputBinding), Const(0), vertexOffset, value }));
            }

            return newNode;
        }

        private static LinkedListNode<INode> GenerateEndPrimitive(ShaderDefinitions definitions, ResourceManager resourceManager, LinkedListNode<INode> node)
        {
            int ibOutputBinding = resourceManager.Reservations.GeometryIndexOutputStorageBufferBinding;

            Operand outputPrimIndex = IncrementLocalMemory(node, resourceManager.LocalGeometryOutputIndexCountMemoryId);
            Operand baseIndexOffset = GenerateBaseOffset(
                resourceManager,
                node,
                definitions.GetGeometryOutputIndexBufferStride(),
                definitions.ThreadsPerInputPrimitive);
            Operand outputBaseIndex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, outputBaseIndex, new[] { baseIndexOffset, outputPrimIndex }));

            return node.List.AddBefore(node, new Operation(
                Instruction.Store,
                StorageKind.StorageBuffer,
                null,
                new[] { Const(ibOutputBinding), Const(0), outputBaseIndex, Const(-1) }));
        }

        private static Operand GenerateBaseOffset(ResourceManager resourceManager, LinkedListNode<INode> node, int stride, int threadsPerInputPrimitive)
        {
            Operand primitiveId = Local();
            GeneratePrimitiveId(resourceManager, node, primitiveId);

            Operand baseOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, baseOffset, new[] { primitiveId, Const(stride) }));

            Operand invocationId = Local();
            GenerateInvocationId(node, invocationId);

            Operand invocationOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, invocationOffset, new[] { invocationId, Const(stride / threadsPerInputPrimitive) }));

            Operand combinedOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, combinedOffset, new[] { baseOffset, invocationOffset }));

            return combinedOffset;
        }

        private static Operand IncrementLocalMemory(LinkedListNode<INode> node, int memoryId)
        {
            Operand oldValue = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.LocalMemory,
                oldValue,
                new[] { Const(memoryId) }));

            Operand newValue = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, newValue, new[] { oldValue, Const(1) }));

            node.List.AddBefore(node, new Operation(Instruction.Store, StorageKind.LocalMemory, null, new[] { Const(memoryId), newValue }));

            return oldValue;
        }

        private static Operand GenerateVertexOffset(
            ResourceManager resourceManager,
            LinkedListNode<INode> node,
            Operand elementOffset,
            Operand primVertex)
        {
            int vertexInfoCbBinding = resourceManager.Reservations.VertexInfoConstantBufferBinding;

            Operand vertexCount = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.ConstantBuffer,
                vertexCount,
                new[] { Const(vertexInfoCbBinding), Const((int)VertexInfoBufferField.VertexCounts), Const(0) }));

            Operand primInputVertex = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.LocalMemory,
                primInputVertex,
                new[] { Const(resourceManager.LocalTopologyRemapMemoryId), primVertex }));

            Operand instanceIndex = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.Input,
                instanceIndex,
                new[] { Const((int)IoVariable.GlobalId), Const(1) }));

            Operand baseVertex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, baseVertex, new[] { instanceIndex, vertexCount }));

            Operand vertexIndex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, vertexIndex, new[] { baseVertex, primInputVertex }));

            Operand vertexBaseOffset = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Multiply,
                vertexBaseOffset,
                new[] { vertexIndex, Const(resourceManager.Reservations.InputSizePerInvocation) }));

            Operand vertexElemOffset;

            if (elementOffset.Type != OperandType.Constant || elementOffset.Value != 0)
            {
                vertexElemOffset = Local();

                node.List.AddBefore(node, new Operation(Instruction.Add, vertexElemOffset, new[] { vertexBaseOffset, elementOffset }));
            }
            else
            {
                vertexElemOffset = vertexBaseOffset;
            }

            return vertexElemOffset;
        }

        private static LinkedListNode<INode> GeneratePrimitiveId(ResourceManager resourceManager, LinkedListNode<INode> node, Operand dest)
        {
            int vertexInfoCbBinding = resourceManager.Reservations.VertexInfoConstantBufferBinding;

            Operand primitivesCount = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.ConstantBuffer,
                primitivesCount,
                new[] { Const(vertexInfoCbBinding), Const((int)VertexInfoBufferField.GeometryCounts), Const(0) }));

            // Geometry output is packed by input primitive, not by input vertex.
            Operand vertexIndex = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.Input,
                vertexIndex,
                new[] { Const((int)IoVariable.GlobalId), Const(0) }));

            Operand instanceIndex = Local();
            node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.Input,
                instanceIndex,
                new[] { Const((int)IoVariable.GlobalId), Const(1) }));

            Operand baseVertex = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, baseVertex, new[] { instanceIndex, primitivesCount }));

            return node.List.AddBefore(node, new Operation(Instruction.Add, dest, new[] { baseVertex, vertexIndex }));
        }

        private static LinkedListNode<INode> GenerateInvocationId(LinkedListNode<INode> node, Operand dest)
        {
            return node.List.AddBefore(node, new Operation(
                Instruction.Load,
                StorageKind.Input,
                dest,
                new[] { Const((int)IoVariable.GlobalId), Const(2) }));
        }

        private static bool TryGetOffset(ResourceManager resourceManager, Operation operation, StorageKind storageKind, out int outputOffset)
        {
            bool isStore = operation.Inst == Instruction.Store;

            IoVariable ioVariable = (IoVariable)operation.GetSource(0).Value;

            bool isValidOutput;

            if (ioVariable == IoVariable.UserDefined)
            {
                int lastIndex = operation.SourcesCount - (isStore ? 2 : 1);

                int location = operation.GetSource(1).Value;
                int component = operation.GetSource(lastIndex).Value;

                isValidOutput = resourceManager.Reservations.TryGetOffset(storageKind, location, component, out outputOffset);
            }
            else
            {
                if (ResourceReservations.IsVectorOrArrayVariable(ioVariable))
                {
                    int component = operation.GetSource(operation.SourcesCount - (isStore ? 2 : 1)).Value;

                    isValidOutput = resourceManager.Reservations.TryGetOffset(storageKind, ioVariable, component, out outputOffset);
                }
                else
                {
                    isValidOutput = resourceManager.Reservations.TryGetOffset(storageKind, ioVariable, out outputOffset);
                }
            }

            return isValidOutput;
        }

        private static bool TryGetInputOffset(
            TransformContext context,
            LinkedListNode<INode> node,
            Operation operation,
            out Operand inputOffset,
            out Operand primVertex)
        {
            IoVariable ioVariable = (IoVariable)operation.GetSource(0).Value;

            if (ioVariable == IoVariable.UserDefined)
            {
                bool isIndexed = context.Definitions.IaIndexing;

                if (isIndexed && operation.SourcesCount == 3)
                {
                    primVertex = operation.GetSource(1);

                    return TryGetUserDefinedLinearOffset(
                        context,
                        node,
                        StorageKind.Input,
                        operation.GetSource(2),
                        out inputOffset);
                }

                if (operation.SourcesCount < 4)
                {
                    inputOffset = null;
                    primVertex = null;
                    return false;
                }

                // Normal geometry attribute loads are emitted as (location, vertex, component),
                // while physically indexed loads use (vertex, location, component).
                int locationIndex = isIndexed ? 2 : 1;
                int componentIndex = operation.SourcesCount - 1;

                primVertex = isIndexed ? operation.GetSource(1) : operation.GetSource(2);

                return TryGetUserDefinedOffset(
                    context,
                    node,
                    StorageKind.Input,
                    operation.GetSource(locationIndex),
                    operation.GetSource(componentIndex),
                    isIndexed,
                    out inputOffset);
            }

            if (TryGetOffset(context.ResourceManager, operation, StorageKind.Input, out int fixedOffset))
            {
                if (operation.SourcesCount < 2)
                {
                    inputOffset = null;
                    primVertex = null;
                    return false;
                }

                primVertex = operation.GetSource(1);
                inputOffset = Const(fixedOffset);
                return true;
            }

            inputOffset = null;
            primVertex = null;
            return false;
        }

        private static bool TryGetOutputOffset(
            TransformContext context,
            LinkedListNode<INode> node,
            Operation operation,
            out Operand outputOffset)
        {
            IoVariable ioVariable = (IoVariable)operation.GetSource(0).Value;

            if (ioVariable == IoVariable.UserDefined)
            {
                bool isStore = operation.Inst == Instruction.Store;
                bool isIndexed = context.Definitions.OaIndexing;

                if (isIndexed &&
                    ((isStore && operation.SourcesCount == 3) ||
                     (!isStore && operation.SourcesCount == 2)))
                {
                    return TryGetUserDefinedLinearOffset(
                        context,
                        node,
                        StorageKind.Output,
                        operation.GetSource(1),
                        out outputOffset);
                }

                int componentIndex = operation.SourcesCount - (isStore ? 2 : 1);

                return TryGetUserDefinedOffset(
                    context,
                    node,
                    StorageKind.Output,
                    operation.GetSource(1),
                    operation.GetSource(componentIndex),
                    isIndexed,
                    out outputOffset);
            }

            if (TryGetOffset(context.ResourceManager, operation, StorageKind.Output, out int fixedOffset))
            {
                outputOffset = Const(fixedOffset);
                return true;
            }

            outputOffset = null;
            return false;
        }

        private static bool TryGetUserDefinedOffset(
            TransformContext context,
            LinkedListNode<INode> node,
            StorageKind storageKind,
            Operand location,
            Operand component,
            bool isIndexed,
            out Operand offset)
        {
            ResourceReservations reservations = context.ResourceManager.Reservations;

            if (!isIndexed)
            {
                if (location.Type == OperandType.Constant &&
                    component.Type == OperandType.Constant &&
                    reservations.TryGetOffset(storageKind, location.Value, component.Value, out int fixedOffset))
                {
                    offset = Const(fixedOffset);
                    return true;
                }

                offset = null;
                return false;
            }

            Operand locationOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Multiply, locationOffset, new[] { location, Const(4) }));

            Operand attributeOffset = Local();
            node.List.AddBefore(node, new Operation(Instruction.Add, attributeOffset, new[] { locationOffset, component }));

            return TryGetUserDefinedLinearOffset(context, node, storageKind, attributeOffset, out offset);
        }

        private static bool TryGetUserDefinedLinearOffset(
            TransformContext context,
            LinkedListNode<INode> node,
            StorageKind storageKind,
            Operand attributeOffset,
            out Operand offset)
        {
            if (!context.ResourceManager.Reservations.TryGetOffset(storageKind, 0, 0, out int baseOffset))
            {
                offset = null;
                return false;
            }

            if (baseOffset == 0)
            {
                offset = attributeOffset;
            }
            else
            {
                offset = Local();
                node.List.AddBefore(node, new Operation(Instruction.Add, offset, new[] { attributeOffset, Const(baseOffset) }));
            }

            return true;
        }

        private static string FormatSources(Operation operation)
        {
            string result = operation.SourcesCount.ToString();

            for (int index = 0; index < operation.SourcesCount; index++)
            {
                Operand source = operation.GetSource(index);
                result += $" [{index}:{source.Type}:{source.Value}]";
            }

            return result;
        }
    }
}
