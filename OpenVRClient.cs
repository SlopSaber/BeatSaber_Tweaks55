using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Tweaks55 {
	// Minimal OpenVR client used alongside the game's OpenXR session on SteamVR.
	// Connects as a background application (never starts SteamVR) and is never shut
	// down, because shutting down the shared client could end the OpenXR session.
	static class OpenVRClient {
		const int ApplicationBackground = 3;
		const int TrackingUniverseStanding = 1;
		const int ControllerRoleLeftHand = 1;
		const int ControllerRoleRightHand = 2;
		const uint InvalidDeviceIndex = 0xFFFFFFFF;
		const int MaxDevices = 64;

		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint InitInternal2(ref int error, int applicationType, [MarshalAs(UnmanagedType.LPStr)] string startupInfo);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetGenericInterface([MarshalAs(UnmanagedType.LPStr)] string version, ref int error);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDeviceToAbsoluteTrackingPoseFn(int origin, float predictedSecondsToPhotonsFromNow, [In, Out] TrackedDevicePose[] poses, uint count);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint GetTrackedDeviceIndexForControllerRoleFn(int role);

		[StructLayout(LayoutKind.Sequential)]
		struct HmdMatrix34 {
			public float m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct TrackedDevicePose {
			public HmdMatrix34 deviceToAbsoluteTracking;
			public float vx, vy, vz;
			public float avx, avy, avz;
			public int trackingResult;
			[MarshalAs(UnmanagedType.I1)] public bool poseIsValid;
			[MarshalAs(UnmanagedType.I1)] public bool deviceIsConnected;
		}

		// IVRSystem_022 function table indices (openvr.h 2.x declaration order).
		const int SystemGetDeviceToAbsoluteTrackingPose = 11;
		const int SystemGetTrackedDeviceIndexForControllerRole = 17;
		const int SystemFunctionCount = 18;

		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
		static extern IntPtr LoadLibrary(string path);
		[DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
		static extern IntPtr GetProcAddress(IntPtr module, string name);

		static bool attempted;
		static bool available;
		static GetDeviceToAbsoluteTrackingPoseFn getPoses;
		static GetTrackedDeviceIndexForControllerRoleFn getRoleIndex;
		static readonly TrackedDevicePose[] poses = new TrackedDevicePose[MaxDevices];

		internal static bool Available => attempted ? available : TryConnect();

		static bool TryConnect() {
			attempted = true;
			try {
				var library = FindLibrary();
				if(library == null) {
					Plugin.Log.Info("OpenVR is unavailable: no SteamVR runtime is registered.");
					return false;
				}
				var module = LoadLibrary(library);
				if(module == IntPtr.Zero) {
					Plugin.Log.Warn($"OpenVR could not be loaded from {library} (error {Marshal.GetLastWin32Error()}).");
					return false;
				}
				var getInterface = Marshal.GetDelegateForFunctionPointer<GetGenericInterface>(GetProcAddress(module, "VR_GetGenericInterface"));
				int error = 0;
				var table = getInterface("FnTable:IVRSystem_022", ref error);
				if(table == IntPtr.Zero) {
					// Another mod may already hold a client in this process; otherwise connect.
					var init = Marshal.GetDelegateForFunctionPointer<InitInternal2>(GetProcAddress(module, "VR_InitInternal2"));
					error = 0;
					init(ref error, ApplicationBackground, null);
					if(error != 0) {
						Plugin.Log.Info($"OpenVR is unavailable (init error {error}).");
						return false;
					}
					error = 0;
					table = getInterface("FnTable:IVRSystem_022", ref error);
				}
				if(table == IntPtr.Zero || error != 0) {
					Plugin.Log.Warn($"OpenVR system interface is unavailable (error {error}).");
					return false;
				}
				var functions = new IntPtr[SystemFunctionCount];
				Marshal.Copy(table, functions, 0, SystemFunctionCount);
				getPoses = Marshal.GetDelegateForFunctionPointer<GetDeviceToAbsoluteTrackingPoseFn>(functions[SystemGetDeviceToAbsoluteTrackingPose]);
				getRoleIndex = Marshal.GetDelegateForFunctionPointer<GetTrackedDeviceIndexForControllerRoleFn>(functions[SystemGetTrackedDeviceIndexForControllerRole]);
				available = true;
				Plugin.Log.Info("OpenVR connected for controller poses.");
			} catch(Exception ex) {
				Plugin.Log.Warn($"OpenVR is unavailable: {ex.Message}");
			}
			return available;
		}

		static string FindLibrary() {
			var paths = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
			if(!File.Exists(paths))
				return null;
			foreach(var runtime in (JArray)JObject.Parse(File.ReadAllText(paths))["runtime"] ?? new JArray()) {
				var library = Path.Combine((string)runtime, "bin", "win64", "openvr_api.dll");
				if(File.Exists(library))
					return library;
			}
			return null;
		}

		// Poses are in OpenVR's standing universe, converted to Unity's left-handed axes.
		internal static bool TryGetPoses(out Pose head, out Pose left, out Pose right, out bool leftValid, out bool rightValid) {
			head = left = right = Pose.identity;
			leftValid = rightValid = false;
			if(!Available)
				return false;
			getPoses(TrackingUniverseStanding, 0f, poses, MaxDevices);
			if(!poses[0].poseIsValid)
				return false;
			head = ToPose(poses[0].deviceToAbsoluteTracking);
			leftValid = TryGetRolePose(ControllerRoleLeftHand, out left);
			rightValid = TryGetRolePose(ControllerRoleRightHand, out right);
			return true;
		}

		static bool TryGetRolePose(int role, out Pose pose) {
			pose = Pose.identity;
			var index = getRoleIndex(role);
			if(index == InvalidDeviceIndex || index >= MaxDevices || !poses[index].poseIsValid)
				return false;
			pose = ToPose(poses[index].deviceToAbsoluteTracking);
			return true;
		}

		static Pose ToPose(HmdMatrix34 m) {
			// Mirror the Z axis: OpenVR is right-handed, Unity left-handed.
			var forward = new Vector3(-m.m2, -m.m6, m.m10);
			var up = new Vector3(m.m1, m.m5, -m.m9);
			return new Pose(new Vector3(m.m3, m.m7, -m.m11), Quaternion.LookRotation(forward, up));
		}
	}
}
