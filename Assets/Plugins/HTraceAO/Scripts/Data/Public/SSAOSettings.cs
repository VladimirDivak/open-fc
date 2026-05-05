//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using UnityEngine;

namespace HTraceAO.Scripts.Data.Public
{
	[Serializable]
	public class SSAOSettings
	{
		public DebugModeSSAO DebugModeSSAO = DebugModeSSAO.None;
		
		[SerializeField]
		private float _thickness = 0f;
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

				_thickness = HExtensions.Clamp(value, typeof(SSAOSettings), nameof(SSAOSettings.Thickness));
			}
		}
		
		[SerializeField]
		private int _radius = 2;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;4]</value>
		[HExtensions.HRangeAttribute(1,4)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				if (value == _radius)
					return;

				_radius = HExtensions.Clamp(value, typeof(SSAOSettings), nameof(SSAOSettings.Radius));
			}
		}
	}
}
