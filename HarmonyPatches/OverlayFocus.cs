using HarmonyLib;
using System;
using UnityEngine;

namespace Tweaks55.HarmonyPatches {
	[HarmonyPatch(typeof(DeactivateVRControllersOnFocusCapture), nameof(DeactivateVRControllersOnFocusCapture.UpdateVRControllerActiveState))]
	static class KeepSabersInOverlay {
		static bool Prefix(DeactivateVRControllersOnFocusCapture __instance) {
			if(!Config.Instance.keepSabersInOverlay || __instance._vrControllerGameObjects == null || __instance._xrSystemState.hasInputFocus)
				return true;

			// Without input focus the runtime may report invalid poses; VRController then keeps its last tracked pose.
			var controllers = __instance._vrControllers;
			for(var i = 0; i < controllers.Length; i++) {
				if(controllers[i] == null)
					continue;

				var gameObject = __instance._vrControllerGameObjects[i];
				if(!gameObject.activeSelf)
					gameObject.SetActive(true);
			}

			return false;
		}

		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

	[HarmonyPatch(typeof(SongPreviewPlayerPauseOnInputFocusLost), nameof(SongPreviewPlayerPauseOnInputFocusLost.HandleSystemStateChange))]
	static class KeepMusicInOverlay {
		static bool Prefix(XRSystemEventType eventType) =>
			!Config.Instance.keepMusicInOverlay ||
			(eventType != XRSystemEventType.InputFocusLost && eventType != XRSystemEventType.InputFocusAcquired);

		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
