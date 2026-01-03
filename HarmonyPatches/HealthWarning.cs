using HarmonyLib;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Tweaks55.Util;

namespace Tweaks55.HarmonyPatches {
	[HarmonyPatch]
	static class PatchHealthWarning {
		[HarmonyPriority(int.MinValue)]
		static void Postfix(HealthWarningViewController __instance) {
			if(!Config.Instance.disableHealthWarning || __instance._taskCompletionSource == null)
				return;

			__instance.Complete();
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(HealthWarningViewController), nameof(HealthWarningViewController.DidActivate));
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
