using Insthync.DevExtension;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    public partial class PlayerCharacterEntity
    {
        [Header("Demo Developer Extension")]
        public bool writeAddonLog;
        [DevExtMethods("Awake")]
        protected void DevExtAwakeDemo()
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.Awake()");
            onStart += DevExtStartDemo;
            onEnable += DevExtOnEnableDemo;
            onDisable += DevExtOnDisableDemo;
            onUpdate += DevExtUpdateDemo;
            onSetup += DevExtOnSetupDemo;
            onSetupNetElements += DevExtSetupNetElementsDemo;
            onNetworkDestroy += DevExtOnNetworkDestroyDemo;
            onReceiveDamage += DevExtReceiveDamageDemo;
            onReceivedDamage += DevExtReceivedDamageDemo;
            onApplyBuff += DevExtApplyBuff;
            onRemoveBuff += DevExtRemoveBuff;
        }

        [DevExtMethods("OnDestroy")]
        protected void DevExtOnDestroyDemo()
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.OnDestroy()");
            onStart -= DevExtStartDemo;
            onEnable -= DevExtOnEnableDemo;
            onDisable -= DevExtOnDisableDemo;
            onUpdate -= DevExtUpdateDemo;
            onSetup -= DevExtOnSetupDemo;
            onSetupNetElements -= DevExtSetupNetElementsDemo;
            onNetworkDestroy -= DevExtOnNetworkDestroyDemo;
            onReceiveDamage -= DevExtReceiveDamageDemo;
            onReceivedDamage -= DevExtReceivedDamageDemo;
            onApplyBuff -= DevExtApplyBuff;
            onRemoveBuff -= DevExtRemoveBuff;
        }

        protected void DevExtStartDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.Start()");
        }

        protected void DevExtOnEnableDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.OnEnable()");
        }

        protected void DevExtOnDisableDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.OnDisable()");
        }

        protected void DevExtUpdateDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.Update()");
        }

        protected void DevExtOnSetupDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.OnSetup()");
        }

        protected void DevExtSetupNetElementsDemo(BaseGameEntity target)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.SetupNetElements()");
        }

        protected void DevExtOnNetworkDestroyDemo(BaseGameEntity target, byte reasons)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.OnNetworkDestroy(" + reasons + ")");
        }

        protected void DevExtReceiveDamageDemo(DamageableEntity target, HitBoxPosition position, Vector3 fromPosition, EntityInfo attacker, Dictionary<DamageElement, MinMaxFloat> allDamageAmounts, CharacterItem weapon, BaseSkill skill, int skillLevel)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.ReceiveDamage("
                + (attacker.Entity != null ? attacker.Entity.GetGameObject().name : attacker.Id) + ", " + weapon + ", " + allDamageAmounts.Count + ", " + (skill != null ? skill.Title : "No Debuff") + ")");
        }

        protected void DevExtReceivedDamageDemo(DamageableEntity target, HitBoxPosition position, Vector3 fromPosition, EntityInfo attacker, CombatAmountType combatAmountType, int damage, CharacterItem weapon, BaseSkill skill, int skillLevel, CharacterBuff buff, bool isDamageOverTime)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.ReceivedDamage("
                + (attacker.Entity != null ? attacker.Entity.GetGameObject().name : attacker.Id) + ", " + combatAmountType + ", " + damage + ")");
        }

        protected void DevExtApplyBuff(BaseCharacterEntity target, CharacterBuff buff)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.ApplyBuff("
                + buff.id + ", " + buff.type + ", " + buff.dataId + ", " + buff.level + ")");
        }

        protected void DevExtRemoveBuff(BaseCharacterEntity target, CharacterBuff buff, BuffRemoveReasons reason)
        {
            if (writeAddonLog) Debug.Log("[" + name + "] PlayerCharacterEntity.RemoveBuff("
                + buff.id + ", " + buff.type + ", " + buff.dataId + ", " + buff.level + ", " + reason + ")");
        }
    }
}
