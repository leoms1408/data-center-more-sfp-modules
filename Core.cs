using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[assembly: MelonInfo(typeof(MoreSFPModules.Core), "More SFP Modules", "1.0.0", "leoms1408")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace MoreSFPModules
{
    public class Core : MelonMod
    {
        // Sprite from the vanilla QSFP+ shop entry — reused as icon for all custom modules.
        internal static Sprite BaseQsfpSprite;

        // sfpType of the vanilla QSFP+ module (form-factor; determines port compatibility).
        // Our custom modules keep this value so they fit the same switch ports.
        internal static int BaseQsfpSfpType = -1;

        // prefabID of the vanilla QSFP+ module — used as clone source in BuildModulePrefab/BuildBoxPrefab.
        internal static int BaseQsfpPrefabID = -1;

        // Inactive holder for prefab templates — parenting templates here makes their
        // activeInHierarchy = false, so the game's UsableObject tracker ignores them.
        // Object.Instantiate still produces active clones from inactive-hierarchy objects.
        internal static GameObject TemplateHolder { get; private set; }

        // Tracks pending 32x bulk box orders: key = custom prefabID (100+), value = count.
        // Populated by PatchButtonCheckOut, consumed by DeliveryScannerRoutine.
        internal static readonly Dictionary<int, int> PendingBulkOrders = new();

        // -----------------------------------------------------------------------
        // Scans vanilla sfpPrefabs to find the highest-speed module (QSFP+ 40G),
        // stores it as the clone source, then extends the sfpPrefabs array with
        // one slot per custom module starting at MOD_ID_BASE (100).
        //
        // Starting at 100 instead of vanillaCount prevents prefabID collisions if
        // the game later adds new vanilla SFP types at indices 4, 5, 6 …
        //
        // Called from PatchMainGameManagerAwake — the earliest point where
        // sfpPrefabs is populated, guaranteed to run before OnLoad() restores saves.
        // -----------------------------------------------------------------------
        internal static void SetupRegistry(MainGameManager mgm)
        {
            ModuleRegistry.Clear();

            var sfpPrefabs = mgm.sfpPrefabs;
            if (sfpPrefabs == null || sfpPrefabs.Length == 0)
            {
                MelonLogger.Warning("[More SFP] sfpPrefabs is empty — skipping setup.");
                return;
            }

            MelonLogger.Msg($"[More SFP] Vanilla SFP prefabs: {sfpPrefabs.Length}");

            float highestSpeed = -1f;

            for (int i = 0; i < sfpPrefabs.Length; i++)
            {
                var go        = sfpPrefabs[i];
                if (go == null) continue;
                var sfpMod    = go.GetComponent<SFPModule>();
                var usableObj = go.GetComponent<UsableObject>();
                float speed   = sfpMod    != null ? sfpMod.speed       : -1f;
                int   sfpType = sfpMod    != null ? sfpMod.sfpType     : -1;
                int   pid     = usableObj != null ? usableObj.prefabID : -1;

                if (speed > highestSpeed)
                {
                    highestSpeed     = speed;
                    BaseQsfpSfpType  = sfpType;
                    BaseQsfpPrefabID = pid;
                }
            }

            if (BaseQsfpPrefabID < 0)
            {
                MelonLogger.Error("[More SFP] Could not identify base QSFP+ prefab.");
                return;
            }

            MelonLogger.Msg($"[More SFP] Base QSFP+: prefabID={BaseQsfpPrefabID}, " +
                            $"sfpType={BaseQsfpSfpType}, {highestSpeed * 5f} Gbps");

            // Create/recreate the inactive holder that hides templates from the world system.
            if (TemplateHolder != null)
                Object.Destroy(TemplateHolder);
            TemplateHolder = new GameObject("MoreSFP_TemplateHolder");
            TemplateHolder.SetActive(false);
            Object.DontDestroyOnLoad(TemplateHolder);

            const int MOD_ID_BASE = 100;
            int vanillaCount = sfpPrefabs.Length;

            if (vanillaCount > MOD_ID_BASE)
            {
                MelonLogger.Error($"[More SFP] vanilla sfpPrefabs.Length={vanillaCount} exceeds " +
                                  $"MOD_ID_BASE={MOD_ID_BASE}! prefabID collision risk — mod disabled.");
                return;
            }

            // Vanilla entries at their original indices, null padding up to MOD_ID_BASE,
            // then one slot per custom module.
            var extended = new GameObject[MOD_ID_BASE + ModuleList.All.Length];
            for (int i = 0; i < vanillaCount; i++)
                extended[i] = sfpPrefabs[i];

            int nextID = MOD_ID_BASE;

            foreach (var def in ModuleList.All)
            {
                int id = nextID++;
                var entry = new ModuleRegistry.Entry(
                    speedInternal: def.InternalSpeed,
                    moduleSfpType: BaseQsfpSfpType,
                    boxSfpType:    id,
                    basePrefabID:  BaseQsfpPrefabID
                );
                ModuleRegistry.Register(id, entry);

                // Store a template at sfpPrefabs[id] so LoadSFPsFromSave (which does
                // direct array access) can find the prefab during save loading.
                var template = BuildModulePrefab(mgm, id, entry, TemplateHolder.transform);
                if (template != null)
                    template.name = $"SFPModule_template_{id}";
                extended[id] = template;

                MelonLogger.Msg($"[More SFP] Registered '{def.DisplayName}': " +
                                $"prefabID={id}, {def.SpeedGbps} Gbps");
            }

            mgm.sfpPrefabs = extended;
            MelonLogger.Msg($"[More SFP] sfpPrefabs extended: {vanillaCount} → {extended.Length}");
        }

        // -----------------------------------------------------------------------
        // Clones the vanilla QSFP+ module prefab and applies our custom speed and
        // prefabID. Called on-demand from patches rather than caching the result,
        // because Il2Cpp's GC can silently invalidate native pointers on cached
        // GameObjects stored in C# data structures.
        //
        // parent: when non-null the clone is instantiated directly under that transform,
        // so it is never active in hierarchy and the world tracker cannot pick it up.
        // Pass TemplateHolder.transform for cached templates, null for live clones.
        // -----------------------------------------------------------------------
        internal static GameObject BuildModulePrefab(MainGameManager mgm, int prefabID,
                                                     ModuleRegistry.Entry entry,
                                                     Transform parent = null)
        {
            var basePrefab = mgm.sfpPrefabs[entry.BasePrefabID];
            if (basePrefab == null)
            {
                MelonLogger.Error($"[More SFP] Base prefab [{entry.BasePrefabID}] is null.");
                return null;
            }

            var clone = parent != null
                ? Object.Instantiate(basePrefab, parent, false)
                : Object.Instantiate(basePrefab);
            clone.name = $"SFPModule_custom_{prefabID}";

            var sfpMod = clone.GetComponent<SFPModule>();
            if (sfpMod != null)
                sfpMod.speed = entry.SpeedInternal;

            var usableObj = clone.GetComponent<UsableObject>();
            if (usableObj != null)
                usableObj.prefabID = prefabID;

            return clone;
        }

        // -----------------------------------------------------------------------
        // Clones the vanilla QSFP+ box prefab and applies our custom sfpBoxType
        // and prefabID. Also updates all child SFPModule components inside the box
        // so the player receives the correct module when unboxing.
        // -----------------------------------------------------------------------
        internal static GameObject BuildBoxPrefab(MainGameManager mgm, int prefabID,
                                                  ModuleRegistry.Entry entry)
        {
            var boxPrefabs = mgm.sfpsBoxedPrefab;
            if (boxPrefabs == null) return null;

            GameObject baseBox = entry.BasePrefabID < boxPrefabs.Length
                ? boxPrefabs[entry.BasePrefabID]
                : null;

            // Fall back to the first non-null box if the expected index is missing.
            if (baseBox == null)
                for (int i = 0; i < boxPrefabs.Length; i++)
                    if (boxPrefabs[i] != null) { baseBox = boxPrefabs[i]; break; }

            if (baseBox == null)
            {
                MelonLogger.Warning("[More SFP] No base box prefab found.");
                return null;
            }

            var clone = Object.Instantiate(baseBox);
            clone.name = $"SFPBox_custom_{prefabID}";

            var sfpBox = clone.GetComponent<SFPBox>();
            if (sfpBox != null)
                sfpBox.sfpBoxType = prefabID;

            var usableObj = clone.GetComponent<UsableObject>();
            if (usableObj != null)
                usableObj.prefabID = prefabID;

            // The box prefab contains the SFPModules as child GameObjects.
            // Only update speed — do NOT set prefabID on children, as that would
            // register them as independent world items and cause them to spawn loose.
            // PatchCableLinkInsertSFP corrects the prefabID at insertion time instead.
            foreach (var childModule in clone.GetComponentsInChildren<SFPModule>())
                childModule.speed = entry.SpeedInternal;

            return clone;
        }

        // -----------------------------------------------------------------------
        // Triggered on every scene load. Starts the shop injection coroutine for
        // any scene other than the main menu (buildIndex 0).
        // -----------------------------------------------------------------------
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (buildIndex != 0)
                MelonCoroutines.Start(AddShopItems());
        }

        // -----------------------------------------------------------------------
        // Waits for the shop to finish initializing, then injects a shop button
        // for each registered custom module into the "HL Mods" section.
        // The 1.5 s delay is necessary because the shop UI is built after scene load.
        // -----------------------------------------------------------------------
        private IEnumerator AddShopItems()
        {
            yield return new WaitForSeconds(1.5f);

            var mgm = MainGameManager.instance;
            if (mgm == null) { LoggerInstance.Warning("MGM null — shop skipped."); yield break; }

            var computerShop = mgm.computerShop;
            if (computerShop == null) { LoggerInstance.Warning("Shop null — skipped."); yield break; }

            // Find the vanilla QSFP+ box shop entry to use as a UI clone template.
            // The shop sells SFPBox items (type 9), not bare SFPModule items.
            ShopItem sourceItem = null;
            if (computerShop.shopItems != null)
            {
                foreach (var si in computerShop.shopItems)
                {
                    if (si == null || si.shopItemSO == null) continue;

                    if ((int)si.shopItemSO.itemType == 9 && si.shopItemSO.itemID == BaseQsfpPrefabID)
                    {
                        sourceItem     = si;
                        BaseQsfpSprite = si.shopItemSO.sprite;
                    }
                }
            }

            if (sourceItem == null)
            {
                LoggerInstance.Warning("[More SFP] No QSFP+ box shop item found — shop buttons skipped.");
                yield break;
            }

            var shopParent = computerShop.shopItemParent;
            if (shopParent == null) { LoggerInstance.Warning("shopItemParent null."); yield break; }

            // Target the "HL Mods" section inside VL-ShopItems so our items appear
            // in the correct category rather than being appended at the end.
            var modsTransform = shopParent.transform.Find("HL Mods");
            if (modsTransform != null)
                shopParent = modsTransform.gameObject;
            else
                LoggerInstance.Warning("[More SFP] 'HL Mods' not found — falling back to shopItemParent.");

            float itemHeight = 0f;
            var sourceRt = sourceItem.GetComponent<UnityEngine.RectTransform>();
            if (sourceRt != null)
                itemHeight = sourceRt.rect.height;

            int addedCount = 0;
            int basePrice  = sourceItem.shopItemSO.price;

            for (int i = 0; i < ModuleList.All.Length; i++)
            {
                var def      = ModuleList.All[i];
                int prefabID = 100 + i;
                if (!ModuleRegistry.TryGet(prefabID, out _)) continue;

                // 5x shop button (standard)
                string label5 = $"5x {def.DisplayName}";
                int price5    = (int)(basePrice * def.PriceMultiplier);
                var added5    = AddShopButton(sourceItem, shopParent, prefabID,
                                              label5, price5, def.XpToUnlock, def.ShopGuid);
                if (added5 != null) addedCount++;

                // 32x shop button — uses the SAME prefabID as 5x so the game
                // delivers a normal box; PatchButtonCheckOut upgrades it post-delivery.
                string label32 = $"32x {def.DisplayName}";
                int price32    = (int)(basePrice * def.PriceMultiplier * 32f / 5f);
                var added32    = AddShopButton(sourceItem, shopParent, prefabID,
                                               label32, price32, def.XpToUnlock,
                                               def.ShopGuid + "_32x");
                if (added32 != null) addedCount++;
            }

            // The HL Mods container has a fixed height — extend it so the ScrollRect
            // can scroll far enough to reveal our newly added items.
            var containerRt = shopParent.GetComponent<UnityEngine.RectTransform>();
            if (containerRt != null && itemHeight > 0f && addedCount > 0)
            {
                var sd = containerRt.sizeDelta;
                sd.y += itemHeight * addedCount;
                containerRt.sizeDelta = sd;
            }

            UnityEngine.Canvas.ForceUpdateCanvases();
        }

        // -----------------------------------------------------------------------
        // Clones an existing shop item GameObject, assigns a new ShopItemSO with
        // the custom module's name/price/ID, and adds it to the given parent.
        // Returns the created GameObject, or null if the ShopItem component is missing.
        // -----------------------------------------------------------------------
        private static GameObject AddShopButton(ShopItem source, GameObject parent, int prefabID,
                                               string label, int price, int xpToUnlock, string guid)
        {
            var newSO = ScriptableObject.CreateInstance<ShopItemSO>();
            newSO.itemName   = label;
            newSO.price      = price;
            newSO.xpToUnlock = xpToUnlock;
            newSO.itemType   = source.shopItemSO.itemType; // SFPBox (9)
            newSO.itemID     = prefabID;
            newSO.eol        = source.shopItemSO.eol;
            newSO.sprite     = BaseQsfpSprite;

            var cloned = Object.Instantiate(source.gameObject, parent.transform, false);
            cloned.name = $"ShopItem_{label.Replace(" ", "_")}";
            cloned.transform.localPosition = Vector3.zero;
            cloned.transform.localScale    = Vector3.one;

            var shopItem = cloned.GetComponent<ShopItem>();
            if (shopItem == null)
            {
                MelonLogger.Error($"[More SFP] ShopItem component missing for '{label}'.");
                Object.Destroy(cloned);
                return null;
            }

            shopItem.shopItemSO = newSO;
            shopItem.guid       = guid;
            cloned.SetActive(true);

            MelonLogger.Msg($"[More SFP] Shop button added: '{newSO.itemName}' (prefabID={prefabID}, price={newSO.price})");
            return cloned;
        }

        // -----------------------------------------------------------------------
        // Expands a live SFPBox from its vanilla capacity (5) to newCapacity (32)
        // by cloning slot positions and using proper Il2Cpp array types.
        // Called by DeliveryScannerRoutine after the box has been delivered.
        // -----------------------------------------------------------------------
        internal static void UpgradeToBulkBox(SFPBox box, int newCapacity)
        {
            var oldPositions = box.sfpPositions;
            if (oldPositions == null || oldPositions.Length == 0) return;

            int oldCap = oldPositions.Length;
            if (oldCap >= newCapacity) return;

            var newPositions = new Il2CppReferenceArray<Transform>(newCapacity);
            var newUsed      = new Il2CppStructArray<int>(newCapacity);

            int fullSlotValue = box.usedPositions != null && box.usedPositions.Length > 0
                ? box.usedPositions[oldCap - 1] : 1;

            // Copy existing slots.
            for (int i = 0; i < oldCap; i++)
            {
                newPositions[i] = oldPositions[i];
                newUsed[i] = box.usedPositions != null && i < box.usedPositions.Length
                    ? box.usedPositions[i] : 0;
            }

            // Clone new slots from the original positions (round-robin).
            for (int i = oldCap; i < newCapacity; i++)
            {
                int baseIdx = i % oldCap;
                Transform baseSlot = oldPositions[baseIdx];

                var newSlotObj = Object.Instantiate(baseSlot.gameObject, baseSlot.parent);
                newSlotObj.name = $"SFPPositionInBox_{i}";
                newSlotObj.transform.localPosition = baseSlot.localPosition;

                newPositions[i] = newSlotObj.transform;
                newUsed[i] = fullSlotValue;
            }

            box.sfpPositions  = newPositions;
            box.usedPositions = newUsed;

            MelonLogger.Msg($"[More SFP] Upgraded box '{box.gameObject.name}' from {oldCap} → {newCapacity} slots.");
        }

        // -----------------------------------------------------------------------
        // Scans the world for newly delivered custom boxes that should be 32x.
        // Runs in a coroutine, polling every 1.5 s until all pending orders are
        // fulfilled. Snapshots existing boxes before scanning to avoid upgrading
        // boxes the player already had in the world.
        // -----------------------------------------------------------------------
        internal static IEnumerator DeliveryScannerRoutine()
        {
            MelonLogger.Msg("[More SFP] DeliveryScannerRoutine started.");
            foreach (var kv in PendingBulkOrders)
                MelonLogger.Msg($"[More SFP]   Pending: prefabID={kv.Key}, count={kv.Value}");

            // Track boxes we've already upgraded so we don't touch them again.
            var upgradedIds = new HashSet<int>();

            bool hasPending = true;
            int scanCount = 0;
            while (hasPending)
            {
                yield return new WaitForSeconds(1.5f);
                scanCount++;

                var allBoxes = Object.FindObjectsOfType<SFPBox>();
                if (scanCount <= 5 || scanCount % 10 == 0)
                    MelonLogger.Msg($"[More SFP] Scan #{scanCount}: {allBoxes.Count} boxes in world.");

                foreach (var box in allBoxes)
                {
                    if (box == null) continue;
                    if (upgradedIds.Contains(box.GetInstanceID())) continue;
                    // Already has more than vanilla 5 slots — skip.
                    if (box.sfpPositions != null && box.sfpPositions.Length > 5) continue;

                    int boxType = box.sfpBoxType;
                    if (PendingBulkOrders.ContainsKey(boxType) && PendingBulkOrders[boxType] > 0)
                    {
                        MelonLogger.Msg($"[More SFP] Upgrading box '{box.gameObject.name}' " +
                                        $"(sfpBoxType={boxType}) to 32 slots!");
                        UpgradeToBulkBox(box, 32);
                        PendingBulkOrders[boxType]--;
                        upgradedIds.Add(box.GetInstanceID());
                    }
                }

                hasPending = false;
                foreach (var count in PendingBulkOrders.Values)
                    if (count > 0) { hasPending = true; break; }

                // Safety: stop after 5 minutes.
                if (scanCount >= 200)
                {
                    MelonLogger.Warning("[More SFP] DeliveryScannerRoutine timed out after 5 min.");
                    break;
                }
            }

            MelonLogger.Msg("[More SFP] DeliveryScannerRoutine finished.");
            PendingBulkOrders.Clear();
        }
    }
}
