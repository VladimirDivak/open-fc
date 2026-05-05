#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HTraceAO.Scripts.Editor
{
	internal static class HEditorStyles
	{
		public static float defaultLineSpace = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
		public static float additionalLineSpace = 10f;
		public static float helpBoxHeight = EditorGUIUtility.singleLineHeight * 2;
		public static float checkBoxOffsetWidth = 15f;
		public static float checkBoxWidth = 15f;
		public static float tabOffset = 8f;

		// Buttons name
		public const string FixButtonName = "Fix";
		public const string OpenButtonName = "Open";

		// General Tab
		public static GUIContent InjectionPoint        = new GUIContent("Injection point");
		public static GUIContent GlobalSettingsContent = new GUIContent("Global Settings");
		public static GUIContent SSAOSettingsContent   = new GUIContent("Screen Space AO");
		public static GUIContent Enable   = new GUIContent("Enable");
		public static GUIContent AOMode   = new GUIContent("Mode");
		public static GUIContent hTraceLayerContent    = new GUIContent("HTrace Layer", "Excludes objects from HTrace rendering on a per-layer basis");
		public static GUIContent DebugModeContent      = new GUIContent("Debug Mode",   "Visualizes the debug mode for different rendering components of H-Trace.");
		public static GUIContent HBuffer               = new GUIContent("Buffer",   "Visualizes the debug mode for different buffers.");
		
		public static GUIContent Intensity               = new GUIContent("Intensity", "Guides the final intensity of the ambient occlusion, higher values result in darker ambient occlusion.");
		public static GUIContent DirectLightingOcclusion = new GUIContent("Direct Lighting Occlusion", "Defines how visible the effect is in areas exposed to direct lighting. It's recommended to use 0 for maximum physical accuracy.");
		
		// SSAO Tab
		public static GUIContent SSAO_Thickness               = new GUIContent("Thickness", "Control the thickness of the surfaces on screen. Because the screen-space algorithms can not distinguish thin objects from thick ones, this property helps trace rays behind objects, treating them uniformly.");
		public static GUIContent SSAO_Radius                  = new GUIContent("Radius");
		
		// GTAO Tab
		public static GUIContent GTAO_Thickness                 = new GUIContent("Thickness", "Control the thickness of the surfaces on screen. Because the screen-space algorithms can not distinguish thin objects from thick ones, this property helps trace rays behind objects, treating them uniformly.");
		public static GUIContent GTAO_WorldSpaceRadius          = new GUIContent("World Space Radius", "Defines the maximum distance (in meters) for occluder search. RTAO produces darker results with the same distance due to being more physically correct and not needing a falloff.");
		public static GUIContent GTAO_SliceCount                = new GUIContent("Slice Count", "Specifies the number of directions (slices) used to evaluate occlusion. The final sample count is calculated as \"Slice Count × Step Count × 2\". Each increment in the Slice Count significantly increases the number of samples. This parameter has the greatest impact on noise reduction (aside from the denoiser itself) but comes with a substantial performance cost.");
		public static GUIContent GTAO_StepCount                 = new GUIContent("Step Count", "Specifies the number of steps taken along each direction (slice). This parameter determines the accuracy of occlusion and affects noise levels, although to a lesser extent than the Slice Count.");
		public static GUIContent GTAO_VisibilityBitmasks        = new GUIContent("Visibility Bitmasks", "This approach provides more accurate results, especially in areas with thin geometry such as bars, fences, grills, and thin pillars, at a moderate performance cost.");
		public static GUIContent GTAO_Falloff                   = new GUIContent("               Falloff", "Specifies whether falloff should be applied during the occlusion evaluation. Bitmasks are originally designed to work without falloff, which makes them faster.");
		public static GUIContent GTAO_Checkerboarding           = new GUIContent("Checkerboarding", "Specifies whether the effect should be rendered using a checkerboard pattern, which processes only half of the pixels per frame. This option is designed to minimize visual impact while improving calculation times by up to 50%. It is recommended to enable this feature whenever possible.");
		public static GUIContent GTAO_UpscalingQuality          = new GUIContent("Quality");
		public static GUIContent GTAO_UpscaleingNormalRejection = new GUIContent("Normal Rejection", "Specifies whether the difference between surface normals is considered during the upscaling calculation.");
		public static GUIContent GTAO_FullResolution            = new GUIContent("Full Resolution", "Determines whether the effect is rendered at full resolution or half resolution.");
		public static GUIContent GTAO_SampleCount               = new GUIContent("Sample Count", "Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.");
		public static GUIContent GTAO_NormalRejectionTemporal   = new GUIContent("Normal Rejection", "Specifies whether the difference in surface normals should be considered during temporal history reprojection. This option can mitigate reprojection artifacts, such as the one in the screenshot where color from the frontal plane of the cube \"leaks\" onto its newly revealed side during camera panning.");
		public static GUIContent GTAO_RejectionStrengthTemporal = new GUIContent("Rejection Strength", "Defines the overall strictness of temporal history rejection. Similar to Normal Rejection, setting this parameter to high values close to 1.0 can cause temporal instability on small and thin details, such as foliage. Using very low values close to 0.0 can lead to ghosting in certain scenarios.");
		public static GUIContent GTAO_MotionRejection           = new GUIContent("Motion Rejection", "Controls the strictness of temporal history rejection. Lower values accept all history, producing smoother output with less noise, but can cause ghosting and trailing near moving objects or during camera translation/rotation. Higher values reject potentially invalid history samples, but may result in noisier output.");
		public static GUIContent GTAO_ReprojectionFilter        = new GUIContent("Reprojection Filter" , "Defines a reprojection filter used for temporal history fetch. Bilinear is fast but introduces blur, while Lanczos is approximately three times slower but much sharper. This option affects only the sharpness of the reprojection, not its effectiveness.");
		public static GUIContent GTAO_FilterStrength            = new GUIContent("Filter Strength", "Controls the strictness of spatial neighbors' rejection. Higher values better preserve details, while lower values reduce noise more efficiently. Check the first two screenshots in the comparison below to see the difference between the 0 and 1 values of this parameter.");
		public static GUIContent GTAO_NormalRejectionSpatial    = new GUIContent("Normal Rejection", "Specifies whether the difference in surface normals should be considered for spatial neighbors' rejection. Enabling this option further enhances detail preservation at the cost of performance");
		public static GUIContent GTAO_PixelRadius               = new GUIContent("Pixel Radius", "Controls the spatial denoiser radius in pixels. The wider the radius, the better the noise reduction at the cost of additional blur and performance.");
		public static GUIContent GTAO_DenoisingTabContent       = new GUIContent("Denoising");

		// RTAO Tab
		
		public static GUIContent RTAO_SpecularOcclusion         = new GUIContent("Specular Occlusion", "Defines the level of occlusion applied to the specular lighting component.");
		public static GUIContent RTAO_AlphaCutout               = new GUIContent("Alpha Cutout");
		public static GUIContent RTAO_LayerMask                 = new GUIContent("Layer Mask", "Use this option to exclude objects from the Ray Tracing Acceleration Structure on a per-layer basis.");
		public static GUIContent RTAO_WorldSpaceRadius          = new GUIContent("World Space Radius", "Defines the maximum distance (in meters) for occluder search. RTAO produces darker results with the same distance due to being more physically correct and not needing a falloff.");
		public static GUIContent RTAO_MaxRayBias                = new GUIContent("Max Ray Bias", "Controls the maximum ray bias (offset) from a surface. This parameter allows to avoid self-intersection with geometry and is especially useful when Unity's TAA is active.");
		public static GUIContent RTAO_RayCount                  = new GUIContent("Ray Count", "Specifies the number of rays launched into the scene to evaluate occlusion. This parameter represents the primary tradeoff between noise level and performance.");
		public static GUIContent RTAO_UpscalingQuality          = new GUIContent("Quality", "Defines the filter used for upscaling the half-resolution occlusion buffer.");
		public static GUIContent RTAO_UpscalingNormalRejection  = new GUIContent("Normal Rejection", "Specifies whether the difference between surface normals is considered during the upscaling calculation.");
		public static GUIContent RTAO_CullBackfaces             = new GUIContent("Cull Backfaces", "Specifies whether rays can collide with the backfacing sides of polygons. This option is global and affects all surfaces, ignoring the \"Double-Sided\" material parameter.");
		public static GUIContent RTAO_FullResolution            = new GUIContent("Full Resolution", "Determines whether the effect is rendered at full resolution or half resolution.");
		public static GUIContent RTAO_Checkerboarding           = new GUIContent("Checkerboarding", "Specifies whether the effect should be rendered using a checkerboard pattern, which processes only half of the pixels per frame. This option is designed to minimize visual impact while improving calculation times by up to 50%. It is recommended to enable this feature whenever possible.");
		public static GUIContent RTAO_SampleCountTemporal       = new GUIContent("Sample Count", "Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.");
		public static GUIContent RTAO_NormalRejection           = new GUIContent("Normal Rejection", "Specifies whether the difference in surface normals should be considered during temporal history reprojection. This option can mitigate reprojection artifacts, such as the one in the screenshot where color from the frontal plane of the cube \"leaks\" onto its newly revealed side during camera panning.");
		public static GUIContent RTAO_RejectionStrengthTemporal = new GUIContent("Rejection Strength", "Defines the overall strictness of temporal history rejection. Similar to Normal Rejection, setting this parameter to high values close to 1.0 can cause temporal instability on small and thin details, such as foliage. Using very low values close to 0.0 can lead to ghosting in certain scenarios.");
		public static GUIContent RTAO_MotionRejection           = new GUIContent("Motion Rejection", "Controls the strictness of temporal history rejection. Lower values accept all history, producing smoother output with less noise, but can cause ghosting and trailing near moving objects or during camera translation/rotation. Higher values reject potentially invalid history samples, but may result in noisier output.");
		public static GUIContent RTAO_ReprojectionFilter        = new GUIContent("Reprojection Filter", "Defines a reprojection filter used for temporal history fetch. Bilinear is fast but introduces blur, while Lanczos is approximately three times slower but much sharper. This option affects only the sharpness of the reprojection, not its effectiveness.");
		public static GUIContent RTAO_FilterStrength            = new GUIContent("Filter Strength" ,"Controls the strictness of spatial neighbors' rejection. Higher values better preserve details, while lower values reduce noise more efficiently. Check the first two screenshots in the comparison below to see the difference between the 0 and 1 values of this parameter.");
		public static GUIContent RTAO_NormalRejectionSpatial    = new GUIContent("Normal Rejection", "Specifies whether the difference in surface normals should be considered for spatial neighbors' rejection. Enabling this option further enhances detail preservation at the cost of performance");
		public static GUIContent RTAO_PixelRadius               = new GUIContent("Pixel Radius", "Controls the spatial denoiser radius in pixels. The wider the radius, the better the noise reduction at the cost of additional blur and performance.");
		public static GUIContent RTAO_DenoisingTabContent       = new GUIContent("Denoising");
		
		// Debug Tab
		public static GUIContent DebugContent           = new GUIContent("Debug");

		public static GUIStyle bold = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(2, 0, 0, 0),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				textColor = new Color(0.903f, 0.903f, 0.903f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};
		
		public static GUIStyle hiddenFoldout = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				//textColor = new Color(0.703f, 0.703f, 0.703f, 1f), //default color
				textColor = new Color(0.500f, 0.500f, 0.500f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};

		public static GUIStyle headerFoldout = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				textColor = new Color(0.903f, 0.903f, 0.903f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};

		private static GUIStyle _versionStyle;
		public static GUIStyle VersionStyle
		{
			get
			{
				if (_versionStyle == null)
				{
					_versionStyle = new GUIStyle(GUI.skin.label)
					{
						//padding = new RectOffset(left: 10, right: 10, top: 2, bottom: 2),
						fontStyle = FontStyle.Bold,
						fontSize = 10,
					};
				}
				return _versionStyle;
			}
		}
		
		//buttons gui styles
		public static Color warningBackgroundColor = new Color(1,1, 0);
		public static Color warningColor           = new Color(1, 1, 1);

		public static GUIStyle foldout = EditorStyles.foldout;
	}
}
#endif