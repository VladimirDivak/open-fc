//pipelinedefine
#define H_URP

using HTraceAO.Scripts.Globals;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HTraceAO.Scripts.Wrappers
{
	public class RTWrapper
	{
		private RenderTextureDescriptor _dscr;

		public RTHandle      rt;



		public void HTextureAlloc(string name, Vector2 scaleFactor, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(scaleFactor, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HTextureAlloc(string name, ScaleFunc scaleFunc, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(scaleFunc, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HTextureAlloc(string name, int width, int height, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(width, height, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HRelease()
		{
			RTHandles.Release(rt);
		}

		public void ReAllocateIfNeeded(string name, ref RenderTextureDescriptor inputDescriptor, int width = -1, int height = -1, int depth = -1,
			GraphicsFormat graphicsFormat = GraphicsFormat.None, TextureDimension dimension = TextureDimension.Unknown, bool useMipMap = false)
		{
			_dscr = inputDescriptor;

			// if (rt == null  ||  rt.rt == null)
			// 	return;

			_dscr.width          = width != -1 ? width : _dscr.width;
			_dscr.height         = height != -1 ? height : _dscr.height;
			_dscr.volumeDepth = depth != -1 ? depth : _dscr.volumeDepth;;
			_dscr.graphicsFormat = graphicsFormat != GraphicsFormat.None ? graphicsFormat : _dscr.graphicsFormat;
			_dscr.dimension      = dimension != TextureDimension.Unknown ? dimension : _dscr.dimension;
			_dscr.useMipMap      = useMipMap;

#if !UNITY_2023_0_OR_NEWER
			_dscr.msaaSamples = 1;
#endif

#if UNITY_2023_3_OR_NEWER
			RenderingUtils.ReAllocateHandleIfNeeded(ref rt, _dscr, name: name);
#else
			RenderingUtils.ReAllocateIfNeeded(ref rt, _dscr, name: name);
#endif
		}
	}
}
