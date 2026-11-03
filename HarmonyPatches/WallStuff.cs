using HarmonyLib;
using IPA.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Tweaks55.Util;
using UnityEngine;

namespace Tweaks55.HarmonyPatches {
	[HarmonyPatch]
	static class WallClash {
		[HarmonyPriority(int.MaxValue)]
		static void Postfix(MonoBehaviour __instance) {
			if(!Config.Instance.disableWallRumbleAndParticles)
				return;

			__instance.enabled = false;
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(ObstacleSaberSparkleEffectManager), nameof(ObstacleSaberSparkleEffectManager.Start));
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

	// Legacy obstacle prefabs use this component to select frame and glow visibility.
#pragma warning disable CS0612
	[HarmonyPatch]
	static class DisableFakeWallBloom {
		static readonly FieldAccessor<ConditionalActivation, bool>.Accessor ConditionalActivation_activateOnFalse = 
			FieldAccessor<ConditionalActivation, bool>.GetAccessor("_activateOnFalse");

		static readonly FieldAccessor<ConditionalActivation, BoolSO>.Accessor ConditionalActivation_value =
			FieldAccessor<ConditionalActivation, BoolSO>.GetAccessor("_value");
		sealed class OriginalActivation {
			internal bool activateOnFalse;
		}

		static readonly ConditionalWeakTable<ConditionalActivation, OriginalActivation> originalActivations =
			new ConditionalWeakTable<ConditionalActivation, OriginalActivation>();

		static void RestoreActivation(ConditionalActivation activation) {
			if(activation == null)
				return;

			var original = originalActivations.GetValue(activation, component => new OriginalActivation {
				activateOnFalse = ConditionalActivation_activateOnFalse(ref component)
			});
			ConditionalActivation_activateOnFalse(ref activation) = original.activateOnFalse;
		}

		[HarmonyPriority(int.MaxValue)]
		static void Prefix(ObstacleController ____obstaclePrefab, GameplayCoreSceneSetupData ____sceneSetupData) {
			WallVisualState.logNextObstacle = true;
			Plugin.Log.Info($"Wall setup: level={____sceneSetupData.beatmapKey.levelId}, prefab={____obstaclePrefab.name}, fakeBloomDisabled={Config.Instance.disableFakeWallBloom}, transparent={Config.Instance.transparentWalls}, cameraNoiseDisabled={Config.Instance.disableCameraNoise}");

			var x = ____obstaclePrefab.GetComponentInChildren<ParametricBoxFrameController>(true)?
				.GetComponent<ConditionalActivation>();
			var fakeGlow = ____obstaclePrefab.GetComponentInChildren<ParametricBoxFakeGlowController>(true)?
				.GetComponent<ConditionalActivation>();

			RestoreActivation(x);
			RestoreActivation(fakeGlow);

			if(!Config.Instance.disableFakeWallBloom || TransparentWall.HasVisualMod(____sceneSetupData.beatmapKey))
				return;

			if(x != null)
				ConditionalActivation_activateOnFalse(ref x) = !(!Config.Instance.disableFakeWallBloom || ConditionalActivation_value(ref x));

			if(fakeGlow != null) {
				var bloomIsOn = ConditionalActivation_value(ref fakeGlow);

				ConditionalActivation_activateOnFalse(ref fakeGlow) = !(!Config.Instance.disableFakeWallBloom != !bloomIsOn) || bloomIsOn;
			}
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(BeatmapObjectsInstaller), "InstallBindings");
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

#pragma warning restore CS0612

	[HarmonyPatch]
	static class WallVisualState {
		internal static bool logNextObstacle;
		static readonly int tintColorID = Shader.PropertyToID("_TintColor");

		[HarmonyPriority(int.MinValue)]
		static void Postfix(ObstacleController __instance) {
			if(!logNextObstacle)
				return;
			logNextObstacle = false;

			var block = new MaterialPropertyBlock();
			var renderers = __instance.GetComponentsInChildren<Renderer>(true).Select(renderer => {
				var material = renderer.sharedMaterial;
				renderer.GetPropertyBlock(block);
				return $"{renderer.name}: active={renderer.gameObject.activeInHierarchy}, enabled={renderer.enabled}, layer={renderer.gameObject.layer}, material={material?.name}, shader={material?.shader?.name}, queue={material?.renderQueue}, tint={block.GetColor(tintColorID)}";
			});
			Plugin.Log.Info("First wall visuals: " + string.Join("; ", renderers));
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(ObstacleController), nameof(ObstacleController.Init));
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

	[HarmonyPatch]
	static class TransparentWall {
		static FieldAccessor<ObstacleController, GameObject[]>.Accessor ObstacleController_visualWrappers = FieldAccessor<ObstacleController, GameObject[]>.GetAccessor("_visualWrappers");

		static GameObject[] visualWrappersOriginal = null;
		static ObstacleController originalPrefab = null;
		static bool originalWrapperActive;

		static bool HasVisualMod(string[] capabilities) => capabilities != null && capabilities.Any(x =>
			string.Equals(x, "Noodle Extensions", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(x, "Chroma", StringComparison.OrdinalIgnoreCase));

		internal static bool HasVisualMod(BeatmapKey beatmapKey) {
			var difficulty = SongCore.Collections.GetCustomLevelSongDifficultyData(beatmapKey);
			var requirements = difficulty?.additionalDifficultyData;
			return HasVisualMod(requirements?._requirements) || HasVisualMod(requirements?._suggestions);
		}

		[HarmonyPriority(int.MaxValue)]
		static void Postfix(ObstacleController ____obstaclePrefab, GameplayCoreSceneSetupData ____sceneSetupData) {
			if(originalPrefab != ____obstaclePrefab) {
				originalPrefab = ____obstaclePrefab;
				visualWrappersOriginal = null;
			}

			bool transparentWalls = Config.Instance.transparentWalls &&
				!HasVisualMod(____sceneSetupData.beatmapKey);

			if(visualWrappersOriginal != null) {
				if(transparentWalls)
					return;

				ObstacleController_visualWrappers(ref ____obstaclePrefab) = visualWrappersOriginal;
				visualWrappersOriginal[0].SetActive(originalWrapperActive);
				visualWrappersOriginal = null;
				return;
			}

			if(!transparentWalls)
				return;

			GameObject[] wrappers = ObstacleController_visualWrappers(ref ____obstaclePrefab);
			if(wrappers.Length != 2)
				return;

			visualWrappersOriginal = wrappers;
			originalWrapperActive = wrappers[0].activeSelf;
			ObstacleController_visualWrappers(ref ____obstaclePrefab) = new[] { wrappers[1] };

			wrappers[0].SetActive(false);
		}

		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(BeatmapObjectsInstaller), "InstallBindings");
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

	[HarmonyPatch]
	static class WallOutline {
		internal static readonly Color defaultColor = Color.white.ColorWithAlpha(0);

		static byte colorParam = 0;

		static bool Prepare(MethodBase __originalMethod) {
			colorParam = (byte)(1 + Array.FindIndex(TargetMethod().GetParameters(), x => x.Name == "color" && x.ParameterType == typeof(Color)));

			return colorParam != 0;
		}

		internal static Color realBorderColor;
		internal static Color fakeBorderColor;
		internal static bool enabled;

		static Color GetRealBorderColor(Color originalColor) {
			if(enabled)
				return realBorderColor;

			return originalColor;
		}

		static Color GetFakeBorderColor(Color originalColor) {
			if(enabled)
				return fakeBorderColor;

			return originalColor;
		}

		[HarmonyPriority(int.MaxValue)]
		static IEnumerable Transpiler(IEnumerable<CodeInstruction> instructions) {
			var c = new CodeMatcher(instructions);

			c.MatchForward(true,
				new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(StretchableObstacle), "_obstacleFrame")),
				new CodeMatch(OpCodes.Ldarg_S, colorParam)
			)
			.ThrowIfInvalid("_obstacleFrame color setter not found")
			.Advance(1)
			.InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WallOutline), nameof(GetRealBorderColor))))

			.MatchForward(true,
				new CodeMatch(OpCodes.Ldfld, operand: AccessTools.Field(typeof(StretchableObstacle), "_obstacleFakeGlow")),
				new CodeMatch(OpCodes.Ldarg_S, colorParam)
			)
			.ThrowIfInvalid("_obstacleFakeGlow color setter not found")
			.Advance(1)
			.InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WallOutline), nameof(GetFakeBorderColor))));

			return c.InstructionEnumeration();
		}
		static MethodBase TargetMethod() => Resolver.GetMethod(nameof(StretchableObstacle), nameof(StretchableObstacle.SetAllProperties));
		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
