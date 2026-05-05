//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using UnityEngine;

namespace HTraceAO.Scripts.Data.Public
{
	[Serializable]
	public class GTAOSettings
	{
		public DebugModeGTAO DebugMode = DebugModeGTAO.None;
		
		// ----------------------------------------------- Visuals -----------------------------------------------
		[SerializeField]
		private float _thickness = 0.5f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float Thickness
		{
			get => _thickness;    
			set
			{
				if (Mathf.Abs(value - _thickness) < Mathf.Epsilon)
					return;

				_thickness = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.Thickness));
			}
		}
		
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

				_worldSpaceRadius = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.WorldSpaceRadius));
			}
		}
		
		// ----------------------------------------------- Quality -----------------------------------------------
		
		[SerializeField]
		private int _sliceCount = 2;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;4]</value>
		[HExtensions.HRangeAttribute(1,4)]
		public int SliceCount
		{
			get
			{
				if (VisibilityBitmasks == false)
				{
					return Mathf.Clamp(_sliceCount,2, 4);
				}

				return _sliceCount;
			}
			set
			{
				if (value == _sliceCount)
					return;

				_sliceCount = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.SliceCount));
			}
		}
		
		[SerializeField]
		private int _stepCount = 16;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[8;32]</value>
		[HExtensions.HRangeAttribute(8,32)]
		public int StepCount
		{
			get => _stepCount;    
			set
			{
				if (value == _stepCount)
					return;

				_stepCount = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.StepCount));
			}
		}

		[SerializeField]
		public bool FullResolution = true;
		
		[SerializeField]
		public bool VisibilityBitmasks = false;
		
		[SerializeField]
		public bool Falloff = true;
		
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
		private int _sampleCountTemporal = 8;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0;12]</value>
		[HExtensions.HRangeAttribute(0,12)]
		public int SampleCountTemporal
		{
			get => _sampleCountTemporal + 1;
			set
			{
				if (value == _sampleCountTemporal)
					return;

				_sampleCountTemporal = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.SampleCountTemporal));
			}
		}
		
		[SerializeField]
		private float _motionRejection = 0.75f;
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

				_motionRejection = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.MotionRejection));
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

				_normalRejectionTemporal = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.NormalRejectionTemporal));
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

				_rejectionStrengthTemporal = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.RejectionStrengthTemporal));
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
		[HExtensions.HRangeAttribute(0,4)]
		public int PixelRadius
		{
			get => _pixelRadius;    
			set
			{
				if (value == _pixelRadius)
					return;

				_pixelRadius = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.PixelRadius));
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

				_filterStrength = HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.FilterStrength));
			}
		}
		
		[SerializeField]
		public bool NormalRejectionSpatial = false;
	}
}
