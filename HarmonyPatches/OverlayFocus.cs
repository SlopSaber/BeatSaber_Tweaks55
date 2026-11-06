using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.XR;

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

	// OpenXR stops updating input actions while another application has input focus,
	// so the controllers would freeze. On SteamVR, follow OpenVR's raw poses instead,
	// aligned to the game's tracking space when focus is lost.
	[HarmonyPatch(typeof(VRController), nameof(VRController.Update))]
	static class TrackControllersInOverlay {
		static bool hasWorld;
		static Pose world;
		static readonly bool[] hasOffset = new bool[2];
		static readonly Pose[] offset = new Pose[2];
		static int poseFrame = -1;
		static bool posesValid;
		static Pose head, left, right;
		static bool leftValid, rightValid;

		static void Postfix(VRController __instance) {
			if(!Config.Instance.keepSabersInOverlay)
				return;
			var node = __instance._node;
			if(node != XRNode.LeftHand && node != XRNode.RightHand)
				return;
			if(__instance._xrSystemState.hasInputFocus) {
				hasWorld = false;
				hasOffset[0] = hasOffset[1] = false;
				return;
			}
			// Without a running XR display there is no OpenXR session yet; connecting to OpenVR
			// first would make SteamVR reject the session the game creates afterwards.
			if(!XRSettings.isDeviceActive)
				return;

			if(poseFrame != Time.frameCount) {
				poseFrame = Time.frameCount;
				posesValid = OpenVRClient.TryGetPoses(out head, out left, out right, out leftValid, out rightValid);
			}
			if(!posesValid)
				return;

			if(!hasWorld) {
				if(!__instance._vrPlatformHelper.GetNodePose(XRNode.Head, 0, out var headPosition, out var headRotation))
					return;
				world = Multiply(new Pose(headPosition, headRotation), Inverse(head));
				hasWorld = true;
			}

			var index = node == XRNode.LeftHand ? 0 : 1;
			if(!(index == 0 ? leftValid : rightValid))
				return;

			var raw = Multiply(world, index == 0 ? left : right);
			var transform = __instance.transform;
			if(!hasOffset[index]) {
				// The frozen pose is the last tracked one, so it fixes the grip offset.
				offset[index] = Multiply(Inverse(raw), new Pose(transform.localPosition, transform.localRotation));
				hasOffset[index] = true;
			}
			var pose = Multiply(raw, offset[index]);
			transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
		}

		static Pose Multiply(Pose a, Pose b) => new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);

		static Pose Inverse(Pose pose) {
			var rotation = Quaternion.Inverse(pose.rotation);
			return new Pose(rotation * -pose.position, rotation);
		}

		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}

	[HarmonyPatch(typeof(SongPreviewPlayerPauseOnInputFocusLost),nameof(SongPreviewPlayerPauseOnInputFocusLost.HandleSystemStateChange))]
	static class KeepMusicInOverlay {
		static bool Prefix(XRSystemEventType eventType) =>
			!Config.Instance.keepMusicInOverlay ||
			(eventType != XRSystemEventType.InputFocusLost && eventType != XRSystemEventType.InputFocusAcquired);

		static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);
	}
}
