using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Tweaks55.Util;
using UnityEngine;

namespace Tweaks55.HarmonyPatches {
	[HarmonyPatch]
	static class CameraNoise {
		static readonly int _globalNoiseTextureID = Shader.PropertyToID("_GlobalBlueNoiseTex");

		[HarmonyPriority(int.MaxValue)]
		static void Postfix() {
			// Preserve per-camera noise parameters and random shader values before suppressing grain.
			if(Config.Instance.disableCameraNoise)
				Shader.SetGlobalTexture(_globalNoiseTextureID, null);
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(BlueNoiseDitheringUpdater), nameof(BlueNoiseDitheringUpdater.HandleCameraPreRender), assemblyName: "HMRendering");
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
