using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using SkillManager;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ranching;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class Ranching : BaseUnityPlugin
{
	private const string ModName = "Ranching";
	private const string ModVersion = "1.1.3";
	private const string ModGUID = "org.bepinex.plugins.ranching";

	private static readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<float> ranchingItemFactor = null!;
	private static ConfigEntry<float> ranchingTamingFactor = null!;
	private static ConfigEntry<int> ranchingFoodLevel = null!;
	private static ConfigEntry<int> ranchingCalmLevel = null!;
	private static ConfigEntry<int> ranchingPregnancyLevel = null!;
	private static ConfigEntry<float> experienceGainedFactor = null!;
	private static ConfigEntry<int> experienceLoss = null!;

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
		Off = 0,
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public bool? ShowRangeAsPercent;
	}

	private static Skill ranching = null!;

	public void Awake()
	{
		ranching = new Skill("Ranching", "ranching.png");
		ranching.Description.English("Reduces the time required to tame animals and increases item yield of tamed animals.");
		ranching.Name.German("Viehhaltung");
		ranching.Description.German("Reduziert die Zeit, die benötigt wird, um ein Tier zu zähmen und erhöht die Ausbeute von gezähmten Tieren.");
  		ranching.Name.Chinese("畜牧");
		ranching.Description.Chinese("减少驯服动物所需的时间，增加被驯服动物的产量");
		ranching.Configurable = false;

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		ranchingItemFactor = config("2 - Ranching", "Item Drop Factor", 2f, new ConfigDescription("Item drop factor for tamed creatures at skill level 100.", new AcceptableValueRange<float>(1f, 10f)));
		ranchingTamingFactor = config("2 - Ranching", "Taming Factor", 2f, new ConfigDescription("Speed at which creatures get tame at skill level 100.", new AcceptableValueRange<float>(1f, 10f)));
		ranchingFoodLevel = config("2 - Ranching", "Food Level Requirement", 10, new ConfigDescription("Minimum required skill level to see when tamed creatures will become hungry again. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		ranchingCalmLevel = config("2 - Ranching", "Calming Level Requirement", 20, new ConfigDescription("Minimum required skill level to calm nearby taming creatures. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		ranchingPregnancyLevel = config("2 - Ranching", "Pregnancy Level Requirement", 40, new ConfigDescription("Minimum required skill level to get pregnancy related information of tame creatures. 0 is disabled.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
		experienceGainedFactor = config("3 - Other", "Skill Experience Gain Factor", 1f, new ConfigDescription("Factor for experience gained for the ranching skill.", new AcceptableValueRange<float>(0.01f, 5f)));
		experienceGainedFactor.SettingChanged += (_, _) => ranching.SkillGainFactor = experienceGainedFactor.Value;
		ranching.SkillGainFactor = experienceGainedFactor.Value;
		experienceLoss = config("3 - Other", "Skill Experience Loss", 0, new ConfigDescription("How much experience to lose in the ranching skill on death.", new AcceptableValueRange<int>(0, 100)));
		experienceLoss.SettingChanged += (_, _) => ranching.SkillLoss = experienceLoss.Value;
		ranching.SkillLoss = experienceLoss.Value;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	public class PlayerAwake
	{
		private static void Postfix(Player __instance)
		{
			__instance.m_nview.Register("Ranching IncreaseSkill", (long _, int factor) => __instance.RaiseSkill("Ranching", factor));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Update))]
	public class PlayerUpdate
	{
		private static void Postfix(Player __instance)
		{
			if (__instance == Player.m_localPlayer)
			{
				__instance.m_nview.GetZDO().Set("Ranching Skill", __instance.GetSkillFactor(Skill.fromName("Ranching")));
			}
		}
	}

	[HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
	public class TamedCreatureDied
	{
		private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
		{
			if (__instance.m_character.IsTamed())
			{
				if (Player.GetClosestPlayer(__instance.transform.position, 50f) is { } closestPlayer)
				{
					float factor = closestPlayer.m_nview.GetZDO().GetFloat("Ranching Skill") * (ranchingItemFactor.Value - 1);
					int increase = (Random.Range(0f, 1f) < factor % 1 ? 1 : 0) + (int)factor;
					for (int i = 0; i < __result.Count; ++i)
					{
						__result[i] = new KeyValuePair<GameObject, int>(__result[i].Key, __result[i].Value * (1 + increase));
					}

					closestPlayer.m_nview.InvokeRPC("Ranching IncreaseSkill", 50);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Tameable), nameof(Tameable.DecreaseRemainingTime))]
	public class TameFaster
	{
		private static void Prefix(Tameable __instance, ref float time)
		{
			if (Player.GetClosestPlayer(__instance.transform.position, 10f) is { } closestPlayer)
			{
				time *= 1 + closestPlayer.m_nview.GetZDO().GetFloat("Ranching Skill") * (ranchingTamingFactor.Value - 1);
				if (Random.Range(0, 10) == 0)
				{
					closestPlayer.m_nview.InvokeRPC("Ranching IncreaseSkill", 7);
				}
			}
		}
	}

	[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
	public static class SetTamingFlag
	{
		public static bool gettingTamed = false;

		private static void Prefix(MonsterAI __instance)
		{
			if (__instance.m_character?.GetComponent<Tameable>()?.GetTameness() > 0)
			{
				gettingTamed = true;
			}
		}

		private static void Finalizer() => gettingTamed = false;
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetStealthFactor))]
	public static class DoNotAlert
	{
		private static bool Prefix(Player __instance, ref float __result)
		{
			if (SetTamingFlag.gettingTamed && __instance.m_nview.GetZDO().GetFloat("Ranching Skill") >= ranchingCalmLevel.Value / 100f && ranchingCalmLevel.Value > 0)
			{
				__result = 0f;
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Tameable), nameof(Tameable.GetStatusString))]
	private static class DisplayInformation
	{
		private static void Postfix(Tameable __instance, ref string __result)
		{
			if (__instance.m_character.IsTamed())
			{
				if (__instance.GetComponent<Procreation>() is { } procreation && Player.m_localPlayer.GetSkillFactor("Ranching") >= ranchingPregnancyLevel.Value / 100f && ranchingPregnancyLevel.Value > 0)
				{
					if (procreation.IsPregnant())
					{
						long ToTicks(double seconds) => (long)(seconds * 10_000_000);
						long ticks = __instance.m_nview.GetZDO().GetLong("pregnant") + ToTicks(procreation.m_pregnancyDuration + 1);
						long effectiveTicks = ticks + (ToTicks(ProcreationTimeOffset(procreation)) - ticks) % ToTicks(procreation.m_updateInterval);
						if (effectiveTicks < ticks)
						{
							effectiveTicks += ToTicks(procreation.m_updateInterval);
						}
						TimeSpan duration = new DateTime(effectiveTicks) - ZNet.instance.GetTime();

						__result += $@", Pregnancy: {duration:hh\:mm\:ss}";
					}
					else
					{
						int progress = Mathf.RoundToInt((float)__instance.m_nview.GetZDO().GetInt("lovePoints") / procreation.m_requiredLovePoints * 100);

						__result += $", Pre-Pregnancy: {progress}%";
					}
				}

				if (!__instance.IsHungry() && Player.m_localPlayer.GetSkillFactor("Ranching") >= ranchingFoodLevel.Value / 100f && ranchingFoodLevel.Value > 0)
				{
					TimeSpan duration = new DateTime(__instance.m_nview.GetZDO().GetLong("TameLastFeeding")).AddSeconds(__instance.m_fedDuration) - ZNet.instance.GetTime();

					__result += $@", Hungry in: {duration:hh\:mm\:ss}";
				}
			}
		}
	}

	private static int ProcreationTimeOffset(Procreation procreation)
	{
		Random.State state = Random.state;
		try
		{
			Random.InitState(procreation.m_nview.GetZDO().m_uid.GetHashCode());
			return (int)(Random.value * procreation.m_updateInterval);
		}
		finally
		{
			Random.state = state;
		}
	}

	[HarmonyPatch(typeof(Procreation), nameof(Procreation.Awake))]
	private static class StableProcreatingTimes
	{
		private static void Postfix(Procreation __instance)
		{
			__instance.CancelInvoke(nameof(Procreation.Procreate));
			double time = ZNet.instance.GetTimeSeconds();
			__instance.InvokeRepeating(nameof(Procreation.Procreate), (float)(__instance.m_updateInterval - time % __instance.m_updateInterval + ProcreationTimeOffset(__instance)) % __instance.m_updateInterval, __instance.m_updateInterval);
		}
	}
}
