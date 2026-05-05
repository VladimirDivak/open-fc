//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using UnityEngine;

namespace HTraceAO.Scripts.Data.Public
{
	[Serializable]
	public class RTAOSettings
	{
		public DebugModeRTAO DebugMode = DebugModeRTAO.None;
		
		// ----------------------------------------------- Visuals -----------------------------------------------
		
		[SerializeField]
		private float _worldSpaceRadius = 1f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.25;5.0]</value>
		[HExtensions.HRangeAttribute(0.25f,5.0f)]
		public float WorldSpaceRadius
		{
			get => _worldSpaceRadius;    
			set
			{
				if (Mathf.Abs(value - _worldSpaceRadius) < Mathf.Epsilon)
					return;

				_worldSpaceRadius = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.WorldSpaceRadius));
			}
		}
		
		[SerializeField]
		private float _maxRayBias = 0.002f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.001;0.02]</value>
		[HExtensions.HRangeAttribute(0.001f,0.02f)]
		public float MaxRayBias
		{
			get => _maxRayBias;    
			set
			{
				if (Mathf.Abs(value - _maxRayBias) < Mathf.Epsilon)
					return;

				_maxRayBias = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.MaxRayBias));
			}
		}
		
		
		[SerializeField]
		private float _specularOcclusion = 0.5f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float SpecularOcclusion
		{
			get => _specularOcclusion;    
			set
			{
				if (Mathf.Abs(value - _specularOcclusion) < Mathf.Epsilon)
					return;

				_specularOcclusion = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.SpecularOcclusion));
			}
		}
		
		// ----------------------------------------------- Quality -----------------------------------------------

		/// <summary>
		/// Only HDRP
		/// </summary>
		[SerializeField]
		public AlphaCutout AlphaCutout

	= AlphaCutout.DepthTest;

		/// <summary>
		/// Only URP and BIRP
		/// </summary>
		[SerializeField]
		public LayerMask LayerMask = ~0;
		
		[SerializeField]
		private int _rayCount = 4;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;8]</value>
		[HExtensions.HRangeAttribute(1,8)]
		public int RayCount
		{
			get
			{
				return _rayCount;
			}
			set
			{
				if (value == _rayCount)
					return;

				_rayCount = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.RayCount));
			}
		}
		
		[SerializeField]
		public bool CullBackfaces = false;

		[SerializeField]
		public bool FullResolution = true;
		
		[SerializeField]
		public bool Checkerboarding = false;

		// ----------------------------------------------- Upscaling -----------------------------------------------

		[SerializeField]
		public UpscalingQuality UpscalingQuality = UpscalingQuality.Linear5Taps;
		
		[SerializeField]
		public bool UpscalingNormalRejection = false;
		
		// ----------------------------------------------- DENOISING -----------------------------------------------
		// ----------------------------------------------- Temporal -----------------------------------------------
		
		[SerializeField]
		private int _sampleCountTemporal = 12;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[8;16]</value>
		[HExtensions.HRangeAttribute(8,16)]
		public int SampleCountTemporal
		{
			get => _sampleCountTemporal + 1;    
			set
			{
				if (value == _sampleCountTemporal)
					return;

				_sampleCountTemporal = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.SampleCountTemporal));
			}
		}
		
		[SerializeField]
		private float _motionRejection = 0.6f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float MotionRejection
		{
			get => _motionRejection;    
			set
			{
				if (Mathf.Abs(value - _motionRejection) < Mathf.Epsilon)
					return;

				_motionRejection = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.MotionRejection));
			}
		}
		
		[SerializeField]
		private float _normalRejectionTemporal = 0f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float NormalRejectionTemporal
		{
			get => HMath.Remap(_normalRejectionTemporal, 0f, 1f, 0f, 0.9f);    
			set
			{
				if (Mathf.Abs(value - _normalRejectionTemporal) < Mathf.Epsilon)
					return;

				_normalRejectionTemporal = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.NormalRejectionTemporal));
			}
		}
		
		[SerializeField]
		private float _rejectionStrengthTemporal = 0.25f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float RejectionStrengthTemporal
		{
			get => _rejectionStrengthTemporal;
			set
			{
				if (Mathf.Abs(value - _rejectionStrengthTemporal) < Mathf.Epsilon)
					return;

				_rejectionStrengthTemporal = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.RejectionStrengthTemporal));
			}
		}
		
		[SerializeField]
		public ReprojectionFilter ReprojectionFilter = ReprojectionFilter.Linear4Taps;

		// ----------------------------------------------- Spatial -----------------------------------------------
		
		[SerializeField]
		private int _pixelRadius = 1;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;4]</value>
		[HExtensions.HRangeAttribute(1,4)]
		public int PixelRadius
		{
			get => _pixelRadius;    
			set
			{
				if (value == _pixelRadius)
					return;

				_pixelRadius = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.PixelRadius));
			}
		}
		
		[SerializeField]
		private float _filterStrength = 0.75f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float FilterStrength
		{
			get => _filterStrength;    
			set
			{
				if (Mathf.Abs(value - _filterStrength) < Mathf.Epsilon)
					return;

				_filterStrength = HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.FilterStrength));
			}
		}
		
		[SerializeField]
		public bool NormalRejectionSpatial = false;
	}
}
