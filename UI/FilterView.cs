﻿using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using BetterSongSearch.Configuration;
using BetterSongSearch.Util;
using HarmonyLib;
using HMUI;
using IPA.Utilities;
using SongDetailsCache.Structs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterSongSearch.UI {

	[HotReload(RelativePathToLayout = @"Views\FilterView.bsml")]
	[ViewDefinition("BetterSongSearch.UI.Views.FilterView.bsml")]
	class FilterView : BSMLAutomaticViewController, INotifyPropertyChanged {
		public static List<DateTime> hideOlderThanOptions { get; private set; }
		public static PluginConfig cfgInstance;

		public void Awake() {
			if(hideOlderThanOptions != null)
				return;

			hideOlderThanOptions = new List<DateTime>();

			for(var x = new DateTime(2018, 5, 1); x < DateTime.Now; x = x.AddMonths(1))
				hideOlderThanOptions.Add(x);

			FilterPresets.Init();
		}

		public static FilterOptions currentFilter = new FilterOptions();

		[UIComponent("dontScuffBg")] ModalView dontScuffBg = null;
		[UIComponent("filterbarContainer")] Transform filterbarContainer = null;
		//[UIComponent("modsRequirementDropdown")] DropdownWithTableView _modsRequirementDropdown = null;

		[UIAction("#post-parse")]
		void Parsed() {
			currentFilter.hideOlderThanSlider.slider.maxValue = hideOlderThanOptions.Count - 1;
			currentFilter.hideOlderThanSlider.Value = 0;
			currentFilter.hideOlderThanSlider.ReceiveValue();

			((RectTransform)gameObject.transform).offsetMax = new Vector2(20, 22);

			BSMLStuff.GetScrollbarForTable(presetList.tableView.gameObject, _presetScrollbarContainer.transform);

			// BSML / HMUI my beloved
			ReflectionUtil.SetField(dontScuffBg, "_animateParentCanvas", false);
			ReflectionUtil.SetField(newPresetName.modalKeyboard.modalView, "_animateParentCanvas", false);

			StartCoroutine(BSMLStuff.MergeSliders(gameObject));

			// I hate BSML some times
			var m = ReflectionUtil.GetField<ModalView, DropdownWithTableView>(
				GetComponentsInChildren<DropDownListSetting>().Where(x => x.associatedValue.MemberName == "mods").First().GetComponent<DropdownWithTableView>()
			, "_modalView");
			((RectTransform)m.transform).pivot = new Vector2(0.5f, 0.3f);

			// This is garbage
			foreach(var x in GetComponentsInChildren<Backgroundable>().Select(x => x.GetComponent<ImageView>())) {
				if(!x || x.color0 != Color.white || x.sprite.name != "RoundRect10")
					continue;

				ReflectionUtil.SetField(x, "_skew", 0f);
				x.overrideSprite = null;
				x.SetImage("#RoundRect10BorderFade");
				x.color = new Color(0, 0.7f, 1f, 0.4f);
			}

			foreach(var x in filterbarContainer.GetComponentsInChildren<ImageView>().Where(x => x.gameObject.name == "Underline"))
				x.SetImage("#RoundRect10BorderFade");
		}

		[UIAction("ReloadSongsTable")] void ReloadSongsTable() => BSSFlowCoordinator.songListView.UpdateSearchedSongsList();

		#region PresetStuff
		class FilterPresetRow {
			public readonly string name;
			[UIComponent("label")] TextMeshProUGUI label = null;

			public FilterPresetRow(string name) => this.name = name;

			[UIAction("refresh-visuals")]
			public void Refresh(bool selected, bool highlighted) {
				label.color = new UnityEngine.Color(
					selected ? 0 : 255,
					selected ? 128 : 255,
					selected ? 128 : 255,
					highlighted ? 0.9f : 0.6f
				);
			}
		}

		[UIComponent("loadButton")] private NoTransitionsButton loadButton = null;
		[UIComponent("deleteButton")] private NoTransitionsButton deleteButton = null;
		[UIComponent("presetList")] private CustomCellListTableData presetList = null;
		[UIComponent("newPresetName")] private StringSetting newPresetName = null;
		[UIComponent("presetScrollbarContainer")] private VerticalLayoutGroup _presetScrollbarContainer = null;
		void ClearFilters() => SetFilter();

		void ReloadPresets() {
			presetList.data = FilterPresets.presets.Select(x => new FilterPresetRow(x.Key)).ToList<object>();
			presetList.tableView.ReloadData();
			presetList.tableView.ClearSelection();

			loadButton.interactable = false;
			deleteButton.interactable = false;

			newPresetName.Text = "";
		}

		string curSelected;
		void PresetSelected(object _, FilterPresetRow row) {
			loadButton.interactable = true;
			deleteButton.interactable = true;
			newPresetName.Text = curSelected = row.name;
		}

		void SetFilter(FilterOptions filter = null) {
			filter ??= new FilterOptions();
			foreach(var x in AccessTools.GetDeclaredProperties(typeof(FilterOptions)))
				x.SetValue(currentFilter, x.GetValue(filter));

			currentFilter.NotifyPropertiesChanged();
			/*
			 * This is a massive hack and I have NO IDEA why I need to do this. If I dont do this, or
			 * the FilterSongs() method NEVER ends up being called, not even if I manually invoke
			 * limitedUpdateData.Call[NextFrame]() OR EVEN BSSFlowCoordinator.FilterSongs() DIRECTLY
			 * Seems like there is SOMETHING broken with how input changes are handled, something to do
			 * with nested coroutines or whatever. I have no idea. For now I spent enough time trying to fix this
			 */
			currentFilter.hideOlderThanSlider.onChange.Invoke(currentFilter.hideOlderThanSlider.Value);
		}

		void AddPreset() {
			FilterPresets.Save(newPresetName.Text);
			ReloadPresets();
		}

		void LoadPreset() {
			SetFilter(FilterPresets.presets[curSelected]);
		}
		void DeletePreset() {
			FilterPresets.Delete(curSelected);
			ReloadPresets();
		}
		#endregion


		#region filters
		static bool requiresScore => (currentFilter.existingScore == (string)FilterOptions.scoreFilterOptions[2]) || SongListController.opt_sort == "Worst local score";

		public bool DifficultyCheck(in SongDifficulty diff) {
			if(diff.stars < currentFilter.minimumStars || diff.stars > currentFilter.maximumStars)
				return false;

			if(currentFilter.difficulty_int != -1 && (int)diff.difficulty != currentFilter.difficulty_int)
				return false;

			if(currentFilter.characteristic_int != -1 && (int)diff.characteristic != currentFilter.characteristic_int)
				return false;

			if(diff.njs < currentFilter.minimumNjs || diff.njs > currentFilter.maximumNjs)
				return false;

			if(currentFilter.rankedState == (string)FilterOptions.rankedFilterOptions[1] && !diff.ranked)
				return false;

			if(currentFilter.mods != (string)FilterOptions.modOptions[0]) {
				if(currentFilter.mods == (string)FilterOptions.modOptions[1]) {
					if((diff.mods & MapMods.NoodleExtensions) == 0)
						return false;
				} else if(currentFilter.mods == (string)FilterOptions.modOptions[2]) {
					if((diff.mods & MapMods.MappingExtensions) == 0)
						return false;
				} else if(currentFilter.mods == (string)FilterOptions.modOptions[3]) {
					if((diff.mods & MapMods.Chroma) == 0)
						return false;
				} else if(currentFilter.mods == (string)FilterOptions.modOptions[4]) {
					if((diff.mods & MapMods.Cinema) == 0)
						return false;
				}
			}

			if(diff.song.songDurationSeconds > 0) {
				var nps = diff.notes / (float)diff.song.songDurationSeconds;

				if(nps < currentFilter.minimumNps || nps > currentFilter.maximumNps)
					return false;
			}

			return true;
		}

		public bool SearchDifficultyCheck(SongSearchSong.SongSearchDiff diff) {
			if(currentFilter.existingScore != (string)FilterOptions.scoreFilterOptions[0] || requiresScore) {
				if(diff.CheckHasScore() != requiresScore)
					return false;
			}

			return true;
		}

		public bool SongCheck(in Song song) {
			if(song.uploadTime < currentFilter.hideOlderThan)
				return false;

			if(currentFilter.rankedState != (string)FilterOptions.rankedFilterOptions[0]) {
				if(currentFilter.rankedState == (string)FilterOptions.rankedFilterOptions[1]) {
					if(song.rankedStatus != RankedStatus.Ranked)
						return false;
				} else if(currentFilter.rankedState == (string)FilterOptions.rankedFilterOptions[2]) {
					if(song.rankedStatus != RankedStatus.Qualified)
						return false;
				}
			}

			if(song.songDurationSeconds > 0f && (song.songDurationSeconds < currentFilter.minimumSongLength * 60 || song.songDurationSeconds > currentFilter.maximumSongLength * 60))
				return false;

			var voteCount = song.downvotes + song.upvotes;

			if(voteCount < currentFilter.minimumVotes)
				return false;

			if(currentFilter.minimumRating > 0f && (currentFilter.minimumRating > song.rating || voteCount == 0))
				return false;

			return true;
		}

		public bool SearchSongCheck(SongSearchSong song) {
			if(currentFilter.existingSongs != (string)FilterOptions.downloadedFilterOptions[0]) {
				if(SongCore.Collections.songWithHashPresent(song.hash) == (currentFilter.existingSongs == (string)FilterOptions.downloadedFilterOptions[2]))
					return false;
			}

			if(currentFilter.existingScore != (string)FilterOptions.scoreFilterOptions[0] || requiresScore) {
				if(song.CheckHasScore() != requiresScore)
					return false;
			}

			if(currentFilter.uploaders.Count != 0) {
				if(currentFilter.uploaders.Contains(song.uploaderNameLowercase)) {
					if(currentFilter.uploadersBlacklist)
						return false;
				} else if(!currentFilter.uploadersBlacklist) {
					return false;
				}
			}

			return true;
		}
		#endregion


		[UIComponent("sponsorsText")] CurvedTextMeshPro sponsorsText = null;
		void OpenSponsorsLink() => Process.Start("https://github.com/sponsors/kinsi55");
		void OpenSponsorsModal() {
			sponsorsText.text = "Loading...";
			Task.Run(() => {
				string desc = "Failed to load";
				try {
					desc = (new WebClient()).DownloadString("http://kinsi.me/sponsors/bsout.php");
				} catch { }

				_ = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					sponsorsText.text = desc;
					// There is almost certainly a better way to update / correctly set the scrollbar size...
					sponsorsText.gameObject.SetActive(false);
					sponsorsText.gameObject.SetActive(true);
				});
			}).ConfigureAwait(false);
		}


		public static RatelimitCoroutine limitedUpdateData { get; private set; } = new RatelimitCoroutine(BSSFlowCoordinator.FilterSongs, 0.1f);

		readonly string version = $" BetterSongSearch v{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)} by Kinsi55";
		[UIComponent("datasetInfoLabel")] private TextMeshProUGUI _datasetInfoLabel = null;
		public TextMeshProUGUI datasetInfoLabel => _datasetInfoLabel;
	}
}
