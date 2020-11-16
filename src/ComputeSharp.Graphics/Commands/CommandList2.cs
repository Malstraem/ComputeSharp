﻿using System;
using ComputeSharp.Graphics.Buffers.Abstract;
using ComputeSharp.Graphics.Extensions;
using TerraFX.Interop;
using static TerraFX.Interop.D3D12_COMMAND_LIST_TYPE;

namespace ComputeSharp.Graphics.Commands
{
    /// <summary>
    /// A command list to set and execute operaations on the GPU.
    /// </summary>
    internal readonly unsafe ref struct CommandList2
    {
        /// <summary>
        /// The <see cref="GraphicsDevice2"/> instance associated with the current command list.
        /// </summary>
        private readonly GraphicsDevice2 device;

        /// <summary>
        /// The command list type being used by the current instance.
        /// </summary>
        private readonly D3D12_COMMAND_LIST_TYPE d3d12CommandListType;

        /// <summary>
        /// The <see cref="ID3D12CommandAllocator"/> object in use by the current instance.
        /// </summary>
        private readonly ComPtr<ID3D12CommandAllocator> d3D12CommandAllocator;

        /// <summary>
        /// The <see cref="ID3D12GraphicsCommandList"/> object in use by the current instance.
        /// </summary>
        private readonly ComPtr<ID3D12GraphicsCommandList> d3D12GraphicsCommandList;

        /// <summary>
        /// Creates a new <see cref="CommandList2"/> instance with the specified parameters.
        /// </summary>
        /// <param name="device">The target <see cref="GraphicsDevice2"/> instance to use.</param>
        /// <param name="d3d12CommandListType">The type of command list to create.</param>
        public CommandList2(GraphicsDevice2 device, D3D12_COMMAND_LIST_TYPE d3d12CommandListType)
        {
            this.device = device;
            this.d3d12CommandListType = d3d12CommandListType;
            this.d3D12CommandAllocator = device.GetCommandAllocator(d3d12CommandListType);
            this.d3D12GraphicsCommandList = device.D3D12Device->CreateCommandList(d3d12CommandListType, this.d3D12CommandAllocator);

            // Set the heap descriptor if the command list is not for copy operations
            if (d3d12CommandListType is not D3D12_COMMAND_LIST_TYPE_COPY)
            {
                device.SetDescriptorHeapForCommandList(this.d3D12GraphicsCommandList);
            }
        }

        /// <summary>
        /// Copies a memory region from one resource to another.
        /// </summary>
        /// <param name="d3D12ResourceSource">The source <see cref="ID3D12Resource"/> to read from.</param>
        /// <param name="sourceOffset">The starting offset to read the source resource from.</param>
        /// <param name="d3d12ResourceDestination">The destination <see cref="ID3D12Resource"/> to write to.</param>
        /// <param name="destinationOffset">The starting offset to write the destination resource from.</param>
        /// <param name="numBytes">The total number of bytes to copy from one resource to another.</param>
        public void CopyBufferRegion(ID3D12Resource* d3D12ResourceSource, int sourceOffset, ID3D12Resource* d3d12ResourceDestination, int destinationOffset, int numBytes)
        {
            this.d3D12GraphicsCommandList.Get()->CopyBufferRegion(d3d12ResourceDestination, (uint)destinationOffset, d3D12ResourceSource, (uint)sourceOffset, (uint)numBytes);
        }

        /// <summary>
        /// Binds an input <see cref="GraphicsResource"/> object to a specified root parameter.
        /// </summary>
        /// <param name="rootParameterIndex">The root parameter index to bind to the input resource.</param>
        /// <param name="resource">The input <see cref="GraphicsResource"/> instance to bind.</param>
        public void SetComputeRootDescriptorTable(int rootParameterIndex, D3D12_GPU_DESCRIPTOR_HANDLE d3D12GpuDescriptorHandle)
        {
            this.d3D12GraphicsCommandList.Get()->SetComputeRootDescriptorTable((uint)rootParameterIndex, d3D12GpuDescriptorHandle);
        }

        /// <summary>
        /// Sets a given <see cref="PipelineState"/> object ready to be executed.
        /// </summary>
        /// <param name="pipelineState">The input <see cref="PipelineState"/> to setup.</param>
        public void SetPipelineData(PipelineData pipelineData)
        {
            this.d3D12GraphicsCommandList.Get()->SetComputeRootSignature(pipelineData.D3D12RootSignature);
            this.d3D12GraphicsCommandList.Get()->SetPipelineState(pipelineData.D3D12PipelineState);
        }

        /// <summary>
        /// Dispatches the pending shader using the specified thread group values.
        /// </summary>
        /// <param name="threadGroupCountX">The number of thread groups to schedule for the X axis.</param>
        /// <param name="threadGroupCountY">The number of thread groups to schedule for the Y axis.</param>
        /// <param name="threadGroupCountZ">The number of thread groups to schedule for the Z axis.</param>
        public void Dispatch(int threadGroupCountX, int threadGroupCountY, int threadGroupCountZ)
        {
            this.d3D12GraphicsCommandList.Get()->Dispatch((uint)threadGroupCountX, (uint)threadGroupCountY, (uint)threadGroupCountZ);
        }

        /// <summary>
        /// Executes the commands in the current commands list, and waits for completion.
        /// </summary>
        public void ExecuteAndWaitForCompletion()
        {
            this.d3D12GraphicsCommandList.Get()->Close();

            // TODO: GraphicsDevice.ExecuteCommandList(this);
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose()
        {
            this.device.EnqueueCommandAllocator(this.d3D12CommandAllocator.Move(), this.d3d12CommandListType);

            this.d3D12GraphicsCommandList.Dispose();
        }
    }
}
