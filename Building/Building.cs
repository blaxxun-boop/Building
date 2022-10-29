using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using SkillManager;
using Random = UnityEngine.Random;

namespace Building;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Building : BaseUnityPlugin
{
	private const string ModName = "Building";
	private const string ModVersion = "1.2.2";
	private const string ModGUID = "org.bepinex.plugins.building";

	private static readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<float> maximumSupportFactor = null!;
	private static ConfigEntry<float> supportLossFactor = null!;
	private static ConfigEntry<float> healthFactor = null!;
	private static ConfigEntry<int> freeBuildLevelRequirement = null!;
	private static ConfigEntry<float> experienceGainedFactor = null!;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public bool? ShowRangeAsPercent;
	}

	private static Skill building = null!;

	public void Awake()
	{
		building = new Skill("Building", "building-icon.png");
		building.Description.English("Increases the health of pieces built by you and you can build higher.");
		building.Name.German("Bauen");
		building.Description.German("Erhöht die Lebenspunkte von Bauten, die von dir errichtet wurden und du kannst höher bauen.");
		building.Configurable = false;

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		maximumSupportFactor = config("2 - Building", "Maximum Support Factor", 1.5f, new ConfigDescription("Maximum support factor for building pieces at skill level 100.", new AcceptableValueRange<float>(1f, 5f)));
		supportLossFactor = config("2 - Building", "Support Loss Factor", 0.75f, new ConfigDescription("Support loss factor for vertical and horizontal building at skill level 100.", new AcceptableValueRange<float>(0.01f, 1f)));
		healthFactor = config("2 - Building", "Health Factor", 3f, new ConfigDescription("Health factor for building pieces at skill level 100.", new AcceptableValueRange<float>(1f, 10f)));
		freeBuildLevelRequirement = config("2 - Building", "Free Build Level Requirement", 50, new ConfigDescription("Minimum required skill level to be able to receive free building pieces. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		experienceGainedFactor = config("3 - Other", "Skill Experience Gain Factor", 1f, new ConfigDescription("Factor for experience gained for the building skill.", new AcceptableValueRange<float>(0.01f, 5f)));
		experienceGainedFactor.SettingChanged += (_, _) => building.SkillGainFactor = experienceGainedFactor.Value;
		building.SkillGainFactor = experienceGainedFactor.Value;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
	public class AddZDO
	{
		public static bool forFree = false;

		[UsedImplicitly]
		private static void Postfix(WearNTear __instance)
		{
			__instance.GetComponent<ZNetView>().GetZDO().Set("BuildingSkill Level", Player.m_localPlayer.GetSkillFactor("Building"));
			__instance.m_health *= 1 + __instance.GetComponent<ZNetView>().GetZDO().GetFloat("BuildingSkill Level") * (healthFactor.Value - 1f);

			if (freeBuildLevelRequirement.Value > 0)
			{
				forFree = Random.Range(freeBuildLevelRequirement.Value / 100f, 5.5f) <= Player.m_localPlayer.GetSkillFactor("Building");
			}
			__instance.GetComponent<ZNetView>().GetZDO().Set("BuildingSkill FreeBuild", forFree);

			Player.m_localPlayer.RaiseSkill("Building");
		}
	}

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
	private class IncreaseHealth
	{
		[UsedImplicitly]
		private static void Prefix(WearNTear __instance)
		{
			__instance.m_health *= 1 + __instance.GetComponent<ZNetView>().GetZDO()?.GetFloat("BuildingSkill Level") * (healthFactor.Value - 1f) ?? 1;
		}
	}

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.GetMaterialProperties))]
	private class BuildHigher
	{
		[UsedImplicitly]
		private static void Postfix(WearNTear __instance, ref float maxSupport, ref float minSupport, ref float horizontalLoss, ref float verticalLoss)
		{
			float skillLevel = __instance.m_nview?.GetZDO()?.GetFloat("BuildingSkill Level") ?? Player.m_localPlayer?.GetSkillFactor("Building") ?? 0;
			maxSupport *= 1 + skillLevel * (maximumSupportFactor.Value - 1);
			horizontalLoss *= 1 - skillLevel * (1 - supportLossFactor.Value);
			verticalLoss *= 1 - skillLevel * (1 - supportLossFactor.Value);
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
	private class ResetFlag
	{
		private static void Finalizer() => AddZDO.forFree = false;
	}

	[HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
	private class BuildingIsFree
	{
		private class SkipConsumeResourcesException : Exception
		{
		}

		[HarmonyPriority(Priority.First)]
		private static void Prefix()
		{
			if (AddZDO.forFree)
			{
				throw new SkipConsumeResourcesException();
			}
		}

		private static Exception? Finalizer(Exception __exception) => __exception is SkipConsumeResourcesException ? null : __exception;
	}

	[HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
	private class RecoverResources
	{
		private static bool Prefix(Piece __instance) => !__instance.GetComponent<ZNetView>().GetZDO().GetBool("BuildingSkill FreeBuild");
	}
}
