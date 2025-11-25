using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using FrooxEngine.UIX;
using HarmonyLib;
using Newtonsoft.Json;
// using ResoniteHotReloadLib;
using ResoniteModLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InventoryHelper
{
    public class CoreSearch : ResoniteMod
    {
        internal const string VERSION_CONSTANT = "3.0.0";
        public override string Name => "InventoryHelper";
        public override string Author => "Noble, kaan";
        public override string Version => VERSION_CONSTANT;
        public override string Link => "https://github.com/noblereign/InventorySearch";
        const string harmonyId = "dev.kaan.InventorySearch";

        private static Dictionary<string, SerializableRecord> _cache = new Dictionary<string, SerializableRecord>();
        private static RecordConverter _cacheConverter = new RecordConverter();

        public enum SearchType
        {
            EntireInventory,
            FocusedRecursive,
            FocusedNonRecursive
        }

        private static readonly string CacheFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InventorySearchCache.json");

        [AutoRegisterConfigKey] private static readonly ModConfigurationKey<string> CacheConfig =
            new ModConfigurationKey<string>("Cache Config Location",
                "Save InventorySearchCache to a separate location.", () => CacheFilePath);

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<SearchType> SearchStrategy = new ModConfigurationKey<SearchType>("Search Scope", "When searching, what parts of your inventory should be considered?\n\n<color=yellow>NOTE:</color> Caches from before v3.0.0 only support <b>EntireInventory</b>. If you have an older cache, it will be slowly updated to the new format as you navigate through folders.", () => SearchType.EntireInventory);

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> RememberPasteOnLaunch = new("Remember copied item on launch", "Should items in the 'clipboard' persist across launches?", () => false);

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> DebugLogging = new("Enable debug logging", "Print debug logs when records are cached, copied, pasted, etc? <b>Enabling this may cause sensitive information to be saved to your logs.</b>", () => false);

        private static ModConfiguration? Config;
        private static TextField? LocalTextField;

        private static Button? CopyItemButton;
        private static Button? PasteItemButton;
        private static Button? CutItemButton;

        private static Harmony? har;

        /*static void BeforeHotReload()
        {
            har.UnpatchAll(harmonyId);
            // Record yeah = new();
            // CopyItemButton.LocalPressed -= TransferItemsButtonOnLocalPressed;
        }

        static void OnHotReload(ResoniteMod modInstance)
        {
            har = new Harmony(harmonyId);
            har.PatchAll();

            Config = modInstance.GetConfiguration();
            Config?.Save(true);

            LoadCacheFromFile();

            Console.WriteLine("I reloaded uwu");
        }*/

        public override void OnEngineInit()
        {
            har = new Harmony(harmonyId);
            har.PatchAll();

            Config = GetConfiguration();
            Config?.Save(true);

            LoadCacheFromFile();

            if (Config!.GetValue(RememberPasteOnLaunch) != true)
            {
                if (_cache.ContainsKey("CopiedItem"))
                {
                    _cache.Remove("CopiedItem");
                }
                if (_cache.ContainsKey("CutItem"))
                {
                    _cache.Remove("CutItem");
                }
            }
            // HotReloader.RegisterForHotReload(this);
        }
        private static void DoUIUpdate(ref InventoryBrowser browser)
        {
            var __0 = browser.CurrentDirectory;
            if (browser.CanInteract(browser.LocalUser))
            {

                var selectionIsCuttable = true;
                if (CachedInventory.SelectedInventoryItem != null)
                {
                    var SelectedDirectory = typeof(InventoryItemUI).GetField("Directory", AccessTools.all)?
                                        .GetValue(CachedInventory.SelectedInventoryItem) as RecordDirectory;

                    if (SelectedDirectory != null)
                    {
                        selectionIsCuttable = SelectedDirectory.IsLink == true;
                    }
                }
                
                if (CopyItemButton != null)
                {
                    CopyItemButton.Enabled = browser.SelectedInventoryItem != null;
                }
                if (PasteItemButton != null)
                {
                    PasteItemButton.Enabled = (__0 != null && __0.CanWrite && _cache.ContainsKey("CopiedItem"));
                }
                if (CutItemButton != null)
                {
                    CutItemButton.Enabled = __0 != null && __0.CanWrite && browser.SelectedInventoryItem != null && selectionIsCuttable;
                }
            }
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        class InventorySearchCoreHarmony
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(InventoryBrowser), nameof(InventoryBrowser.Open))]
            public static void OnOpen(ref RecordDirectory __0, ref InventoryBrowser __instance, SyncRef<Slot> ____buttonsRoot)
            {
                CacheDirectoryRecords(__0);
                SaveCacheToFile();
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(InventoryBrowser), nameof(InventoryBrowser.OpenDirectory))]
            public async static void OnDirectoryOpening(RecordDirectory directory, InventoryBrowser __instance)
            {
                var doDebugLogging = Config!.GetValue(DebugLogging);

                if (doDebugLogging) 
                {
                    Debug($"Waiting for directory {directory.Name}...");
                }

                await directory.EnsureFullyLoaded();
                if (directory == __instance.CurrentDirectory)
                {
                    Debug("Directory loaded, attempting to cache");
                    CacheDirectoryRecords(directory);
                    SaveCacheToFile();
                    Debug("Directory has been cached");
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(InventoryBrowser), "OnItemSelected")]
            public static void OnUserInvokeUI(ref InventoryBrowser __instance, SyncRef<Slot> ____buttonsRoot)
            {
                var __0 = __instance.CurrentDirectory;
                CachedInventory = __instance;
                var buttonRoot = ____buttonsRoot.Slot.Parent;
                var ui = new UIBuilder(buttonRoot);

                var verticalLayout = ui.HorizontalLayout(5, 0, Alignment.MiddleCenter);

                verticalLayout.Slot.Name = "InventoryHelperButtons";
                verticalLayout.ForceExpandWidth.Value = true;
                verticalLayout.Slot.GetComponent<RectTransform>().AnchorMin.Value = new float2(0, 1);
                verticalLayout.Slot.GetComponent<RectTransform>().AnchorMax.Value = new float2(1, 1);
                verticalLayout.Slot.GetComponent<RectTransform>().OffsetMin.Value = new float2(0, -30);
                verticalLayout.Slot.GetComponent<RectTransform>().OffsetMax.Value = new float2(-10, -10);

                TextField(ui, "Search Bar", __instance);
                Button(ui, "Copy", __instance, __0);
                Button(ui, "Paste", __instance, __0);
                Button(ui, "Cut", __instance, __0);

                CacheDirectoryRecords(__0);
                SaveCacheToFile();
                DoUIUpdate(ref CachedInventory);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(InventoryBrowser), "OnChanges")]
            public static void OnUIUpdated(ref InventoryBrowser __instance)
            {
                DoUIUpdate(ref __instance);
            }

            private static void CacheDirectoryRecords(object directory)
            {
                if (directory == null)
                {
                    Warn("No directory given for cache, skipping");
                    return;
                }

                RecordDirectory typedDirectory = (RecordDirectory)directory;
                if (typedDirectory.OwnerId == null || typedDirectory.OwnerId == "NONE")
                {
                    Warn("Directory has no owner, skipping");
                    return;
                }

                var RecordsField = directory.GetType().GetField("records", AccessTools.all);
                var SubdirectoriesField = directory.GetType().GetField("subdirectories", AccessTools.all);

                if (RecordsField != null)
                {
                    var Records = (IEnumerable)RecordsField.GetValue(directory);
                    foreach (Record record in Records)
                    {
                        var id = GetPropertyValue(record, "RecordId") ?? record?.RecordId;
                        if (id != null && _cache.TryGetValue(id, out SerializableRecord? value) && value.Path != null) continue;

                        if (record.RecordType != "directory" && record.RecordType != "link")
                        {
                            var serializableRecord = new SerializableRecord(record);
                            _cache[id] = serializableRecord;

                            if (Config!.GetValue(DebugLogging))
                            {
                                Debug($"Cached record: {id}, {serializableRecord.Name}, type: {record.RecordType}");
                            }
                        }
                        else // in some cases, subdirectories show up as normal records (like when they're newly made). this is here to catch that
                        {
                            var serializableRecordDirectory = new SerializableRecordDirectory(record);
                            _cache[id] = serializableRecordDirectory;

                            if (Config!.GetValue(DebugLogging))
                            {
                                Debug($"Cached record as subdirectory: {id}, {serializableRecordDirectory.Name}");
                            }
                        }
                    }
                }

                if (SubdirectoriesField == null) return;

                var Subdirectories = (IEnumerable)SubdirectoriesField.GetValue(directory);
                foreach (RecordDirectory Subdirectory in Subdirectories)
                {
                    var record = Subdirectory.EntryRecord;

                    var id = GetPropertyValue(record, "RecordId") ?? record?.RecordId;
                    if (id != null && _cache.TryGetValue(id, out SerializableRecord? value) && value.Path != null) continue;

                    var serializableRecordDirectory = new SerializableRecordDirectory(Subdirectory.EntryRecord);
                    _cache[id] = serializableRecordDirectory;

                    if (Config!.GetValue(DebugLogging))
                    {
                        Debug($"Cached subdirectory: {id}, {serializableRecordDirectory.Name}");
                    }

                    CacheDirectoryRecords(Subdirectory);
                }
            }
        }

        private static void SaveCacheToFile()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
                File.WriteAllText(CacheFilePath, json);
                // Console.WriteLine($"Cache saved to file: {CacheFilePath}");
                // Console.WriteLine($"Cache entries: {_cache.Count}");
            }
            catch (Exception e)
            {
                // Console.WriteLine($"Error saving cache to file: {e.Message}");
            }
        }

        private static void LoadCacheFromFile()
        {
            if (!File.Exists(CacheFilePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(Config.GetValue(CacheConfig));
                _cache = JsonConvert.DeserializeObject<Dictionary<string, SerializableRecord>>(json, _cacheConverter);
                // Console.WriteLine($"Cache loaded from file. Entries: {_cache.Count}");
            }
            catch (Exception e)
            {
                // Console.WriteLine($"Error loading cache from file: {e.Message}");
            }
        }

        private static BrowserItem CachedItem;
        private static InventoryBrowser CachedInventory;
        private static RecordDirectory CachedDir;

        private static void Button(UIBuilder UI, string Tag, InventoryBrowser inventoryBrowser, RecordDirectory __0)
        {
            RadiantUI_Constants.SetupEditorStyle(UI, extraPadding: true);

            EnsureButtonInitialized(ref CopyItemButton, UI, "Copy", CopyItemButtonOnLocalPressed);
            EnsureButtonInitialized(ref PasteItemButton, UI, "Paste", PasteItemButtonOnLocalPressed);
            EnsureButtonInitialized(ref CutItemButton, UI, "Cut", CutItemButtonOnLocalPressed);

            if (inventoryBrowser.SelectedItem?.Target != null && inventoryBrowser.SelectedItem.Target.IsFolder())
            {
                CachedItem = inventoryBrowser.SelectedInventoryItem;
            }

            CachedInventory = inventoryBrowser;
        }

        private static void EnsureButtonInitialized(ref Button button, UIBuilder UI, string text,
            ButtonEventHandler onPressed)
        {
            if (button != null) return;

            button = UI.Button(text: text);
            button.LocalPressed += onPressed;
        }


        private static string CleanString(string str)
        {
            return new StringRenderTree(str).GetRawString();
        }

        private static void CopyItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var SelectedItem = typeof(InventoryItemUI).GetField("Item", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as Record;

            var SelectedDirectory = typeof(InventoryItemUI).GetField("Directory", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as RecordDirectory;

            if (SelectedItem == null && SelectedDirectory == null)
            {
                NotificationMessage.SpawnTextMessage("No copiable item selected!", colorX.Red);
                return;
            }

            var SelectedObjectRecordId = SelectedItem != null ? SelectedItem.RecordId : (SelectedDirectory != null ? SelectedDirectory.EntryRecord.RecordId : null);

            if (SelectedObjectRecordId == null)
            {
                NotificationMessage.SpawnTextMessage("Failed to get Record ID for selected object.", colorX.Red);
                return;
            }

            var isDirectory = false;
            string SelectedRecordId =
                (from record in CachedInventory.CurrentDirectory.Records
                 where SelectedObjectRecordId == record.RecordId
                 select record.RecordId).FirstOrDefault();

            if (SelectedRecordId == null) // try again but as a directory
            {
                isDirectory = true;
                SelectedRecordId =
                (from record in CachedInventory.CurrentDirectory.Subdirectories
                 where SelectedObjectRecordId == record.EntryRecord.RecordId
                 select record.EntryRecord.RecordId).FirstOrDefault();

                if (SelectedRecordId == null)
                {
                    NotificationMessage.SpawnTextMessage("Selected item not found in records!!", colorX.Red);
                    return;
                }
            }

            if (_cache.ContainsKey(SelectedRecordId))
            {
                _cache["CopiedItem"] = _cache[SelectedRecordId];
                if (_cache.ContainsKey("CutItem"))
                {
                    _cache.Remove("CutItem");
                }
                NotificationMessage.SpawnTextMessage($"Copied: {CleanString(_cache[SelectedRecordId].Name)}", colorX.FromHexCode("#61B9FF"), 0.5f, 3f, 0.25f, 0.5f, 0.14f);
            }
            else
            {
                Msg($"No entries found for {SelectedRecordId}.");
            }

            DoUIUpdate(ref CachedInventory);
        }

        private static void PasteItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var copiedItem = _cache["CopiedItem"];

            if (Config!.GetValue(DebugLogging))
            {
                Debug($" COPIED: {copiedItem} {copiedItem.ToRecord()}");
            }

            //Msg(CachedInventory.CurrentDirectory.Name);

            if (!_cache.ContainsKey("CopiedItem"))
            {
                NotificationMessage.SpawnTextMessage($"No item copied!", colorX.Red);
                return;
            }

            var isCut = _cache.TryGetValue("CutItem", out SerializableRecord? value) && value == copiedItem;

            if (isCut) {
                var id = GetPropertyValue(copiedItem, "RecordId") ?? copiedItem.RecordId;
                _cache.Remove(id);
            }

            var cuttingPermitted = true;
            if (copiedItem is SerializableRecordDirectory copiedItemAsDirectory)
            {
                cuttingPermitted = copiedItemAsDirectory.RecordType == "link";

                // links have asset uris while directories don't, so if there isn't one we gotta make it ourselves
                Uri linkUri = copiedItem.AssetURI != null ? new Uri(copiedItem.AssetURI) : Engine.Current.PlatformProfile.GetRecordUri(copiedItemAsDirectory.OwnerId, copiedItemAsDirectory.RecordId);

                CachedInventory.CurrentDirectory.AddLinkAsync(copiedItem.Name, linkUri);
            }
            else
            {
                CachedInventory.CurrentDirectory.AddItem(copiedItem.Name, new Uri(copiedItem.AssetURI),
                    new Uri(copiedItem.ThumbnailURI));
            }

            var recordsField = typeof(RecordDirectory).GetField("records", AccessTools.all);
            if (recordsField != null)
            {
                if (isCut && cuttingPermitted)
                {
                    var records = (IList)recordsField.GetValue(CachedDir);
                    var recordToRemove = records.Cast<Record>().FirstOrDefault(r => r.RecordId == copiedItem.RecordId);
                    if (recordToRemove != null)
                    {
                        records.Remove(recordToRemove);
                        Engine.Current.RecordManager.DeleteRecord(recordToRemove);
                    }
                    else
                    {
                        // try again as directory
                        var subdirsField = typeof(RecordDirectory).GetField("subdirectories", AccessTools.all);
                        if (subdirsField != null)
                        {
                            var subdirs = (IList)subdirsField.GetValue(CachedDir);
                            var subdirToRemove = subdirs.Cast<RecordDirectory>().FirstOrDefault(r => r.EntryRecord.RecordId == copiedItem.RecordId);
                            if (subdirToRemove != null)
                            {
                                subdirs.Remove(subdirToRemove);
                                Engine.Current.RecordManager.DeleteRecord(subdirToRemove.EntryRecord);
                            }
                            else
                            {
                                if (Config!.GetValue(DebugLogging))
                                {
                                    Warn($"Record with ID {copiedItem.RecordId} not found in CachedDir!");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Warn("Failed to retrieve 'records' field from CachedDir.");
            }

            if (isCut)
            {
                if (cuttingPermitted)
                {
                    NotificationMessage.SpawnTextMessage($"Moved {CleanString(copiedItem.Name)} from {CleanString(CachedDir.Name)} to {CleanString(CachedInventory.CurrentDirectory.Name)}", colorX.Green, 0.5f, 4.5f, 0.3f, 0.5f, 0.17f);
                    _cache.Remove("CopiedItem");
                }
                else
                {
                    NotificationMessage.SpawnTextMessage($"Pasted {CleanString(copiedItem.Name)} into {CleanString(CachedInventory.CurrentDirectory.Name)}", colorX.Green, 0.5f, 3.5f, 0.3f, 0.5f, 0.17f);
                    NotificationMessage.SpawnTextMessage($"(Cutting isn't permitted on directories)", colorX.Orange, 0.5f, 3.5f, 0.175f, 0.5f, 0.11f);
                }
            }
            else
            {
                NotificationMessage.SpawnTextMessage($"Pasted {CleanString(copiedItem.Name)} into {CleanString(CachedInventory.CurrentDirectory.Name)}", colorX.Green, 0.5f, 3.5f, 0.3f, 0.5f, 0.17f);
            }
            DoUIUpdate(ref CachedInventory);
        }


        private static void CutItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var SelectedItem = typeof(InventoryItemUI).GetField("Item", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as Record;

            var SelectedDirectory = typeof(InventoryItemUI).GetField("Directory", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as RecordDirectory;

            if (SelectedItem == null && SelectedDirectory == null)
            {
                NotificationMessage.SpawnTextMessage("No copiable item selected!", colorX.Red);
                return;
            }

            var SelectedObjectRecordId = SelectedItem != null ? SelectedItem.RecordId : (SelectedDirectory != null ? SelectedDirectory.EntryRecord.RecordId : null);

            if (SelectedObjectRecordId == null)
            {
                NotificationMessage.SpawnTextMessage("Failed to get Record ID for selected object.", colorX.Red);
                return;
            }

            var isDirectory = false;
            string SelectedRecordId =
                (from record in CachedInventory.CurrentDirectory.Records
                 where SelectedObjectRecordId == record.RecordId
                 select record.RecordId).FirstOrDefault();

            if (SelectedRecordId == null) // try again but as a directory
            {
                isDirectory = true;
                SelectedRecordId =
                (from record in CachedInventory.CurrentDirectory.Subdirectories
                 where SelectedObjectRecordId == record.EntryRecord.RecordId
                 select record.EntryRecord.RecordId).FirstOrDefault();

                if (SelectedRecordId == null)
                {
                    NotificationMessage.SpawnTextMessage("Selected item not found in records!!", colorX.Red);
                    return;
                }
            }

            // i'm not entirely sure why Cut is done like this.
            // i'm assuming maybe it was supposed to have some kind of other complex behavior?
            // but the behavior i observed from the OG mod was basically just Copy with a different name really
            // either way i'll just keep it how it is (but updated for folder support), in case it's important somehow...

            if (string.IsNullOrEmpty(SelectedItem?.Name) && string.IsNullOrEmpty(SelectedDirectory?.Name))
            {
                NotificationMessage.SpawnTextMessage($"No item selected!", colorX.Red);
                return;
            }

            var SelectedObjectName = SelectedItem != null ? SelectedItem.Name : (SelectedDirectory != null ? SelectedDirectory.Name : null);

            var matchedEntries = _cache
                .Where(kvp => kvp.Value.RecordId.Equals(SelectedObjectRecordId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedEntries.Count == 0)
            {
                Msg($"No entries found for {SelectedObjectName}.");
                return;
            }

            foreach (var entry in matchedEntries)
            {
                _cache["CopiedItem"] = entry.Value;
                _cache["CutItem"] = entry.Value;
                CachedDir = CachedInventory.CurrentDirectory;
                NotificationMessage.SpawnTextMessage($"Cut: {CleanString(entry.Value.Name)}", colorX.FromHexCode("#D386FC"), 0.5f, 3f, 0.25f, 0.5f, 0.14f);
                //_cache.Remove(entry.Key);
                break; // this calls many times. so breaking may do good.
            }
            DoUIUpdate(ref CachedInventory);
        }

        private static string? RemovePathRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            int firstSlashIndex = path.IndexOfAny(new char[] { '\\', '/' });

            if (firstSlashIndex == -1)
            {
                return null;
            }

            return path.Substring(firstSlashIndex + 1);
        }

        private static string? GetParentPath(string path)
        {
            int lastSlashIndex = path.LastIndexOfAny(new char[2] { '\\', '/' });

            if (lastSlashIndex == -1)
            {
                return null;
            }

            return RemovePathRoot(path.Substring(0, lastSlashIndex)); // unsure if this works for group inventories, i'm not in one to check...but it's similar to what's in InventoryBrowser, so
        }

        private static RecordDirectory? PreviousTempDirectory;
        private static void TextField(UIBuilder UI, string Tag, InventoryBrowser InventoryBrowser)
        {
            if (LocalTextField != null) return;

            RadiantUI_Constants.SetupEditorStyle(UI, extraPadding: true);
            UI.FitContent(SizeFit.Disabled, SizeFit.MinSize);
            UI.Style.MinHeight = 30;
            //UI.Style.MinWidth = 30;
            UI.PushStyle();
            LocalTextField = LocalTextField ?? UI.TextField(null, false, "Search", true, $"<alpha=#77><i>Search...");

            LocalTextField.Text.HorizontalAutoSize.Value = true;
            LocalTextField.Text.Size.Value = 39.55418f;
            LocalTextField.Text.Color.Value = RadiantUI_Constants.Neutrals.LIGHT;
            LocalTextField.Text.ParseRichText.Value = false;

            LocalTextField.Editor.Target.FinishHandling.Value = TextEditor.FinishAction.NullOnWhitespace;
            UI.Style.FlexibleHeight = 1f;

            LocalTextField.Slot.Tag = Tag;

            var buttonRect = LocalTextField.Slot.GetComponent<RectTransform>();
            var layoutElement = LocalTextField.Slot.AttachComponent<LayoutElement>();

            layoutElement.PreferredWidth.Value = 200f;
            layoutElement.PreferredHeight.Value = 50f;

            LocalTextField.Editor.Target.LocalSubmitPressed += async (Change) =>
            {
                var TextField = LocalTextField.Editor.Target.Text.Target.Text;
                if (string.IsNullOrEmpty(TextField)) return;

                var SearchTerm = TextField.ToLower();

                var strategy = Config!.GetValue(SearchStrategy);

                var currentPath = PreviousTempDirectory != InventoryBrowser.CurrentDirectory ? InventoryBrowser.CurrentDirectory.Path : PreviousTempDirectory.ParentDirectory.Path;

                var AllResults = _cache
                    .Where(Kvp => Kvp.Value != null
                                && !string.IsNullOrEmpty(Kvp.Value.Name)
                                && Kvp.Value.Name.ToLower().Contains(SearchTerm.ToLower())
                                && (strategy == SearchType.EntireInventory || (strategy == SearchType.FocusedRecursive && Kvp.Value.Path != null && Kvp.Value.Path.Contains(currentPath)) || (strategy == SearchType.FocusedNonRecursive && Kvp.Value.Path != null && currentPath == Kvp.Value.Path)))
                    .Select(Kvp => Kvp.Value)
                    .ToLookup(item => item is SerializableRecordDirectory);

                var SearchResults = AllResults[false]
                    .Select(item => item.ToRecord())
                    .ToList();

                var SearchResultsDirs = AllResults[true]
                    .Cast<SerializableRecordDirectory>()
                    .Select(item => item.ToRecordDirectory(InventoryBrowser.CurrentDirectory))
                    .ToList();

                var Records = new List<Record>(SearchResults);
                var SubDirs = new List<RecordDirectory>(SearchResultsDirs);

                var NewDir = new RecordDirectory(Engine.Current, SubDirs, Records);
                
                SetPropertyValue(NewDir, "CurrentLoadState", RecordDirectory.LoadState.NotLoaded);
                SetPropertyValue(NewDir, "Name", "Search Results");
                SetPropertyValue(NewDir, "ParentDirectory", PreviousTempDirectory != InventoryBrowser.CurrentDirectory ? InventoryBrowser.CurrentDirectory : PreviousTempDirectory.ParentDirectory);

                PreviousTempDirectory = NewDir;

                InventoryBrowser.Open(NewDir, SlideSwapRegion.Slide.Left);

                var rootDirectory = NewDir.GetRootDirectory();
                
                foreach (RecordDirectory subdir in NewDir.Subdirectories)
                {
                    string parentPath = GetParentPath(subdir.Path) ?? NewDir.ParentDirectory.Path;
                    if (parentPath != rootDirectory.Path)
                    {
                        RecordDirectory originalParent = await rootDirectory.GetSubdirectoryAtPath(parentPath);
                        SetPropertyValue(subdir, "ParentDirectory", originalParent);
                    }
                    else
                    {
                        SetPropertyValue(subdir, "ParentDirectory", rootDirectory);
                    }
                }

                SetPropertyValue(NewDir, "CurrentLoadState", RecordDirectory.LoadState.FullyLoaded);
                _ = NewDir.EnsureFullyLoaded();
            };
        }

        private static void SetPropertyValue<T>(T obj, string propertyName, object value)
        {
            var property = typeof(T).GetProperty(propertyName, AccessTools.all);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value);
            }
            else
            {
                throw new ArgumentException(
                    $"Property '{propertyName}' not found or is not writable on type '{typeof(T)}'.");
            }
        }

        private static string GetPropertyValue<T>(T obj, string propertyName)
        {
            var property = typeof(T).GetProperty(propertyName, AccessTools.all);
            if (property != null && property.CanRead)
            {
                return (string)property.GetValue(obj);
            }
            else
            {
                throw new ArgumentException(
                    $"Property '{propertyName}' not found or is not readable on type '{typeof(T)}'.");
            }
        }

        [Serializable]
        [JsonConverter(typeof(RecordConverter))]
        public class SerializableRecord
        {
            public string RecordId { get; set; }
            public string Name { get; set; }
            public string OwnerName { get; set; }
            public string AssetURI { get; set; }
            public string ThumbnailURI { get; set; }
            public string Path { get; set; }

            public SerializableRecord()
            {
            }

            public SerializableRecord(Record record)
            {
                RecordId = record.RecordId;
                Name = record.Name;
                OwnerName = record.OwnerName;
                AssetURI = record.AssetURI;
                ThumbnailURI = record.ThumbnailURI;
                Path = record.Path;
            }

            public Record ToRecord()
            {
                return new Record
                {
                    RecordId = RecordId,
                    Name = Name,
                    OwnerName = OwnerName,
                    AssetURI = AssetURI,
                    ThumbnailURI = ThumbnailURI,
                    Path = Path
                };
            }
        }

        [Serializable]
        [JsonConverter(typeof(RecordConverter))]
        public class SerializableRecordDirectory : SerializableRecord
        {
            public string OwnerId { get; set; }
            public string RecordType { get; set; }

            public SerializableRecordDirectory()
            {
            }

            public SerializableRecordDirectory(Record record)
            {
                RecordId = record.RecordId;
                Name = record.Name;
                OwnerName = record.OwnerName;
                OwnerId = record.OwnerId;
                Path = record.Path;
                RecordType = record.RecordType;
                AssetURI = record.AssetURI;
            }

            public new Record ToRecord()
            {
                return new Record
                {
                    RecordId = RecordId,
                    Name = Name,
                    OwnerName = OwnerName,
                    OwnerId = OwnerId,
                    Path = Path,
                    RecordType = RecordType,
                    AssetURI = AssetURI
                };
            }

            public RecordDirectory ToRecordDirectory(RecordDirectory parent)
            {
                return new RecordDirectory(this.ToRecord(), parent, Engine.Current);
            }
        }
    }
}
