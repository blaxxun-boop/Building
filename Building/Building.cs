using System.Reflection;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using SkillManager;
using UnityEngine;

namespace Building;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Building : BaseUnityPlugin
{
	private const string ModName = "Building";
	private const string ModVersion = "1.1.0";
	private const string ModGUID = "org.bepinex.plugins.building";

	public void Awake()
	{
		Skill building = new("Building", "building-icon.png");
		building.Description.English("Increases the health of pieces built by you and you can build higher.");
		building.Name.German("Bauen");
		building.Description.German("Erhöht die Lebenspunkte von Bauten, die von dir errichtet wurden und du kannst höher bauen.");
		building.Configurable = true;
			
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
			__instance.m_health *= 1 + __instance.GetComponent<ZNetView>().GetZDO().GetFloat("BuildingSkill Level") * 2f;
			
			forFree = Random.Range(0.5f, 5.5f) <= Player.m_localPlayer.GetSkillFactor("Building");
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
			__instance.m_health *= 1 + __instance.GetComponent<ZNetView>().GetZDO()?.GetFloat("BuildingSkill Level") * 2f ?? 1;
		}
	}

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.GetMaterialProperties))]
	private class BuildHigher
	{
		[UsedImplicitly]
		private static void Postfix(WearNTear __instance, ref float maxSupport, ref float minSupport, ref float horizontalLoss, ref float verticalLoss)
		{
			float skillLevel = __instance.m_nview.GetZDO()?.GetFloat("BuildingSkill Level") ?? 0;
			maxSupport *= 1 + skillLevel * 0.5f;
			horizontalLoss /= 1 + skillLevel * 0.33f;
			verticalLoss /= 1 + skillLevel * 0.33f;
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
		private static bool Prefix() => !AddZDO.forFree;
	}
	
	[HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
	private class RecoverResources
	{
		private static bool Prefix(Piece __instance) => !__instance.GetComponent<ZNetView>().GetZDO().GetBool("BuildingSkill FreeBuild");
	}
}
