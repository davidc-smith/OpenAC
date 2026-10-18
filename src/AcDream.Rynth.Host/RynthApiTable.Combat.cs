using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's combat and buff slots (RynthAi sub-project #3): 1 = sent, 0 = refused or failed.
public sealed unsafe partial class RynthApiTable
{
    private void BindCombatSlots()
    {
        Native->CastSpellFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&CastSpell;
        Native->GetCastBusyStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetCastBusyState;
        Native->MeleeAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, float, int>)&MeleeAttack;
        Native->MissileAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, float, int>)&MissileAttack;
        Native->CancelAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&CancelAttack;
        Native->GetTargetVitalsFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, uint*, uint*, uint*, uint*, int>)&GetTargetVitals;
        Native->UseObjectOnFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&UseObjectOn;
        Native->ReadObjectEnchantmentsFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, double*, int, int>)&ReadObjectEnchantments;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CastSpell(uint targetId, int spellId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return spellId > 0 && table._view.CastSpell(targetId, (uint)spellId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(CastSpell), error);
            return 0;
        }
    }

    // RynthCore: 1 while a cast or action gesture plays, 0 when clear (also its "cannot tell").
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCastBusyState()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return table._view.IsCastGestureActive ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetCastBusyState), error);
            return 0;
        }
    }

    // RynthCore clamps power and accuracy to 0..1 before sending.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MeleeAttack(uint targetId, int height, float power)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return targetId != 0 && RynthTranslate.ToAttackHeight(height) is not null
                && table._view.MeleeAttack(targetId, height, Math.Clamp(power, 0f, 1f)) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MeleeAttack), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MissileAttack(uint targetId, int height, float accuracy)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return targetId != 0 && RynthTranslate.ToAttackHeight(height) is not null
                && table._view.MissileAttack(targetId, height, Math.Clamp(accuracy, 0f, 1f)) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MissileAttack), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CancelAttack()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return table._view.CancelAttack() ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(CancelAttack), error);
            return 0;
        }
    }

    // RynthCore: 1 with the creature's appraised vitals, 0 when it holds none.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetTargetVitals(
        uint objectId, uint* health, uint* maxHealth, uint* stamina, uint* maxStamina, uint* mana, uint* maxMana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || maxHealth is null || stamina is null
            || maxStamina is null || mana is null || maxMana is null)
            return 0;
        *health = *maxHealth = *stamina = *maxStamina = *mana = *maxMana = 0;
        try
        {
            if (objectId == 0 || !table._view.TryGetTargetVitals(objectId, out RynthVitals vitals))
                return 0;
            *health = vitals.Health;
            *maxHealth = vitals.MaxHealth;
            *stamina = vitals.Stamina;
            *maxStamina = vitals.MaxStamina;
            *mana = vitals.Mana;
            *maxMana = vitals.MaxMana;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetTargetVitals), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UseObjectOn(uint sourceObjectId, uint targetObjectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return sourceObjectId != 0 && targetObjectId != 0
                && table._view.UseObjectOn(sourceObjectId, targetObjectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(UseObjectOn), error);
            return 0;
        }
    }

    // RynthCore: 0 for max <= 0, -1 when the object's registry cannot be read, else how many it wrote.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadObjectEnchantments(uint objectId, uint* spellIds, double* expiryTimes, int max)
    {
        RynthApiTable? table = _current;
        if (max <= 0)
            return 0;
        if (table is null || spellIds is null || expiryTimes is null)
            return -1;
        try
        {
            if (objectId == 0 || table._view.GetObjectEnchantments(objectId) is not { } enchantments)
                return -1;
            int count = Math.Min(enchantments.Count, max);
            for (int i = 0; i < count; i++)
            {
                spellIds[i] = enchantments[i].SpellId;
                expiryTimes[i] = enchantments[i].ExpiryServerTime;
            }

            return count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ReadObjectEnchantments), error);
            return -1;
        }
    }
}
