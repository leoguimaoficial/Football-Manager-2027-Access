using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A ribbon bookmark slot: the icon-only SIButton ("BookmarkButton") in the title bar's
/// Bookmarks strip. Its display name lives in a lazy hover tooltip the walk never sees, so the
/// generic ButtonAdapter degraded every slot to "unlabeled icon button". The real name is read
/// live from the game's bookmark module (<see cref="BookmarkBar"/>): the strip renders the
/// module's selected-bookmarks list in order, so this button's position among its sibling slots
/// indexes straight into that list. The game's own Shift+N shortcuts are slot-indexed the same
/// way, which is also what keeps the companion's Ctrl+N chord (ServerHandles.Bookmark) honest.
/// </summary>
internal class BookmarkButtonAdapter : ButtonAdapter
{
    public BookmarkButtonAdapter(VisualElement element) : base(element) { }

    // One computed name, no primary/secondary split.
    protected override bool SplitsLabel => false;

    // HTML surface only: append the companion's Ctrl+N chord so the strip self-documents
    // ("Messages, Ctrl 1"). Slots past 9 have no chord and show the bare name. The speech
    // surface (GetLabel/GetAnnouncement) stays name-only — in-game the chord that applies is
    // the game's own Shift+N, not the companion's Ctrl+N.
    public override string GetPrimaryLabel()
    {
        var label = GetLabel();
        int slot = BookmarkBar.SlotIndexOf(Element);
        if (slot >= 0 && slot < 9) return $"{label}, Ctrl {slot + 1}";
        return label;
    }

    public override string GetLabel()
    {
        int slot = BookmarkBar.SlotIndexOf(Element);
        if (slot >= 0)
        {
            var name = BookmarkBar.NameForSlot(slot);
            if (!string.IsNullOrEmpty(name)) return name;
            // The slot number is still true (it is what Ctrl+N / Shift+N open), so keep it.
            Speech.Degrade.Note($"bookmark {slot + 1}: name unreadable from module list");
            return $"bookmark {slot + 1}";
        }
        // No slot ancestry: during a strip rebuild (a bookmark was added/removed) LiveValues
        // keeps re-reading the old, now-detached buttons every tick until the next render.
        // Detached is routine teardown — throw so each caller handles it at its own boundary
        // (LiveValues drops the tracker quietly, per the 9d55f8b pattern) instead of logging a
        // degrade note per tick. An ATTACHED button the walk cannot place is a real defect and
        // stays loud.
        if (Element.panel == null)
            throw new System.InvalidOperationException("bookmark button left the live tree");
        Speech.Degrade.Note("bookmark button outside a recognisable slot strip");
        return "bookmark";
    }
}
