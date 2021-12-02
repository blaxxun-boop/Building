using System.Reflection;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using SkillManager;

namespace Building
{
	[BepInPlugin(ModGUID, ModName, ModVersion)]
	public class Building : BaseUnityPlugin
	{
		private const string ModName = "Building";
		private const string ModVersion = "1.0";
		private const string ModGUID = "org.bepinex.plugins.building";

		public void Awake()
		{
			Skill building = new("Building", "building-icon.png");
			building.Description.English("Increases the health of pieces built by you.");
			building.Name.German("Bauen");
			building.Description.German("Erhöht die Lebenspunkte von Bauten, die von dir errichtet wurden.");
			building.Configurable = true;
			
			Assembly assembly = Assembly.GetExecutingAssembly();
			Harmony harmony = new(ModGUID);
			harmony.PatchAll(assembly);
		}
		
		[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
		private class AddZDO
		{
			[UsedImplicitly]
			private static void Postfix(WearNTear __instance)
			{
				__instance.GetComponent<ZNetView>().GetZDO().Set("BuildingSkill Level", Player.m_localPlayer.GetSkillFactor("Building"));
				__instance.m_health *= 1 + __instance.GetComponent<ZNetView>().GetZDO().GetFloat("BuildingSkill Level") * 2f;
				
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
	}
}
