﻿// Kerbal Inventory System
// Mod's author: KospY (http://forum.kerbalspaceprogram.com/index.php?/profile/33868-kospy/)
// Module authors: KospY, igor.zavoychinskiy@gmail.com
// License: Restricted

using KSPDev.ConfigUtils;
using KSPDev.LogUtils;
using KSPDev.ModelUtils;
using KSPDev.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KIS {

[KSPAddon(KSPAddon.Startup.Instantly, false)]
[PersistentFieldsDatabase("KIS/settings/KISConfig")]
sealed class KISAddonConfig : MonoBehaviour {
  [PersistentField("StackableItemOverride/partName", isCollection = true)]
  public readonly static List<string> stackableList = new List<string>();

  [PersistentField("StackableModule/moduleName", isCollection = true)]
  public readonly static List<string> stackableModules = new List<string>();

  [PersistentField("EquipAliases/alias", isCollection = true)]
  public readonly static List<string> equipAliases = new List<string>();

  [PersistentField("Global/breathableAtmoPressure")]
  public static float breathableAtmoPressure = 0.5f;

  [PersistentField("EvaInventory")]
  readonly static PersistentConfigNode evaInventory = new PersistentConfigNode();

  [PersistentField("EvaPickup")]
  readonly static PersistentConfigNode evaPickup = new PersistentConfigNode();

  const string MaleKerbalEva = "kerbalEVA";
  const string FemaleKerbalEva = "kerbalEVAfemale";
  const string MaleKerbalEvaVintage = "kerbalEVAVintage";
  const string FemaleKerbalEvaVintage = "kerbalEVAfemaleVintage";
  const string RdKerbalEva = "kerbalEVA_RD";

  /// <summary>Instantly loads the KIS global settings.</summary>
  class KISConfigLoader: LoadingSystem {
    public override bool IsReady() {
      return true;
    }

    public override void StartLoad() {
      DebugEx.Info("Loading KIS global settings...");
      ConfigAccessor.ReadFieldsInType(typeof(KISAddonConfig), instance: null);
      ConfigAccessor.ReadFieldsInType(typeof(ModuleKISInventory), instance: null);
    }
  }

  /// <summary>Adds EVA inventories to every pod.</summary>
  class KISPodInventoryLoader: LoadingSystem {
    public override bool IsReady() {
      return true;
    }

    public override void StartLoad() {
      // Kerbal parts.
      UpdateEvaPrefab(MaleKerbalEva);
      UpdateEvaPrefab(MaleKerbalEvaVintage);
      UpdateEvaPrefab(FemaleKerbalEva);
      UpdateEvaPrefab(FemaleKerbalEvaVintage);

      // Set inventory module for every pod with crew capacity.
      DebugEx.Info("Loading pod inventories...");
      for (var i = 0; i < PartLoader.LoadedPartsList.Count; i++) {
        var avPart = PartLoader.LoadedPartsList[i];
        if (!(avPart.name == MaleKerbalEva || avPart.name == FemaleKerbalEva
              || avPart.name == MaleKerbalEvaVintage || avPart.name == FemaleKerbalEvaVintage
              || avPart.name == RdKerbalEva
              || !avPart.partPrefab || avPart.partPrefab.CrewCapacity < 1)) {
          DebugEx.Fine("Found part with crew: {0}, CrewCapacity={1}",
                       avPart.name, avPart.partPrefab.CrewCapacity);
          AddPodInventories(avPart.partPrefab, avPart.partPrefab.CrewCapacity);
        }
      }
    }
  }

  public void Awake() {
    List<LoadingSystem> list = LoadingScreen.Instance.loaders;
    if (list != null) {
      for (var i = 0; i < list.Count; i++) {
        if (list[i] is PartLoader) {
          var go = new GameObject();

          var invLoader = go.AddComponent<KISPodInventoryLoader>();
          // Cause the pod inventory loader to run AFTER the part loader.
          list.Insert(i + 1, invLoader);

          var cfgLoader = go.AddComponent<KISConfigLoader>();
          // Cause the config loader to run BEFORE the part loader this ensures
          // that the KIS configs are loaded after Module Manager has run but
          // before any parts are loaded so KIS aware part modules can add
          // pod inventories as necessary.
          list.Insert(i, cfgLoader);
          break;
        }
      }
    }
  }

  public static void AddPodInventories(Part part, int crewCapacity) {
    var checkInventories = part.Modules.OfType<ModuleKISInventory>()
        .Where(m => m.invType == ModuleKISInventory.InventoryType.Pod);
    if (checkInventories.Any()) {
      DebugEx.Error("Part {0} has pod inventories in config. Cannot make a proper setup!", part);
    }

    // Assign the seats.
    var podInventories = part.Modules.OfType<ModuleKISInventory>()
        .Where(m => m.invType == ModuleKISInventory.InventoryType.Pod)
        .ToArray();
    for (var i = 0; i < crewCapacity; i++) {
      DebugEx.Fine("{0}: Add pod inventory at seat: {0}", i);
      var moduleNode = new ConfigNode("MODULE", "Dynamically created by KIS. Not persistant!");
      evaInventory.CopyTo(moduleNode);
      moduleNode.SetValue("name", typeof(ModuleKISInventory).Name, createIfNotFound: true);
      moduleNode.SetValue(
          "invType", ModuleKISInventory.InventoryType.Pod.ToString(), createIfNotFound: true);
      moduleNode.SetValue("podSeat", i, createIfNotFound: true);
      part.partInfo.partConfig.AddNode(moduleNode);
      part.AddModule(moduleNode, forceAwake: true);
    }
  }

  /// <summary>Load config of EVA modules for the requested part name.</summary>
  static void UpdateEvaPrefab(string partName) {
    var partInfo = PartLoader.getPartInfoByName(partName);
    if (partInfo != null ){
      var prefab = partInfo.partPrefab;
      if (LoadModuleConfig(prefab, typeof(ModuleKISInventory), evaInventory)) {
        prefab.GetComponent<ModuleKISInventory>().invType = ModuleKISInventory.InventoryType.Eva;
      }
      LoadModuleConfig(prefab, typeof(ModuleKISPickup), evaPickup);
    } else {
      DebugEx.Info("Skipping EVA model: {0}. Expansion is not installed.", partName);
    }
  }

  /// <summary>Loads config values for the part's module fro the provided config node.</summary>
  /// <returns><c>true</c> if loaded successfully.</returns>
  static bool LoadModuleConfig(Part p, Type moduleType, ConfigNode node) {
    var module = p.GetComponent(moduleType);
    if (module == null) {
      DebugEx.Warning(
          "Config node for module {0} in part {1} is NULL. Nothing to load!", moduleType, p);
      return false;
    }
    if (node == null) {
      DebugEx.Warning("Cannot find module {0} on part {1}. Config not loaded!", moduleType, p);
      return false;
    }
    var baseFields = new BaseFieldList(module);
    baseFields.Load(node);
    DebugEx.Info("Loaded config for {0} on part {1}", moduleType, p);
    return true;
  }

  /// <summary>Finds a bone to attach the equippable item to.</summary>
  /// <param name="root">The transform to start searching from.</param>
  /// <param name="bonePath">The hierarchy search pattern or a KIS alias.</param>
  /// <returns>The transform or <c>null</c> if nothing found.</returns>
  /// <seealso cref="KISAddonConfig.equipAliases"/>
  public static Transform FindEquipBone(Transform root, string bonePath) {
    Transform res;
    if (bonePath.StartsWith("alias", StringComparison.Ordinal)) {
      res = KISAddonConfig.equipAliases
          .Select(a => a.Split(new[] {','}, 2))
          .Where(pair => pair.Length == 2 && pair[0] == bonePath)
          .Select(pair => Hierarchy.FindTransformByPath(root, pair[1]))
          .FirstOrDefault(t => t != null);
      DebugEx.Fine("For alias '{0}' found transform: {1}", bonePath, res);
    } else {
      res = Hierarchy.FindTransformByPath(root, bonePath);
      DebugEx.Fine("For bone path '{0}' found transform: {1}", bonePath, res);
    }
    if (res == null) {
      DebugEx.Error("Cannot find object for EVA item: {0}", bonePath);
    }
    return res;
  }
}

}  // namespace
