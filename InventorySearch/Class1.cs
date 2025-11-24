using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements.Core;
using Newtonsoft.Json;
using FrooxEngine;
using FrooxEngine.Store;
using FrooxEngine.UIX;
using HarmonyLib;
// using ResoniteHotReloadLib;
using ResoniteModLoader;
using System.Text;

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
        public static readonly ModConfigurationKey<SearchType> SearchStrategy = new ModConfigurationKey<SearchType>("Search Scope", "When searching, what parts of your inventory should be considered?", () => SearchType.EntireInventory);

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

            Debug("I reloaded uwu");
            // HotReloader.RegisterForHotReload(this);
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        class InventorySearchCoreHarmony
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(InventoryBrowser), nameof(InventoryBrowser.Open))]
            public static void OnOpen(ref RecordDirectory __0, ref InventoryBrowser __instance,
                SyncRef<Slot> ____buttonsRoot)
            {
                CacheDirectoryRecords(__0);
                SaveCacheToFile();
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
            }

            private static void CacheDirectoryRecords(object directory)
            {
                if (directory == null)
                {
                    Warn("No directory given for cache, skipping");
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
                        if (id != null && _cache.ContainsKey(id)) continue;

                        var serializableRecord = new SerializableRecord(record);
                        _cache[id] = serializableRecord;

                        if (Config!.GetValue(DebugLogging))
                        {
                            Debug($"Cached record: {id}, {serializableRecord.Name}");
                        }
                    }
                }

                if (SubdirectoriesField == null) return;
                var Subdirectories = (IEnumerable)SubdirectoriesField.GetValue(directory);
                foreach (var Subdirectory in Subdirectories)
                {
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
                _cache = JsonConvert.DeserializeObject<Dictionary<string, SerializableRecord>>(json);
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

        private static void CopyItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var SelectedItem = typeof(InventoryItemUI).GetField("Item", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as Record;
            if (SelectedItem == null)
            {
                NotificationMessage.SpawnTextMessage("SelectedItem is null!! Please select an item.", colorX.Red);
                return;
            }

            string SelectedRecordId =
                (from record in CachedInventory.CurrentDirectory.Records
                    where SelectedItem.RecordId == record.RecordId
                    select record.RecordId).FirstOrDefault();

            if (SelectedRecordId == null)
            {
                NotificationMessage.SpawnTextMessage("Selected item not found in records!!", colorX.Red);
                return;
            }

            if (_cache.ContainsKey(SelectedRecordId))
            {
                _cache["CopiedItem"] = _cache[SelectedRecordId];
                NotificationMessage.SpawnTextMessage($"Copied item: {_cache[SelectedRecordId].Name}", colorX.Green);
            }
            else
            {
                Msg($"No entries found for {SelectedRecordId}.");
            }
        }

        private static void PasteItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var copiedItem = _cache["CopiedItem"];

            if (Config!.GetValue(DebugLogging))
            {
                Debug($" COPIED: {copiedItem} {copiedItem.ToRecord()}");
            }

            Msg(CachedInventory.CurrentDirectory.Name);
            
            if (!_cache.ContainsKey("CopiedItem"))
            {
                NotificationMessage.SpawnTextMessage($"No item copied!", colorX.Red);
                return;
            }

            CachedInventory.CurrentDirectory.AddItem(copiedItem.Name, new Uri(copiedItem.AssetURI),
                new Uri(copiedItem.ThumbnailURI));

            var recordsField = typeof(RecordDirectory).GetField("records", AccessTools.all);
            if (recordsField != null)
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
                    if (Config!.GetValue(DebugLogging))
                    {
                        Warn($"Record with ID {copiedItem.RecordId} not found in CachedDir.Records.");
                    }
                }
            }
            else
            {
                Warn("Failed to retrieve 'records' field from CachedDir.");
            }

            NotificationMessage.SpawnTextMessage(
                $"Pasted item: {copiedItem.Name}, Removed {copiedItem.Name} from {CachedDir.Name}", colorX.Green);
        }


        private static void CutItemButtonOnLocalPressed(IButton button, ButtonEventData eventdata)
        {
            var SelectedItem = typeof(InventoryItemUI).GetField("Item", AccessTools.all)?
                .GetValue(CachedInventory.SelectedInventoryItem) as Record;
            if (string.IsNullOrEmpty(SelectedItem?.Name))
            {
                NotificationMessage.SpawnTextMessage($"No item selected!", colorX.Red);
                return;
            }

            var matchedEntries = _cache
                .Where(kvp => kvp.Value.RecordId.Equals(SelectedItem.RecordId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedEntries.Count == 0)
            {
                Msg($"No entries found for {SelectedItem.Name}.");
                return;
            }

            foreach (var entry in matchedEntries)
            {
                _cache["CopiedItem"] = entry.Value;
                CachedDir = CachedInventory.CurrentDirectory;
                NotificationMessage.SpawnTextMessage($"Cut item: {entry.Value.Name}", colorX.Red);
                _cache.Remove(entry.Key);
                break; // this calls many times. so breaking may do good.
            }
        }
        
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

            LocalTextField.Editor.Target.LocalEditingFinished += (Change) =>
            {
                var TextField = LocalTextField.Editor.Target.Text.Target.Text;
                if (string.IsNullOrEmpty(TextField)) return;
                
                var SearchTerm = TextField.ToLower();

                var strategy = Config!.GetValue(SearchStrategy);

                var currentPath = InventoryBrowser.CurrentDirectory.Path;

                var SearchResults = _cache
                    .Where(Kvp => Kvp.Value != null
                                && !string.IsNullOrEmpty(Kvp.Value.Name)
                                && Kvp.Value.Name.ToLower().Contains(SearchTerm.ToLower())
                                && (strategy == SearchType.EntireInventory || (strategy == SearchType.FocusedRecursive && currentPath.Contains(Kvp.Value.ToRecord().Path)) || (strategy == SearchType.FocusedNonRecursive && currentPath == Kvp.Value.ToRecord().Path)))
                    .Select(Kvp => Kvp.Value.ToRecord())
                    .ToList();

                var Records = new List<Record>(SearchResults);
                var SubDirs = new List<RecordDirectory>(InventoryBrowser.CurrentDirectory.Subdirectories);
                //var ParentDirCache = InventoryBrowser.CurrentDirectory.ParentDirectory;
                //var Inventory = new RecordDirectory(Engine.Current.Cloud.CurrentUserID, "Inventory", Engine.Current);
                
                var SearchResultsDirs = SubDirs
                    .Where(Kvp => Kvp.Name.ToLower().Contains(SearchTerm));

                var InventoryAdd = SearchResultsDirs.ToList();
                //InventoryAdd.Add(Inventory);
                
                var NewDir = new RecordDirectory(Engine.Current, InventoryAdd.ToList(), Records);
                NewDir.EnsureFullyLoaded();

                SetPropertyValue(NewDir, "Name", "Search Results");
                SetPropertyValue(NewDir, "ParentDirectory", InventoryBrowser.CurrentDirectory);
                
                // SetPropertyValue(Inventory, "Name", "Inventory");
                // SetPropertyValue(Inventory, "ParentDirectory", ParentDirCache);

                InventoryBrowser.Open(NewDir, SlideSwapRegion.Slide.Left);
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
        private class SerializableRecord
        {
            public string RecordId { get; set; }
            public string Name { get; set; }
            public string OwnerName { get; set; }
            public string AssetURI { get; set; }
            public string ThumbnailURI { get; set; }

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
                };
            }
        }
    }
}
