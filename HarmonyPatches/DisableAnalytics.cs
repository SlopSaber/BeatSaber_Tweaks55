using BeatGames.Analytics;
using HarmonyLib;
using OSCE.Analytics;
using System;
using System.Reflection;
using Tweaks55.Util;
using UnityEngine;

namespace Tweaks55.HarmonyPatches {
	[HarmonyPatch]
	static class DisableAnalytics {
		[HarmonyPriority(int.MaxValue)]
		static void Prefix(AnalyticsManager analyticsManager) {
			if(!Config.Instance.disableTelemetry)
				return;

			analyticsManager.SetSystemMode(AnalyticsSystemModeEnum.DISABLED);
		}

		static MethodBase TargetMethod() => Resolver.GetFirstContructor(nameof(AnalyticsEventsDispatcher), "BeatGames.Analytics");
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
