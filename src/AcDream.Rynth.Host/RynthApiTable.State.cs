using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Core.Items;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

// The table's state slots (RynthAi sub-project #2). Same rules as every table function:
// catch everything, log, return RynthCore's "not available" value.
public sealed unsafe partial class RynthApiTable
{
    private void BindStateSlots()
    {
        Native->GetPlayerPoseFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, float*, float*, float*, float*, float*, float*, float*, int>)&GetPlayerPose;
        Native->GetPlayerHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float*, int>)&GetPlayerHeading;
        Native->GetObjectHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float*, int>)&GetObjectHeading;
        Native->IsPortalingFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&IsPortaling;
        Native->GetBusyStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetBusyState;
        Native->GetCurrentCombatModeFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetCurrentCombatMode;
        Native->GetPlayerVitalsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, uint*, uint*, uint*, uint*, int>)&GetPlayerVitals;
        Native->GetPlayerBaseVitalsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, uint*, int>)&GetPlayerBaseVitals;
        Native->GetVitaeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float>)&GetVitae;
        Native->ReadPlayerEnchantmentsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, double*, int, int>)&ReadPlayerEnchantments;
        Native->GetServerTimeFn = (IntPtr)(delegate* unmanaged[Cdecl]<double>)&GetServerTime;
        Native->GetObjectBoolPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int>)&GetObjectBoolProperty;
        Native->GetObjectDoublePropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, double*, int>)&GetObjectDoubleProperty;
        Native->GetObjectStringPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, IntPtr>)&GetObjectStringProperty;
        Native->GetObjectSkillFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int*, int>)&GetObjectSkill;
        Native->GetObjectSkillBuffedFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int*, int>)&GetObjectSkillBuffed;
        Native->GetObjectAttributeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, uint*, int>)&GetObjectAttribute;
        Native->GetObjectAttribute2ndBaseLevelFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectAttribute2ndBaseLevel;
        Native->IsSpellKnownFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&IsSpellKnown;
        Native->ReadKnownSpellsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, int, int>)&ReadKnownSpells;
        Native->ObjectIsAttackableFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&ObjectIsAttackable;
        Native->GetGroundContainerIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetGroundContainerId;
        Native->GetUseDoneSeqFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetUseDoneSeq;
        Native->GetLastUseDoneFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, uint*, int>)&GetLastUseDone;
        Native->GetLastWeenieErrorFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, uint*, uint*, uint*, int>)&GetLastWeenieError;
        Native->GetLiveObjectIdsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, int, int>)&GetLiveObjectIds;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerPose(uint* cellId, float* x, float* y, float* z, float* qw, float* qx, float* qy, float* qz)
    {
        RynthApiTable? table = _current;
        if (table is null || cellId is null || x is null || y is null || z is null
            || qw is null || qx is null || qy is null || qz is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerPose(out uint cell, out Vector3 origin, out Quaternion orientation))
                return 0;
            *cellId = cell;
            *x = origin.X;
            *y = origin.Y;
            *z = origin.Z;
            *qw = orientation.W;
            *qx = orientation.X;
            *qy = orientation.Y;
            *qz = orientation.Z;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerPose), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerHeading(float* headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null || headingDegrees is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerHeading(out float heading))
                return 0;
            *headingDegrees = heading;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectHeading(uint objectId, float* headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null || headingDegrees is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectHeading(objectId, out float heading))
                return 0;
            *headingDegrees = heading;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int IsPortaling()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.IsPortaling ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(IsPortaling), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetBusyState()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return Math.Max(table._view.BusyCount, 0); }
        catch (Exception error)
        {
            table.Fail(nameof(GetBusyState), error);
            return 0;
        }
    }

    // RynthCore: 1 NonCombat (also its "not available"), 2 Melee, 4 Missile, 8 Magic.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCurrentCombatMode()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return RynthTranslate.NonCombat;
        try { return table._view.CombatMode; }
        catch (Exception error)
        {
            table.Fail(nameof(GetCurrentCombatMode), error);
            return RynthTranslate.NonCombat;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerVitals(uint* health, uint* maxHealth, uint* stamina, uint* maxStamina, uint* mana, uint* maxMana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || maxHealth is null || stamina is null
            || maxStamina is null || mana is null || maxMana is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerVitals(out RynthVitals vitals))
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
            table.Fail(nameof(GetPlayerVitals), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerBaseVitals(uint* health, uint* stamina, uint* mana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || stamina is null || mana is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerBaseVitals(out uint h, out uint s, out uint m))
                return 0;
            *health = h;
            *stamina = s;
            *mana = m;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerBaseVitals), error);
            return 0;
        }
    }

    // RynthCore: a multiplier, 1.0 = no vitae (also its "not available").
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static float GetVitae(uint playerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 1f;
        try { return playerId != 0 && playerId == table._view.PlayerId ? table._view.VitaeMultiplier : 1f; }
        catch (Exception error)
        {
            table.Fail(nameof(GetVitae), error);
            return 1f;
        }
    }

    // RynthCore: copies at most max, returns how many it wrote; -1 when not available.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadPlayerEnchantments(uint* spellIds, double* expiryTimes, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || spellIds is null || expiryTimes is null || max <= 0)
            return -1;
        try
        {
            if (table._view.PlayerEnchantments is not { } enchantments)
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
            table.Fail(nameof(ReadPlayerEnchantments), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static double GetServerTime()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.ServerTime; }
        catch (Exception error)
        {
            table.Fail(nameof(GetServerTime), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectBoolProperty(uint objectId, uint stat, int* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Bools.TryGetValue(stat, out bool found))
                return 0;
            *value = found ? 1 : 0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectBoolProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectDoubleProperty(uint objectId, uint stat, double* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Floats.TryGetValue(stat, out double found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectDoubleProperty), error);
            return 0;
        }
    }

    // RynthCore: an ANSI pointer valid until the next call; 0 = not set.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetObjectStringProperty(uint objectId, uint stat)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Strings.TryGetValue(stat, out string? found))
                return IntPtr.Zero;
            return Replace(ref table.Buffers.String, found);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectStringProperty), error);
            return IntPtr.Zero;
        }
    }

    // RynthCore's GetObjectSkill reports the buffed level (InqSkill raw=0) and the training.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSkill(uint objectId, uint skill, int* level, int* training)
    {
        RynthApiTable? table = _current;
        if (table is null || level is null || training is null)
            return 0;
        try
        {
            if (!table._view.TryGetSkill(objectId, skill, out int buffed, out _, out int trained))
                return 0;
            *level = buffed;
            *training = trained;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSkill), error);
            return 0;
        }
    }

    // raw 0 = buffed, 1 = base.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSkillBuffed(uint objectId, uint skill, int raw, int* level)
    {
        RynthApiTable? table = _current;
        if (table is null || level is null)
            return 0;
        try
        {
            if (!table._view.TryGetSkill(objectId, skill, out int buffed, out int baseLevel, out _))
                return 0;
            *level = raw != 0 ? baseLevel : buffed;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSkillBuffed), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectAttribute(uint objectId, uint attribute, int raw, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetAttribute(objectId, attribute, out uint buffed, out uint baseLevel))
                return 0;
            *value = raw != 0 ? baseLevel : buffed;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectAttribute), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectAttribute2ndBaseLevel(uint objectId, uint vital, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetVitalBase(objectId, vital, out uint baseMaximum))
                return 0;
            *value = baseMaximum;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectAttribute2ndBaseLevel), error);
            return 0;
        }
    }

    // RynthCore: 1 known, 0 not known, -1 not available.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int IsSpellKnown(uint objectId, uint spellId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.IsSpellKnown(objectId, spellId); }
        catch (Exception error)
        {
            table.Fail(nameof(IsSpellKnown), error);
            return -1;
        }
    }

    // RynthCore v69: the total live object count; fills up to capacity (a null buffer just counts).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLiveObjectIds(uint* buffer, int capacity)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            if (table._view.LiveObjectIds is not { } ids)
                return 0;
            if (buffer is not null && capacity > 0)
            {
                int count = Math.Min(capacity, ids.Count);
                for (int i = 0; i < count; i++)
                    buffer[i] = ids[i];
            }
            return ids.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetLiveObjectIds), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadKnownSpells(uint* spellIds, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || spellIds is null || max <= 0)
            return -1;
        try
        {
            if (table._view.KnownSpells is not { } known)
                return -1;
            int count = Math.Min(known.Count, max);
            for (int i = 0; i < count; i++)
                spellIds[i] = known[i];
            return count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ReadKnownSpells), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ObjectIsAttackable(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.IsAttackable(objectId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(ObjectIsAttackable), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetGroundContainerId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.OpenContainerId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetGroundContainerId), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetUseDoneSeq()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.ServerReplies.UseDoneSequence; }
        catch (Exception error)
        {
            table.Fail(nameof(GetUseDoneSeq), error);
            return 0;
        }
    }

    // RynthCore answers 1 whenever it watches game events (seq 0 = none yet); so does this host.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLastUseDone(int* seq, uint* error)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            ServerReplySnapshot replies = table._view.ServerReplies;
            if (seq != null) *seq = replies.UseDoneSequence;
            if (error != null) *error = replies.LastUseDoneError;
            return 1;
        }
        catch (Exception error2)
        {
            table.Fail(nameof(GetLastUseDone), error2);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLastWeenieError(int* seq, uint* error, uint* eventType, uint* objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            ServerReplySnapshot replies = table._view.ServerReplies;
            if (seq != null) *seq = replies.WeenieErrorSequence;
            if (error != null) *error = replies.LastWeenieError;
            if (eventType != null) *eventType = replies.LastWeenieErrorEventType;
            if (objectId != null) *objectId = replies.LastWeenieErrorObjectId;
            return 1;
        }
        catch (Exception failure)
        {
            table.Fail(nameof(GetLastWeenieError), failure);
            return 0;
        }
    }
}
