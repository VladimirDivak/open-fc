//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Infrastructure.URP;

using System.Reflection;
using HTraceAO.Scripts.Globals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using HTraceAO.Scripts.Editor.WindowsAndMenu;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.AnimatedValues;

namespace HTraceAO.Scripts.Editor
{
	[CanEditMultipleObjects]
#if UNITY_2022_2_OR_NEWER
	[CustomEditor(typeof(HTraceAOVolume))]
#else
[VolumeComponentEditor(typeof(HTraceAOVolume))]
#endif
	public class HTraceAOVolumeEditorURP : VolumeComponentEditor
	{
		private const string NO_RENDERER_FEATURE_MESSAGE  = "HTrace Ambient Occlusion feature is missing in the active URP renderer.";
		private const string RENDERER_FEATURE_OFF_MESSAGE = "HTrace Ambient Occlusion is disabled in the active URP renderer.";
		private const string RT_IS_NOT_SUPPORTED_MESSAGE = "Realtime Raytracing is not supported!";

		private Texture2D m_Icon;

		SerializedDataParameter p_Enable;

		// General
		SerializedDataParameter p_AmbientOcclusionMode;
		SerializedDataParameter p_HBuffer;
		SerializedDataParameter p_Intensity;
		SerializedDataParameter p_DirectLightingOcclusion;

		// SSAO
		SerializedDataParameter p_SSAO_DebugMode;
		SerializedDataParameter p_Thickness;
		SerializedDataParameter p_Radius;

		// GTAO
		SerializedDataParameter p_GTAO_DebugMode;
		SerializedDataParameter p_FullResolution;
		SerializedDataParameter p_GTAOThickness;
		SerializedDataParameter p_GTAOWorldSpaceRadius;
		SerializedDataParameter p_GTAOSliceCount;
		SerializedDataParameter p_GTAOStepCount;
		SerializedDataParameter p_GTAOVisibilityBitmasks;
		SerializedDataParameter p_GTAOFalloff;
		SerializedDataParameter p_GTAOCheckerboarding;
		SerializedDataParameter p_GTAOSampleCountTemporal;
		SerializedDataParameter p_GTAOMotionRejection;
		SerializedDataParameter p_GTAONormalRejectionTemporal;
		SerializedDataParameter p_GTAORejectionStrengthTemporal;
		SerializedDataParameter p_GTAOReprojectionFilter;
		SerializedDataParameter p_GTAOPixelRadius;
		SerializedDataParameter p_GTAOFilterStrength;
		SerializedDataParameter p_GTAONormalRejectionSpatial;
		SerializedDataParameter p_GTAOUpscalingQuality;
		SerializedDataParameter p_GTAOUpscalingNormalRejection;

		// RTAO
		SerializedDataParameter p_RTAO_DebugMode;
		SerializedDataParameter p_RTAOWorldSpaceRadius;
		SerializedDataParameter p_RTAOLayerMask;
		SerializedDataParameter p_RTAOMaxRayBias;
		SerializedDataParameter p_RTAORayCount;
		SerializedDataParameter p_RTAOFullResolution;
		SerializedDataParameter p_RTAOCheckerboarding;
		SerializedDataParameter p_RTAOSampleCountTemporal;
		SerializedDataParameter p_RTAOMotionRejection;
		SerializedDataParameter p_RTAONormalRejectionTemporal;
		SerializedDataParameter p_RTAORejectionStrengthTemporal;
		SerializedDataParameter p_RTAOReprojectionFilter;
		SerializedDataParameter p_RTAOPixelRadius;
		SerializedDataParameter p_RTAOFilterStrength;
		SerializedDataParameter p_RTAONormalRejectionSpatial;
		SerializedDataParameter p_RTAOUpscalingQuality;
		SerializedDataParameter p_RTAOUpscalingNormalRejection;

		// Menu state
		private bool _showVisualsArea = true;
		private bool _showQualityArea = true;
		private bool _showUpscalingArea = true;
		private bool _showTemporalArea = true;
		private bool _showSpatialArea = true;

		// Main foldout groups
		private AnimBool AnimBoolGeneralTab;
		private AnimBool AnimBoolDenoisingTab;

		public override void OnEnable()
		{
			var o = new PropertyFetcher<HTraceAOVolume>(serializedObject);

			m_Icon = Resources.Load<Texture2D>("AO UI Card");

			p_Enable = Unpack(o.Find(x => x.Enable));

			// General Settings
			p_AmbientOcclusionMode = Unpack(o.Find(x => x.AmbientOcclusionMode));
			p_HBuffer              = Unpack(o.Find(x => x.HBuffer));
			p_Intensity              = Unpack(o.Find(x => x.Intensity));
			p_DirectLightingOcclusion = Unpack(o.Find(x => x.DirectLightingOcclusion));

			// SSAO Settings
			p_SSAO_DebugMode = Unpack(o.Find(x => x.DebugModeSSAO));
			p_Thickness      = Unpack(o.Find(x => x.Thickness));
			p_Radius         = Unpack(o.Find(x => x.Radius));

			// GTAO Settings
			p_GTAO_DebugMode = Unpack(o.Find(x => x.DebugModeGTAO));
			p_FullResolution = Unpack(o.Find(x => x.FullResolution));
			p_GTAOThickness = Unpack(o.Find(x => x.GTAOThickness));
			p_GTAOWorldSpaceRadius = Unpack(o.Find(x => x.GTAOWorldSpaceRadius));
			p_GTAOSliceCount = Unpack(o.Find(x => x.GTAOSliceCount));
			p_GTAOStepCount = Unpack(o.Find(x => x.GTAOStepCount));
			p_GTAOVisibilityBitmasks = Unpack(o.Find(x => x.GTAOVisibilityBitmasks));
			p_GTAOFalloff = Unpack(o.Find(x => x.GTAOFalloff));
			p_GTAOCheckerboarding = Unpack(o.Find(x => x.GTAOCheckerboarding));
			p_GTAOSampleCountTemporal = Unpack(o.Find(x => x.GTAOSampleCountTemporal));
			p_GTAOMotionRejection = Unpack(o.Find(x => x.GTAOMotionRejection));
			p_GTAONormalRejectionTemporal = Unpack(o.Find(x => x.GTAONormalRejectionTemporal));
			p_GTAORejectionStrengthTemporal = Unpack(o.Find(x => x.GTAORejectionStrengthTemporal));
			p_GTAOReprojectionFilter = Unpack(o.Find(x => x.GTAOReprojectionFilter));
			p_GTAOPixelRadius = Unpack(o.Find(x => x.GTAOPixelRadius));
			p_GTAOFilterStrength = Unpack(o.Find(x => x.GTAOFilterStrength));
			p_GTAONormalRejectionSpatial = Unpack(o.Find(x => x.GTAONormalRejectionSpatial));
			p_GTAOUpscalingQuality = Unpack(o.Find(x => x.GTAOUpscalingQuality));
			p_GTAOUpscalingNormalRejection = Unpack(o.Find(x => x.GTAOUpscalingNormalRejection));

			// RTAO Settings
			p_RTAO_DebugMode = Unpack(o.Find(x => x.DebugModeRTAO));
			p_RTAOWorldSpaceRadius = Unpack(o.Find(x => x.RTAOWorldSpaceRadius));
			p_RTAOMaxRayBias = Unpack(o.Find(x => x.RTAOMaxRayBias));
			p_RTAOLayerMask = Unpack(o.Find(x => x.RTAOLayerMask));
			p_RTAORayCount = Unpack(o.Find(x => x.RTAORayCount));
			p_RTAOFullResolution = Unpack(o.Find(x => x.RTAOFullResolution));
			p_RTAOCheckerboarding = Unpack(o.Find(x => x.RTAOCheckerboarding));
			p_RTAOSampleCountTemporal = Unpack(o.Find(x => x.RTAOSampleCountTemporal));
			p_RTAOMotionRejection = Unpack(o.Find(x => x.RTAOMotionRejection));
			p_RTAONormalRejectionTemporal = Unpack(o.Find(x => x.RTAONormalRejectionTemporal));
			p_RTAORejectionStrengthTemporal = Unpack(o.Find(x => x.RTAORejectionStrengthTemporal));
			p_RTAOReprojectionFilter = Unpack(o.Find(x => x.RTAOReprojectionFilter));
			p_RTAOPixelRadius = Unpack(o.Find(x => x.RTAOPixelRadius));
			p_RTAOFilterStrength = Unpack(o.Find(x => x.RTAOFilterStrength));
			p_RTAONormalRejectionSpatial = Unpack(o.Find(x => x.RTAONormalRejectionSpatial));
			p_RTAOUpscalingQuality = Unpack(o.Find(x => x.RTAOUpscalingQuality));
			p_RTAOUpscalingNormalRejection = Unpack(o.Find(x => x.RTAOUpscalingNormalRejection));

			// Initialize AnimBool for foldouts
			AnimBoolGeneralTab = new AnimBool(true);
			AnimBoolGeneralTab.valueChanged.RemoveAllListeners();
			AnimBoolGeneralTab.valueChanged.AddListener(Repaint);

			AnimBoolDenoisingTab = new AnimBool(true);
			AnimBoolDenoisingTab.valueChanged.RemoveAllListeners();
			AnimBoolDenoisingTab.valueChanged.AddListener(Repaint);

			base.OnEnable();
		}

		public override void OnInspectorGUI()
		{
			if (m_Icon != null)
			{
				//GUILayout.Label(m_Icon, HEditorStyles.icon, GUILayout.ExpandWidth(false));
				Rect rect = GUILayoutUtility.GetAspectRect((float)m_Icon.width / m_Icon.height);
				rect.xMin += 4;
				rect.xMax -= 4;
				GUI.DrawTexture(rect, m_Icon, ScaleMode.ScaleToFit);
				EditorGUILayout.Space(5f);
			}

			var htraceAoRendererFeature = HRendererURP.GetRendererFeatureByTypeName(nameof(HTraceAORendererFeature)) as HTraceAORendererFeature;
			if (htraceAoRendererFeature == null)
			{
				EditorGUILayout.Space();
				CoreEditorUtils.DrawFixMeBox(NO_RENDERER_FEATURE_MESSAGE, MessageType.Error, HEditorStyles.FixButtonName, () =>
				{
					HRendererURP.AddHTraceRendererFeatureToUniversalRendererData();
					GUIUtility.ExitGUI();
				});
				//EditorGUILayout.HelpBox(NO_RENDERER_FEATURE_MESSAGE, MessageType.Error, wide: true);
				return;
			}
			else if (!htraceAoRendererFeature.isActive)
			{
				EditorGUILayout.Space();
				CoreEditorUtils.DrawFixMeBox(RENDERER_FEATURE_OFF_MESSAGE, MessageType.Warning, HEditorStyles.FixButtonName, () =>
				{
					htraceAoRendererFeature.SetActive(true);
					GUIUtility.ExitGUI();
				});
				EditorGUILayout.Space();
			}

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
			{
				if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
					EditorGUILayout.HelpBox(RT_IS_NOT_SUPPORTED_MESSAGE, MessageType.Error);
			}

			// ------------------------------------- Global settings ----------------------------------------------------------

			PropertyField(p_Enable);
			EditorGUILayout.Space(5f);

			using (new HEditorUtils.FoldoutScope(AnimBoolGeneralTab, out var shouldDraw, HEditorStyles.GlobalSettingsContent.text))
			{
				if (shouldDraw)
				{
					PropertyField(p_AmbientOcclusionMode);

					switch ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex)
					{
						case AmbientOcclusionMode.SSAO:
							PropertyField(p_SSAO_DebugMode);
							if((DebugModeSSAO)p_SSAO_DebugMode.value.enumValueIndex == DebugModeSSAO.MainBuffers)
							{
								PropertyField(p_HBuffer);
							}
							break;
						case AmbientOcclusionMode.GTAO:
							PropertyField(p_GTAO_DebugMode);
							if((DebugModeGTAO)p_GTAO_DebugMode.value.enumValueIndex == DebugModeGTAO.MainBuffers)
							{
								PropertyField(p_HBuffer);
							}

							if((DebugModeGTAO)p_GTAO_DebugMode.value.enumValueIndex == DebugModeGTAO.TemporalDisocclusion && p_GTAOSampleCountTemporal.value.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);
							break;
						case AmbientOcclusionMode.RTAO:
							PropertyField(p_RTAO_DebugMode);
							if((DebugModeRTAO)p_RTAO_DebugMode.value.enumValueIndex == DebugModeRTAO.MainBuffers)
							{
								PropertyField(p_HBuffer);
							}

							if((DebugModeRTAO)p_RTAO_DebugMode.value.enumValueIndex == DebugModeRTAO.TemporalDisocclusion && p_RTAOSampleCountTemporal.value.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);
							break;
					}

					EditorGUILayout.Space(3f);

					// -------------------------------------------- SSAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.SSAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Intensity);
								PropertyField(p_Thickness);
								PropertyField(p_Radius);
								PropertyField(p_DirectLightingOcclusion);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
					}

					// -------------------------------------------- GTAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.GTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Intensity);
								PropertyField(p_GTAOThickness);
								PropertyField(p_GTAOWorldSpaceRadius);
								PropertyField(p_DirectLightingOcclusion);
								if (p_GTAOWorldSpaceRadius.value.floatValue > 2.5f && p_GTAOVisibilityBitmasks.value.boolValue == false)
								{
									EditorGUILayout.HelpBox("Enable Visibility Bitmasks for better precision with big radius.", MessageType.Info);
								}
							}

						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_GTAOSliceCount);
								if (p_GTAOVisibilityBitmasks.value.boolValue == false)
								{
									p_GTAOSliceCount.value.intValue = Mathf.Clamp(p_GTAOSliceCount.value.intValue, 2, 4);
								}
								PropertyField(p_GTAOStepCount);
								PropertyField(p_FullResolution);

								if (p_GTAOSampleCountTemporal.value.intValue > 0)
									PropertyField(p_GTAOCheckerboarding);
								else
									p_GTAOCheckerboarding.value.boolValue = false;

								PropertyField(p_GTAOVisibilityBitmasks);

								if (p_GTAOVisibilityBitmasks.value.boolValue == true)
								{
									using (new IndentLevelScope())
									{
										PropertyField(p_GTAOFalloff);
									}
								}
							}

						}
						EditorGUILayout.EndFoldoutHeaderGroup();

						if (p_FullResolution.value.boolValue == false)
						{
							_showUpscalingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showUpscalingArea, "Upscaling");
							if (_showUpscalingArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_GTAOUpscalingQuality);
									PropertyField(p_GTAOUpscalingNormalRejection);
								}

							}
							EditorGUILayout.EndFoldoutHeaderGroup();
						}
					}

					// -------------------------------------------- RTAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Intensity);
								PropertyField(p_RTAOMaxRayBias);
								PropertyField(p_RTAOWorldSpaceRadius);
								PropertyField(p_DirectLightingOcclusion);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_RTAOLayerMask);
								PropertyField(p_RTAORayCount);
#if UNITY_2023_3_OR_NEWER
								if (p_RTAOLayerMask.value.uintValue == 0)
									PropertyField(p_RTAOCheckerboarding);
#endif
								PropertyField(p_RTAOFullResolution);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();

						if (p_FullResolution.value.boolValue == false)
						{
							_showUpscalingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showUpscalingArea, "Upscaling");
							if (_showUpscalingArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_RTAOUpscalingQuality);
									PropertyField(p_RTAOUpscalingNormalRejection);
								}

							}
							EditorGUILayout.EndFoldoutHeaderGroup();
						}
					}
				}
			}

			// ------------------------------------- DENOISING ----------------------------------------------------------

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.GTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDrawDenoising, HEditorStyles.GTAO_DenoisingTabContent.text))
				{
					if (shouldDrawDenoising)
					{
						_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
						if (_showTemporalArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_GTAOSampleCountTemporal);

								bool hide = p_GTAOSampleCountTemporal.value.intValue > 0;
								GUI.enabled = hide;

								PropertyField(p_GTAOMotionRejection);
								PropertyField(p_GTAONormalRejectionTemporal);
								PropertyField(p_GTAORejectionStrengthTemporal);
								PropertyField(p_GTAOReprojectionFilter);

								GUI.enabled = true;
							}

						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
						if (_showSpatialArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_GTAOPixelRadius);
								PropertyField(p_GTAOFilterStrength);
								PropertyField(p_GTAONormalRejectionSpatial);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
					}
				}
			}

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDrawDenoising, HEditorStyles.RTAO_DenoisingTabContent.text))
				{
					if (shouldDrawDenoising)
					{
						_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
						if (_showTemporalArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_RTAOSampleCountTemporal);

								bool hide = p_RTAOSampleCountTemporal.value.intValue > 0;
								GUI.enabled = hide;

								PropertyField(p_RTAOMotionRejection);
								PropertyField(p_RTAONormalRejectionTemporal);
								PropertyField(p_RTAORejectionStrengthTemporal);
								PropertyField(p_RTAOReprojectionFilter);

								GUI.enabled = true;
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
						if (_showSpatialArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_RTAOPixelRadius);
								PropertyField(p_RTAOFilterStrength);
								PropertyField(p_RTAONormalRejectionSpatial);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
					}
				}
			}


			HEditorUtils.HorizontalLine(1f);
			EditorGUILayout.Space(3f);

			//HEditorUtils.DrawClickableLink($"HTrace AO Version: {HNames.HTRACE_AO_VERSION}", HNames.HTRACE_AO_DOCUMENTATION_LINK, true);
			HEditorUtils.DrawLinkRow(
				($"Documentation (v." + HNames.HTRACE_AO_VERSION + ")", () => Application.OpenURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)),
				("Discord", () => Application.OpenURL(HNames.HTRACE_DISCORD_LINK)),
				("Bug report", () => HBugReporterWindow.ShowWindow())
			);
		}
	}
}

#endif
