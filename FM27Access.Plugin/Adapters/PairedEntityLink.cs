using SI.Bindable;
using UnityEngine.UIElements;
using FM27Access.Plugin.Server;

namespace FM27Access.Plugin.Adapters;

// Native entity components split one destination into an image hit surface and
// a named text link. Keep the text link (including its real context menu), rather
// than exposing the same destination twice. Caption equality alone is insufficient.
internal static class PairedEntityLink
{
    internal static bool IsRedundantCaption(FM27Access.Navigation.IAccessibleElement source)
    {
        if (source is not PluginAdapterBase adapter) return false;
        var text = adapter.BackingElement.TryCast<SIText>();
        if (text == null) return false;
        // The generic adapter precedes inline-link decomposition. Its legacy
        // menu/activation inference can differ from a genuinely readonly native
        // fragment; use the same single-fragment contract as the HTML renderer.
        var parts = SITextLink.ReadParts(text);
        if (parts?.Count == 1 && parts[0].Role == "text" && !parts[0].CanActivate) source = parts[0];
        if (source.Role != "text" || source.CanActivate) return false;
        var target = CaptionSubject(text);
        if (target == null && text.EmbeddedData?.Count != 1) return false;
        if (source.HasContextMenu || !string.IsNullOrEmpty(source.GetDescription())) return false;
        var caption = Speech.TextFilters.CleanForDisplay(text.text);
        if (string.IsNullOrWhiteSpace(caption)) return false;
        var scope = text.parent;
        for (int depth = 0; depth < 12 && scope != null; depth++, scope = scope.parent)
        {
            var type = scope.GetIl2CppType().Name;
            if (type is "Panel" or "DynamicItem" or "Cell" or "BindingRoot") return false;
            int matches = 0;
            foreach (var button in NativeTreeQuery.All<SIButton>(scope))
            {
                if (!ElementBridge.IsVisibleInHierarchy(button) || !button.enabledInHierarchy
                    || button.Events?.Count != 1) continue;
                var ownTexts = NativeTreeQuery.All<SIText>(button);
                // A late native caption can replace the image-only branch.
                // Its own single-record name still proves the paired entity;
                // never remove that button's own caption or a mixed label.
                bool namedPair = false;
                if (ownTexts.Count != 0)
                {
                    if (ownTexts.Count != 1 || ownTexts[0].Pointer == text.Pointer || target == null) continue;
                    var ownSubject = CaptionSubject(ownTexts[0]);
                    namedPair = ownSubject?.CombinedIndexAndType == target.CombinedIndexAndType;
                    if (!namedPair) continue;
                }
                var reference = OpeningOrEmbeddedRecord(button);
                bool same = namedPair || reference != null && target != null && SameSubject(button, reference, target);
                // A native icon/name pair can resolve its command argument only
                // on activation. This folds TEXT only, never an independent action:
                // its sole entity-name caption already labels the sole visible
                // ordinary image button in this same small component.
                if (!same && NativeTreeQuery.All<SIText>(scope).Count == 1
                    && NativeTreeQuery.All<SIButton>(scope).Count == 1
                    && NativeTreeQuery.All<SIImage>(scope).Count > 0
                    && !InfoButtonAdapter.IsInfoButton(button)
                    && !FM27Access.Navigation.NavigationManager.IsHidden(new ButtonAdapter(button))) same = true;
                if (same
                    && Speech.TextFilters.CleanForDisplay(new ButtonAdapter(button).GetLabel()) == caption) matches++;
            }
            if (matches != 0) return matches == 1;
            if (type == "BindingRemapper") return false;
        }
        return false;
    }

    // Prefer the actual caption already bound beside an image, but only for
    // its own entity. A club crest beside a player name must ask for its club.
    internal static string CurrentCaption(SIButton button)
    {
        if (button?.Events?.Count != 1) return null;
        var target = OpeningOrEmbeddedRecord(button);
        if (target == null) return null;
        var scope = button.parent;
        for (int depth = 0; depth < 10 && scope != null; depth++, scope = scope.parent)
        {
            var type = scope.GetIl2CppType().Name;
            if (type is "Panel" or "DynamicItem" or "BindingRoot" or "StreamedTable") break;
            string caption = null;
            foreach (var text in NativeTreeQuery.All<SIText>(scope))
            {
                if (!ElementBridge.IsVisibleInHierarchy(text)) continue;
                var subject = CaptionSubject(text);
                if (subject == null || !SameSubject(button, target, subject)) continue;
                var value = Speech.TextFilters.CleanForDisplay(text.text);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (caption != null) return null;
                caption = value;
            }
            if (caption != null) return caption;
            if (type == "Cell") break;
        }
        return null;
    }

    private static bool SameSubject(SIButton button, FM.UI.DatabaseRecordReference target, FM.UI.DatabaseRecordReference subject)
    {
        if (target.CombinedIndexAndType == subject.CombinedIndexAndType) return true;
        if (target.TryCast<FM.UI.ClubReference>() is not { } club || subject.TryCast<FM.UI.TeamReference>() == null) return false;
        var key = button.Events[0].m_sourceKey;
        var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
        if (key.m_key == 0 || !subsystem.TryGetNode(ref key, out var node) || node.ChildCount > 32) return false;
        foreach (var child in node.Children)
        {
            var value = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.ClubReference>();
            if (value?.CombinedIndexAndType == club.CombinedIndexAndType && IsOwningTeam(child, club, subject)) return true;
        }
        return false;
    }

    private static FM.UI.DatabaseRecordReference CaptionSubject(SIText text)
    {
        if (text == null) return null;
        if (text.EmbeddedData?.Count == 1)
        {
            // Recycled SIText can show a new name before its embedded record
            // and link markup bind. Reuse the existing current-link contract.
            if (SITextLink.ReadParts(text)?.Count != 1) return null;
            return BindingRecords.Field(BindingRecords.Fields(text.EmbeddedData[0]), "UIRecord")
                ?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
        }
        // Reuse the known native TranslationID/TranslationArg formatter. A
        // caption naming several records cannot establish one entity's name.
        if (!StaticButtonCaption.IsFormattedLocalizedText(text)) return null;
        var handler = text.m_bindingHandler?.Cast<Il2CppSystem.Object>()?.TryCast<BindingHandler<SIText>>();
        if (handler?.IsBound != true) return null;
        var bag = handler.m_state?.VisualFunction?.Bag;
        FM.UI.DatabaseRecordReference reference = null;
        int remaining = 24;
        for (var request = bag?.m_head; request != null && remaining-- > 0; request = request.Next)
        {
            if (request.m_kind != BindingBag.Request.Kind.Binding) continue;
            var value = request.Value?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (value == null) continue;
            if (reference != null && reference.CombinedIndexAndType != value.CombinedIndexAndType) return null;
            reference = value;
        }
        return remaining < 0 ? null : reference;
    }

    private static FM.UI.DatabaseRecordReference OpeningOrEmbeddedRecord(SIButton button)
    {
        if (button.Events[0].TryCast<EmbeddedDataClickedEventContext>() == null) return PrimaryOpeningRecord(button);
        var key = button.Events[0].m_sourceKey;
        var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
        if (key.m_key == 0 || !subsystem.TryGetNode(ref key, out var node) || node.ChildCount > 32) return null;
        FM.UI.DatabaseRecordReference reference = null;
        foreach (var child in node.Children)
        {
            var value = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (value == null) continue;
            if (reference != null && reference.CombinedIndexAndType != value.CombinedIndexAndType) return null;
            reference = value;
        }
        return reference;
    }

    internal static bool IsRedundantIcon(VisualElement element)
    {
        if (element?.GetIl2CppType().Name == "SIImage") return IsCaptionedFixtureImage(element);
        if (element?.name == "NationButton") return IsPairedFlag(element);
        if (element?.name == "") return IsPairedPortrait(element);
        if (element?.name != "ClubButton") return false;
        var button = element.TryCast<SIButton>();
        if (button?.Events?.Count != 1) return false;
        if (button.Events[0].GetIl2CppType().Name != "EmbeddedDataClickedEventContext")
            return IsPairedOpeningImage(button);
        var component = element.parent?.parent;
        if (component?.GetIl2CppType().Name != "ComponentElement"
            || NativeTreeQuery.All<SIImage>(component, "ClubImage").Count != 1
            || NativeTreeQuery.All<SIText>(component).Count != 0) return false;
        var bindings = SI.Core.GameSubsystems.Get<BindingSubsystem>();
        var key = button.Events[0].m_sourceKey;
        if (bindings == null || !bindings.TryGetNode(ref key, out var node)) return false;
        FM.UI.ClubReference club = null;
        Bindings.Key clubKey = default;
        foreach (var child in node.Children)
        {
            var record = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (record == null) continue;
            var candidate = record.TryCast<FM.UI.ClubReference>();
            if (candidate == null || club != null && club.CombinedIndexAndType != candidate.CombinedIndexAndType)
                return false;
            club = candidate;
            clubKey = child.Key;
        }
        if (club == null) return false;
        if (!bindings.TryGetNode(ref clubKey, out var clubSource)) return false;

        // Stop inside this entity's immediate component, never at the page or
        // another table cell. Native queries are local to this Unity dispatch.
        var scope = component.parent;
        for (int depth = 0; depth < 10 && scope != null; depth++, scope = scope.parent)
        {
            var scopeType = scope.GetIl2CppType().Name;
            if (scopeType is "Panel" or "DynamicItem") break;
            int matches = 0;
            foreach (var text in NativeTreeQuery.All<SIText>(scope))
            {
                if (!ElementBridge.IsVisibleInHierarchy(text) || !text.EnableEmbeddedLinks) continue;
                var parts = SITextLink.ReadParts(text);
                if (parts?.Count != 1 || parts[0] is not SITextLink.Part part
                    || !part.CanActivate || !part.LinkIndex.HasValue) continue;
                var record = BindingRecords.Field(BindingRecords.Fields(text.EmbeddedData[part.LinkIndex.Value]), "UIRecord")
                    ?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                if (record == null) continue;
                bool same = record.CombinedIndexAndType == club.CombinedIndexAndType;
                if (!same && record.TryCast<FM.UI.TeamReference>() != null)
                    same = IsOwningTeam(clubSource, club, record);
                // The shared team component binds the actual Team and its
                // ClubForObjectLookupData. Do not infer a club from names or IDs.
                if (!same && record.TryCast<FM.UI.TeamReference>() != null)
                {
                    var parent = node.Parent;
                    for (int bindingDepth = 0; bindingDepth < 2 && parent != null; bindingDepth++, parent = parent.Parent)
                    {
                        var teamNode = BindingRecords.FindNode(parent, "Team", 0);
                        var team = teamNode == null ? null : BindingRecords.TeamOf(BindingRecords.ValueOf(teamNode));
                        if (team?.CombinedIndexAndType != record.CombinedIndexAndType) continue;
                        var clubNode = BindingRecords.FindNode(teamNode, "ClubForObjectLookupData", 0);
                        var owner = clubNode == null ? null : BindingRecords.ValueOf(clubNode)?.Get()?.TryCast<FM.UI.ClubReference>();
                        same = owner?.CombinedIndexAndType == club.CombinedIndexAndType;
                        break;
                    }
                }
                if (same) matches++;
            }
            if (matches != 0) return matches == 1;
            if (scopeType == "Cell") break;
        }
        return false;
    }

    private static bool IsCaptionedFixtureImage(VisualElement image)
    {
        // The full native calendar's readonly fixture component has separate
        // team/competition badges and captions. Its actual bound Event and
        // caption record must agree; Icon alone is never a decorative verdict.
        if (image.name is not ("Icon" or "CompIcon") || image.parent == null
            || !string.IsNullOrWhiteSpace(image.tooltip) || new ImageAdapter(image).HasContextMenu) return false;
        var opponents = NativeTreeQuery.All<SIText>(image.parent, "TeamOpponent");
        var competitions = NativeTreeQuery.All<SIText>(image.parent, "Name");
        var venues = NativeTreeQuery.All<SIText>(image.parent, "Venue");
        if (opponents.Count != 1 || competitions.Count != 1 || venues.Count != 1) return false;
        var opponent = opponents[0];
        var competition = competitions[0];
        var venue = venues[0];
        if (opponent.parent?.Pointer != image.parent.Pointer
            || competition.parent?.Pointer != image.parent.Pointer || venue.parent?.Pointer != image.parent.Pointer
            || !ElementBridge.IsVisibleInHierarchy(opponent)
            || !ElementBridge.IsVisibleInHierarchy(competition)
            || !ElementBridge.IsVisibleInHierarchy(venue)) return false;
        bool ownEvent = false;
        for (var up = image.parent; up != null; up = up.parent)
        {
            if (up.name == "CalendarEventDynamicItem") { ownEvent = true; break; }
            if (up.GetIl2CppType().Name is "BindingRoot" or "Panel") break;
        }
        if (!ownEvent) return false;
        var data = BindingRecords.Fields(BindingRecords.RecordAbove(image, "Event", 20, 3));
        if (BindingRecords.TeamOf(BindingRecords.Field(data, "TeamOpponent")) == null
            || BindingRecords.Field(data, "Competition")?.Get()?.TryCast<FM.UI.CompReference>() == null) return false;
        var reference = BindingRecords.Field(data, image.name == "Icon" ? "TeamOpponent" : "Competition")
            ?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
        var caption = CaptionSubject(image.name == "Icon" ? opponent : competition);
        return reference != null && caption?.CombinedIndexAndType == reference.CombinedIndexAndType;
    }

    // A remapped crest can bind Team.Club rather than Team.ClubForObjectLookupData.
    // Follow only the real aliases of this Club value and check its actual owner;
    // a nearby team's caption or an unrelated Club field establishes nothing.
    private static bool IsOwningTeam(IReadOnlyNode source, FM.UI.ClubReference club, FM.UI.DatabaseRecordReference team)
    {
        if (IsDirectOwningTeam(source, club, team)) return true;
        // A remapper can copy activeclub instead of retaining an Alias. Find
        // only a matching current Club value in this source's binding scope,
        // and still require its real canonical Team.Club owner to match.
        var parent = source.Parent;
        for (int depth = 0; depth < 3 && parent != null; depth++, parent = parent.Parent)
        {
            if (parent.ChildCount > 32) break;
            var child = BindingRecords.FindNode(parent, "Club", 0);
            if (child == null) continue;
            var value = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.ClubReference>();
            if (value?.CombinedIndexAndType == club.CombinedIndexAndType && IsDirectOwningTeam(child, club, team))
                return true;
        }
        return false;
    }

    private static bool IsDirectOwningTeam(IReadOnlyNode source, FM.UI.ClubReference club, FM.UI.DatabaseRecordReference team)
    {
        var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
        for (int depth = 0; depth < 8 && source != null; depth++)
        {
            var property = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance.GetInfo(source.PropID)?.Name;
            var value = BindingRecords.ValueOf(source)?.Get()?.TryCast<FM.UI.ClubReference>();
            var owner = source.Parent == null ? null : BindingRecords.TeamOf(BindingRecords.ValueOf(source.Parent));
            if (property is "Club" or "ClubForObjectLookupData"
                && value?.CombinedIndexAndType == club.CombinedIndexAndType
                && owner?.CombinedIndexAndType == team.CombinedIndexAndType) return true;
            var key = source.Alias;
            if (key.m_key == 0 || !subsystem.TryGetNode(ref key, out source)) break;
        }
        return false;
    }

    private static bool IsPairedPortrait(VisualElement element)
    {
        var button = element.TryCast<SIButton>();
        var remapper = element.parent;
        if (button?.Events?.Count != 1 || remapper?.GetIl2CppType().Name != "BindingRemapper"
            || remapper.hierarchy.childCount != 2 || NativeTreeQuery.All<SIText>(element).Count != 0
            || NativeTreeQuery.All<TemplateContainer>(element, "PersonPicture").Count != 1) return false;
        var target = PrimaryOpeningRecord(button);
        return target?.TryCast<FM.UI.PersonReference>() != null && HasUniqueNamedLink(remapper, target);
    }

    private static bool IsPairedOpeningImage(SIButton button)
    {
        if (NativeTreeQuery.All<SIText>(button).Count != 0) return false;
        var target = PrimaryOpeningRecord(button);
        if (target == null) return false;
        var scope = button.parent;
        for (int depth = 0; depth < 10 && scope != null; depth++, scope = scope.parent)
        {
            var type = scope.GetIl2CppType().Name;
            if (type is "Panel" or "DynamicItem" or "BindingRoot" or "StreamedTable") return false;
            if (NativeTreeQuery.All<SIImage>(scope).Count > 0 && HasUniqueNamedLink(scope, target)) return true;
            if (type == "Cell" || type == "ComponentElement") return false;
        }
        return false;
    }

    private static bool IsPairedFlag(VisualElement element)
    {
        var button = element.TryCast<SIButton>();
        if (button?.Events?.Count != 1 || NativeTreeQuery.All<SIText>(element).Count != 0
            || NativeTreeQuery.All<SIImage>(element, "NationFlag").Count != 1) return false;
        var target = PrimaryOpeningRecord(button);
        // A flag can open a specific national-team container instead of the
        // named Nation. Its bound first argument must match the actual text link.
        if (target == null) return false;
        var scope = element.parent;
        for (int depth = 0; depth < 10 && scope != null; depth++, scope = scope.parent)
        {
            var type = scope.GetIl2CppType().Name;
            if (type is "Cell" or "Panel" or "DynamicItem" or "BindingRoot" or "StreamedTable") return false;
            // The compact club tile uses a local visual frame instead of the
            // larger entity ComponentElement. Stop at its binding scope; never
            // count unrelated country links elsewhere on the page.
            if (HasUniqueNamedLink(scope, target)) return true;
            if (type is "ComponentElement" or "BindingRemapper") return false;
        }
        return false;
    }

    private static FM.UI.DatabaseRecordReference PrimaryOpeningRecord(SIButton button)
    {
        var action = button.Events[0];
        var rootKey = action.m_sourceKey;
        var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
        if (!subsystem.TryGetNode(ref rootKey, out var root)) return null;
        if (action.TryCast<FM.UI.OpenPanelOrPanelEventContext>() is { } conditional)
            action = conditional.m_bindingValue ? conditional.m_panelWhenTrueEventContext : conditional.m_panelWhenFalseEventContext;
        var open = action?.TryCast<SI.Bindable.Events.OpenPanelEventContext>();
        if (open?.m_argumentBindings == null || open.m_argumentBindings.Length == 0) return null;
        var path = open.m_argumentBindings[0]?.path?.m_path;
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('.');
        for (int depth = 0; depth < 4 && root != null; depth++, root = root.Parent)
        {
            if (root.ChildCount > 32) break;
            var node = root;
            foreach (var part in parts)
            {
                IReadOnlyNode found = null;
                foreach (var child in node.Children)
                    if (string.Equals(child.Name, part, StringComparison.OrdinalIgnoreCase))
                    { if (found != null) return null; found = child; }
                node = found;
                if (node == null) break;
            }
            if (node == null) continue;
            return BindingRecords.ValueOf(node)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
        }
        return null;
    }

    private static bool HasUniqueNamedLink(VisualElement scope, FM.UI.DatabaseRecordReference target)
    {
        int matches = 0;
        foreach (var text in NativeTreeQuery.All<SIText>(scope))
        {
            if (!ElementBridge.IsVisibleInHierarchy(text) || !text.EnableEmbeddedLinks) continue;
            var parts = SITextLink.ReadParts(text);
            if (parts?.Count != 1 || parts[0] is not SITextLink.Part part || !part.CanActivate || !part.LinkIndex.HasValue) continue;
            var record = BindingRecords.Field(BindingRecords.Fields(text.EmbeddedData[part.LinkIndex.Value]), "UIRecord")
                ?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (record?.CombinedIndexAndType == target.CombinedIndexAndType) matches++;
        }
        return matches == 1;
    }
}
