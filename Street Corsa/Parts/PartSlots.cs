using System.Numerics;

namespace StreetCorsa.Parts;

/// <summary>Where parts sit on each other by their slots (no models: the tools use it too)</summary>
public static class PartSlots
{
    /// <summary>
    /// Where a part ends up when one of its slots is brought onto a slot of a placed part; null when either slot is not
    /// there.
    /// </summary>
    public static Matrix4x4? ChildWorld(PartDefinition parent, int parentSlotId, PartDefinition child, int childSlotId, Matrix4x4 parentWorld)
    {
        var parentSlot = parent.Slots.FirstOrDefault(s => s.Id == parentSlotId);
        var childSlot = child.Slots.FirstOrDefault(s => s.Id == childSlotId);
        if (parentSlot == null || childSlot == null || !Matrix4x4.Invert(SlotMatrix(childSlot), out var childSlotInverse)) return null;

        return childSlotInverse * SlotMatrix(parentSlot) * parentWorld;
    }

    public static Matrix4x4 SlotMatrix(PartSlot slot) =>
        Matrix4x4.CreateFromYawPitchRoll(slot.Rotation[0], slot.Rotation[1], slot.Rotation[2]) *
        Matrix4x4.CreateTranslation(slot.Position[0], slot.Position[1], slot.Position[2]);
}
