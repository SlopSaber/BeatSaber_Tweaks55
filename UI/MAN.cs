using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.Util;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using IPA.Utilities.Async;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tweaks55.HarmonyPatches;
using UnityEngine;

namespace Tweaks55.UI {
	class TweaksFlowCoordinator : FlowCoordinator {
		MAN view = null;

		protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
			if(firstActivation) {
				SetTitle("Tweaks55");
				showBackButton = true;

				if(view == null)
					view = BeatSaberUI.CreateViewController<MAN>();

				ProvideInitialViewControllers(view);
			}
		}

		protected override void BackButtonWasPressed(ViewController topViewController) {
			BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this, null, ViewController.AnimationDirection.Horizontal);
			Config.Instance.Changed();
		}

		public void ShowFlow() {
			var _parentFlow = BeatSaberUI.MainFlowCoordinator.YoungestChildFlowCoordinatorOrSelf();

			BeatSaberUI.PresentFlowCoordinator(_parentFlow, this);
		}

		static TweaksFlowCoordinator flow = null;

		static MenuButton theButton;

		public static void Initialize() {
			MainMenuAwaiter.MainMenuInitializing += InitializeOnMainMenuLoad;
		}

		public static void Deinit() {
			MainMenuAwaiter.MainMenuInitializing -= InitializeOnMainMenuLoad;
			if(flow != null && flow.view != null)
				flow.view.RetireSponsorRequest();

			if(theButton != null)
				MenuButtons.Instance.UnregisterButton(theButton);
		}

		private static void InitializeOnMainMenuLoad() {

			MenuButtons.Instance.RegisterButton(theButton ??= new MenuButton("Tweaks55", "A bunch of settings that really should just exist in basegame!", () => {
				if(flow == null)
					flow = BeatSaberUI.CreateFlowCoordinator<TweaksFlowCoordinator>();

				flow.ShowFlow();
			}, true));
		}
	}

	[HotReload(RelativePathToLayout = @"./settings.bsml")]
	[ViewDefinition("Tweaks55.UI.settings.bsml")]
	class MAN : BSMLAutomaticViewController {
		Config config = Config.Instance;

		static bool isAprilFirst = (DateTime.Now.Month == 4) && (DateTime.Now.Day == 1);
		static bool __true => true;

		void ClearBombColor() {
			bombColor = BombColor.defaultColor;
			NotifyPropertyChanged("bombColor");
		}
		void ClearMenuLightColor() {
			menuLightColor = new Color(0, 0, 0, 0);
			NotifyPropertyChanged("menuLightColor");
		}
		void ClearWallOutlineColor() {
			config.wallBorderColor = WallOutline.defaultColor;
			NotifyPropertyChanged("wallOutlineColor");
		}

		Color bombColor {
			get => config.bombColor;
			set => config.bombColor = value.ColorWithAlpha(1);
		}
		Color menuLightColor {
			get => config.menuLightColor;
			set => config.menuLightColor = value;
		}

		Color wallOutlineColor {
			get => config.wallBorderColor;
			set => config.wallBorderColor = value.ColorWithAlpha(1);
		}


		private readonly string version = $"Version {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)} by Kinsi55";

		[UIComponent("sponsorsText")] CurvedTextMeshPro sponsorsText = null;
		[UIComponent("sponsorsModal")] ModalView sponsorsModal = null;
		[UIParams] BSMLParserParams parserParams = null;
		SponsorRequest sponsorRequest;
		int sponsorRevision;
		bool sponsorsOpen;
		bool sponsorsDestroyed;
		static int sponsorBrowserOpening;

		sealed class SponsorRequest {
			public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
			public readonly CurvedTextMeshPro Text;
			public readonly ModalView Modal;
			public int Revision;
			public bool Retired;
			public Task Completion;

			public SponsorRequest(CurvedTextMeshPro text, ModalView modal, int revision) {
				Text = text;
				Modal = modal;
				Revision = revision;
			}
		}

		void OpenSponsorsLink() {
			if(Interlocked.CompareExchange(ref sponsorBrowserOpening, 1, 0) != 0)
				return;
			try {
				var thread = new Thread(OpenSponsorsBrowser) { IsBackground = true, Name = "Tweaks55 Sponsors" };
				// URL shell handlers can require STA; set it before starting the thread.
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
			} catch {
				Interlocked.Exchange(ref sponsorBrowserOpening, 0);
				throw;
			}
		}

		static void OpenSponsorsBrowser() {
			try {
				using(var process = Process.Start("https://github.com/sponsors/kinsi55")) { }
			} catch(Exception ex) {
				Plugin.Log.Error(ex);
			} finally {
				Interlocked.Exchange(ref sponsorBrowserOpening, 0);
			}
		}

		[UIAction("#post-parse")]
		void SponsorsParsed() {
			RetireSponsorRequest();
			var parsed = parserParams;
			parsed.AddEvent("CloseSponsorModal", () => {
				if(ReferenceEquals(parserParams, parsed))
					RetireSponsorRequest();
			});
		}

		protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling) {
			RetireSponsorRequest();
			base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
		}

		protected override void OnDestroy() {
			sponsorsDestroyed = true;
			RetireSponsorRequest();
			base.OnDestroy();
		}

		internal void RetireSponsorRequest() {
			sponsorsOpen = false;
			sponsorRevision++;
			RetireRequest(sponsorRequest);
		}

		static void RetireRequest(SponsorRequest request) {
			if(request == null || request.Retired)
				return;
			request.Retired = true;
			// Retain the request slot until the download and worker abort have finished.
			request.Cancellation.Cancel();
		}

		void OpenSponsorsModal() {
			var text = sponsorsText;
			if(sponsorsDestroyed || this == null || !Plugin.enabled || !isActivated || !isActiveAndEnabled || text == null || sponsorsModal == null)
				return;
			sponsorsOpen = true;
			var revision = ++sponsorRevision;
			text.text = "Loading...";
			if(!CanUseSponsors(text, revision))
				return;
			if(sponsorRequest != null && sponsorRequest.Completion != null && sponsorRequest.Completion.IsCompleted) {
				sponsorRequest.Cancellation.Dispose();
				sponsorRequest = null;
			}
			if(sponsorRequest != null) {
				if(!sponsorRequest.Retired && ReferenceEquals(sponsorRequest.Text, text) && ReferenceEquals(sponsorRequest.Modal, sponsorsModal))
					sponsorRequest.Revision = revision;
				else
					RetireRequest(sponsorRequest);
				return;
			}
			StartSponsorRequest(text, revision);
		}

		void StartSponsorRequest(CurvedTextMeshPro text, int revision) {
			var request = new SponsorRequest(text, sponsorsModal, revision);
			sponsorRequest = request;
			var token = request.Cancellation.Token;
			var download = Task.Run(() => DownloadSponsors(token), token);
			request.Completion = CompleteSponsorsAsync(request, download);
		}

		async Task CompleteSponsorsAsync(SponsorRequest request, Task<string> download) {
			string description = null;
			try {
				description = await download.ConfigureAwait(false);
			} catch(OperationCanceledException) {
			} catch {
				description = "Failed to load";
			}
			try {
				await UnityMainThreadTaskScheduler.Factory.StartNew(() => FinishSponsorRequest(request, description)).ConfigureAwait(false);
			} catch(Exception ex) {
				Plugin.Log.Error(ex);
			}
		}

		bool CanUseSponsors(CurvedTextMeshPro text, int revision) {
			return !sponsorsDestroyed && sponsorsOpen && sponsorRevision == revision && Plugin.enabled && this != null
				&& isActivated && isActiveAndEnabled && text != null && ReferenceEquals(sponsorsText, text) && sponsorsModal != null;
		}

		bool CanPublishSponsors(SponsorRequest request, int revision) {
			return ReferenceEquals(sponsorRequest, request) && !request.Retired && !request.Cancellation.IsCancellationRequested
				&& CanUseSponsors(request.Text, revision) && ReferenceEquals(sponsorsModal, request.Modal)
				&& request.Modal.gameObject.activeInHierarchy;
		}

		void FinishSponsorRequest(SponsorRequest request, string description) {
			var revision = request.Revision;
			try {
				if(description == null || !CanPublishSponsors(request, revision))
					return;
				request.Text.text = description;
				if(!CanPublishSponsors(request, revision))
					return;
				request.Text.gameObject.SetActive(false);
				if(CanPublishSponsors(request, revision))
					request.Text.gameObject.SetActive(true);
			} finally {
				if(ReferenceEquals(sponsorRequest, request)) {
					sponsorRequest = null;
					request.Cancellation.Dispose();
					if((request.Retired || sponsorRevision != revision || !ReferenceEquals(sponsorsText, request.Text))
						&& CanUseSponsors(sponsorsText, sponsorRevision))
						StartSponsorRequest(sponsorsText, sponsorRevision);
				}
			}
		}

		static async Task<string> DownloadSponsors(CancellationToken token) {
			try {
				token.ThrowIfCancellationRequested();
				using(var client = new WebClient()) {
					var download = client.DownloadStringTaskAsync("http://kinsi.me/sponsors/bsout.php");
					Task abort = null;
					// WebClient resets cancellation during kickoff, so register after starting.
					var registration = token.Register(() => abort = Task.Run(() => client.CancelAsync()));
					try {
						var description = await download.ConfigureAwait(false);
						token.ThrowIfCancellationRequested();
						return description;
					} finally {
						// Disposal waits for the callback to finish assigning its abort task.
						registration.Dispose();
						if(abort != null)
							await abort.ConfigureAwait(false);
					}
				}
			} catch {
				token.ThrowIfCancellationRequested();
				return "Failed to load";
			}
		}
	}

	public static class BsmlWrapper {
		static readonly bool hasBsml = IPA.Loader.PluginManager.GetPluginFromId("BeatSaberMarkupLanguage") != null;

		public static void EnableUI() {
			void wrap() => TweaksFlowCoordinator.Initialize();

			if(hasBsml)
				wrap();
		}
		public static void DisableUI() {
			void wrap() => TweaksFlowCoordinator.Deinit();

			if(hasBsml)
				wrap();
		}
	}
}
