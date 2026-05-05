namespace HTraceAO.Scripts.Globals
{
	internal static class HNames
	{
		public const string ASSET_NAME                   = "HTraceAO";
		public const string ASSET_NAME_FULL              = "HTrace Ambient Occlusion";
		public const string HTRACE_AO_DOCUMENTATION_LINK = "https://ipgames.gitbook.io/htrace-ao";
		public const string HTRACE_DISCORD_LINK          = "https://discord.com/invite/Nep56Efu7A";
		public const string HTRACE_DISCORD_BUGS_AO_LINK  = "https://discord.gg/Qajurv4ucJ";
		public const string HTRACE_AO_VERSION            = "1.4.0";


		public const string HTRACE_RENDERER_FEATURE_NAME = "HTrace Ambient Occlusion";

		// ---------------- Profiling ----------------
		public const string HTRACE_PRE_PASS_NAME        = "HTraceAO Pre Pass";
		public const string HTRACE_MV_PASS_NAME         = "HTraceAO Motion Vectors Pass";
		public const string HTRACE_OBJECTS_MV_PASS_NAME = "HTraceAO Objects Motion Vectors Pass";
		public const string HTRACE_CAMERA_MV_PASS_NAME  = "HTraceAO Camera Motion Vectors Pass";
		public const string HTRACE_SSAO_PASS_NAME       = "HTraceAO SSAO Pass";
		public const string HTRACE_GTAO_PASS_NAME       = "HTraceAO GTAO Pass";
		public const string HTRACE_RTAO_PASS_NAME       = "HTraceAO RTAO Pass";
		public const string HTRACE_FINAL_PASS_NAME      = "HTraceAO Final Pass";

		public const string COPY_MOVING_STENCIL_SAMPLER    = "Copy Moving Stencil";
		public const string COMPOSE_MOTION_VECTORS_SAMPLER = "Compose Motion Vectors";

		public const string RENDER_OCCLUSION_SAMPLER         = "Render Occlusion";
		public const string INTERPOLATION_SAMPLER            = "Interpolation";
		public const string TEMPORAL_ACCUMULATION_SAMPLER    = "Temporal Accumulation";
		public const string SPATIAL_FILTER_SAMPLER           = "Spatial Filter";
		public const string CHECKERBOARDING_SAMPLER          = "Checkerboarding";
		public const string DEPTH_PYRAMID_GENERATION_SAMPLER = "Depth Pyramid Generation";

		public const string KEYWORD_SWITCHER = "HTRACE_OVERRIDE_AO";
		public const string INT_SWITCHER     = "_HTRACE_INT_OVERRIDE";
	}
}
