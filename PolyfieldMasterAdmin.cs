using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace PolyfieldMasterAdmin
{
    [BepInPlugin("com.polyfield.master.admin", "Polyfield 终极服主控", "5.0")]
    public class PolyfieldMasterAdmin : BasePlugin
    {
        // ================= 核心配置区 =================
        public static float INVINCIBLE_SECONDS = 10f; // 复活无敌时间：10秒
        public static int RPG_DAMAGE_FIXED = 30;      // 火箭筒固定伤害：30
        public static int RPG_WEAPON_ID = 7;          // 【注意】火箭筒的游戏内ID（需根据实际情况微调）

        public static readonly string[] CYCLE_MESSAGES = new string[]
        {
            "[规则] 禁止外挂，禁止刷屏",
            "[规则1] 禁止开挂、禁止刷屏等",
            "[欢迎] 欢迎玩家到 GPD 训练营",
            "[聊天] 禁止谩骂，违者踢出"
        };

        // ================= 内部数据 (请勿修改) =================
        private static float _lastBroadcastTime = 0f;
        private static int _broadcastIndex = 0;
        private static readonly Dictionary<uint, float> _invincibleDict = new Dictionary<uint, float>();

        public override void Load()
        {
            var harmony = new Harmony("com.polyfield.master.admin");
            harmony.PatchAll(typeof(PolyfieldMasterAdmin));
            Log.LogInfo("[Polyfield Master] 终极服主控 V5.0 已完美加载。");
            Log.LogInfo($"[设置] 火箭筒伤害已锁定: {RPG_DAMAGE_FIXED}");
            Log.LogInfo($"[设置] 复活无敌时间已锁定: {INVINCIBLE_SECONDS} 秒");
        }

        // ================= 1. 系统广播 (服务端定时器) =================
        [HarmonyPatch(typeof(MonoBehaviour), "Update")]
        [HarmonyPostfix]
        static void MonoBehaviour_Update_Postfix(MonoBehaviour __instance)
        {
            if (__instance is GameManager gm && NetworkServer.active)
            {
                if (Time.time - _lastBroadcastTime >= 30f)
                {
                    string msg = CYCLE_MESSAGES[_broadcastIndex % CYCLE_MESSAGES.Length];
                    gm.CreateChat(null, msg);
                    
                    _lastBroadcastTime = Time.time;
                    _broadcastIndex++;
                }
            }
        }

        // ================= 2. 火箭筒伤害锁定 =================
        // 拦截子弹发射事件
        [HarmonyPatch(typeof(GameUtility), "FireBullet", new Type[] {
            typeof(Vector3), typeof(Vector3), typeof(int), typeof(int), typeof(int), typeof(float), typeof(int), typeof(string), typeof(bool)
        })]
        [HarmonyPrefix]
        static void FireBullet_Prefix(Vector3 _pos, Vector3 _dir, ref int _damage, 
                                      int _speed, float _range, int _weaponID, string _name, bool _localPlayer)
        {
            if (NetworkServer.active)
            {
                // 通过武器 ID 精准识别火箭筒（如果 ID 不准，可以改下面的条件判断）
                if (_weaponID == RPG_WEAPON_ID)
                {
                    _damage = RPG_DAMAGE_FIXED; // 强制锁定伤害为 30
                }
            }
        }

        // ================= 3. 无敌伤害拦截 =================
        // 拦截玩家受到伤害的底层方法
        [HarmonyPatch(typeof(PlayerControl), "ApplyDamage")]
        [HarmonyPrefix]
        static bool ApplyDamage_Prefix(PlayerControl __instance, int damageType, int weaponID, int damage, uint target, byte isNPC, string data)
        {
            if (__instance == null || !NetworkServer.active) return true;

            // 检查玩家是否在 10 秒无敌期内
            if (_invincibleDict.TryGetValue(__instance.netId, out float endTime))
            {
                if (Time.time < endTime)
                {
                    return false; // 无敌状态下，直接拦截并免疫所有伤害
                }
                else
                {
                    // 超过 10 秒后，移除无敌状态，恢复正常受击
                    _invincibleDict.Remove(__instance.netId);
                }
            }
            return true;
        }

        // ================= 4. 触发复活无敌 (10秒) =================
        // 监听玩家状态重置/重生
        [HarmonyPatch(typeof(PlayerControl), "RpcUpdateState")]
        [HarmonyPostfix]
        static void PlayerControl_RpcUpdateState_Postfix(PlayerControl __instance, string data)
        {
            if (__instance == null || !NetworkServer.active) return;

            // 玩家重生时，记录当前时间 + 10 秒无敌
            _invincibleDict[__instance.netId] = Time.time + INVINCIBLE_SECONDS;
            Log.LogInfo($"[无敌] 玩家 [{__instance.playerName}] 已重生，获得 10 秒无敌状态。");
        }
    }
}
