using UnityEngine;

namespace Tweaks55.HarmonyPatches {
	static class GlobalParticles {
		static bool lastKnownState = true;

		public static void SetEnabledState() {
			if(Config.Instance == null) {
				SetEnabledState(true);
				return;
			}

			SetEnabledState(!Config.Instance.disableGlobalParticles, true);
		}

		public static void SetEnabledState(bool enabled, bool refreshScene = false) {
			if(enabled == lastKnownState && !refreshScene)
				return;

			if(!lastKnownState || !enabled) {
				foreach(var particle in Resources.FindObjectsOfTypeAll<ParticleSystem>()) {
					var name = particle.name;
					if(name == "DustPS" || name == "DustBritney")
						particle.gameObject.SetActive(enabled);
				}
			}

			lastKnownState = enabled;
		}
	}
}
