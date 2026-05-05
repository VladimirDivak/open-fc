//pipelinedefine
#define H_URP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace HTraceAO.Scripts.Extensions
{
    public class ExtensionsURP
    {

		private static RenderTextureDescriptor _dscr;

#if UNITY_2023_3_OR_NEWER
		public static void UseTexture(IUnsafeRenderGraphBuilder builder, RenderGraph renderGraph, RTHandle targetTexture, ref TextureHandle passTextureHandle, AccessFlags accessFlags =  AccessFlags.ReadWrite)
		{
			TextureHandle outputTarget = renderGraph.ImportTexture(targetTexture);
			passTextureHandle = outputTarget;
			builder.UseTexture(outputTarget, accessFlags);
		}
		public static void UseTexture(IRasterRenderGraphBuilder builder, RenderGraph renderGraph, RTHandle targetTexture, ref TextureHandle passTextureHandle, AccessFlags accessFlags =  AccessFlags.ReadWrite)
		{
			TextureHandle outputTarget = renderGraph.ImportTexture(targetTexture);
			passTextureHandle = outputTarget;
			builder.UseTexture(outputTarget, accessFlags);
		}
#endif //UNITY_2023_3_OR_NEWER

		public static void ReAllocateIfNeeded(string name, ref RTHandle rtHandle, ref RenderTextureDescriptor inputDescriptor, int width = -1, int height = -1,
			GraphicsFormat graphicsFormat = GraphicsFormat.None, TextureDimension dimension = TextureDimension.Unknown, bool useMipMap = false)
		{
			_dscr = inputDescriptor;

			_dscr.width          = width != -1 ? width : _dscr.width;
			_dscr.height         = height != -1 ? height : _dscr.height;
			_dscr.graphicsFormat = graphicsFormat != GraphicsFormat.None ? graphicsFormat : _dscr.graphicsFormat;
			_dscr.dimension      = dimension != TextureDimension.Unknown ? dimension : _dscr.dimension;
			_dscr.useMipMap      = useMipMap;

// #if !UNITY_2023_0_OR_NEWER
// 			_dscr.msaaSamples = 1;
// #endif

#if UNITY_2023_3_OR_NEWER
			RenderingUtils.ReAllocateHandleIfNeeded(ref rtHandle, _dscr, name: name);
#else
			RenderingUtils.ReAllocateIfNeeded(ref rtHandle, _dscr, name: name);
#endif
		}

    }
}
