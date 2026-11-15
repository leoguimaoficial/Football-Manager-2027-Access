using System;
using System.Collections.Generic;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Live enumeration of the game's navigation tree for the Navigator (docs/navigator-plan.md):
/// the main categories (Portal, Squad, ...), each category's subsection entries, and the children
/// of subsection entries that are dropdowns — read straight from GameWorldTabsAndBookmarksModule's
/// MainTabToSubtabsMap, the same live-filtered model the visible bars render from. Dropdown
/// children come from the model's ChildList, so no popup ever opens. Built fresh on every call
/// (never cached) and main-thread only.
///
/// Availability is part of the model: entries whose Condition is false are the ones the game
/// itself would not show, so they are marked rather than silently skipped — the dev view wants to
/// see them, and the player surface (phase 3) filters on the flag. An entry whose label cannot be
/// read degrades in place to an unknown-destination marker.
/// </summary>
internal static class NavigatorIndex
{
    private static long _navigationRequest;
    // Children of children have not been observed in the live model; the guard is against a
    // malformed self-referencing ChildList, not an expected depth.
    private const int MaxDepth = 4;

    internal sealed class Entry
    {
        public string Label;
        public string Path;          // owning trail, e.g. "Squad" or "Squad > Training"
        public uint PanelUid;
        public int ObjectUniqueId;
        public int Depth;            // 0 = category, 1 = subsection, 2+ = dropdown child
        public bool Disabled;
        public bool ConditionMet;
        public string CategoryName;  // main tab asset name, e.g. "SquadScreen"
    }

    /// <summary>
    /// The full navigation tree, or null when the module or its map cannot be reached (the caller
    /// reports that as one honest failure, not an empty list that reads as "no destinations").
    /// </summary>
    internal static List<Entry> Build()
    {
        var module = BookmarkBar.Module();
        if (module == null) return null;

        var map = module.m_mainNavigationMap;
        if (map == null) return null;
        var tabs = map.m_map;
        if (tabs == null) return null;

        var entries = new List<Entry>();
        for (int t = 0; t < tabs.Length; t++)
        {
            var tab = tabs[t];
            // FM27's map also contains contextual meetings, press conferences
            // and debug definitions. Only the native primary navigation tabs
            // belong in the permanent menu tree.
            if (tab != null && (tab.HideFromNavBar || !tab.ShowAsPrimaryTab)) continue;
            var pid = tab?.PanelID;
            if (pid == null)
            {
                Degrade.Note($"[NavigatorIndex] main tab {t} has no PanelID; emitting marker.");
                entries.Add(Marker(depth: 0, path: "", category: $"<tab {t}>"));
                continue;
            }

            string categoryName = pid.name;
            // The ribbon caption first (the label the player knows the category by; PanelID.Title
            // diverges — PortalScreen's is "Overview"), then the title, then the asset name.
            string categoryLabel = null;
            if (CategoryRibbonTargets.TryGetValue(categoryName, out var ribbonTarget))
            {
                try { categoryLabel = ServerHandles.RibbonButtonLabel(ribbonTarget); }
                catch (Exception e)
                {
                    Degrade.Note($"[NavigatorIndex] ribbon label for {categoryName} unreadable: {e.GetType().Name}: {e.Message}");
                }
                // A degrade marker is an honest gap on a button, but as a category caption it
                // would poison every entry's path ("Income — unlabeled icon button > Finances");
                // fall through to the PanelID title instead.
                if (categoryLabel == Adapters.ButtonAdapter.UnlabeledIconButton) categoryLabel = null;
            }
            if (string.IsNullOrWhiteSpace(categoryLabel))
            {
                try { categoryLabel = pid.Title.TranslatedString; }
                catch (Exception e)
                {
                    Degrade.Note($"[NavigatorIndex] category title for {categoryName} unreadable: {e.GetType().Name}: {e.Message}");
                }
            }
            if (string.IsNullOrWhiteSpace(categoryLabel)) categoryLabel = categoryName;

            entries.Add(new Entry
            {
                Label = categoryLabel,
                Path = "",
                PanelUid = pid.ID,
                ObjectUniqueId = -1,
                Depth = 0,
                Disabled = false,
                ConditionMet = true,
                CategoryName = categoryName,
            });

            var subtabs = tab.PanelSubTabsList;
            var subMenus = subtabs?.SubMenus;
            if (subMenus == null) continue;
            for (int i = 0; i < subMenus.Count; i++)
                AddElement(entries, subMenus[i], categoryLabel, categoryName, depth: 1);
        }
        return entries;
    }

    private static void AddElement(List<Entry> entries, SI.Bindable.SIDropdownElement<SI.Bindable.PanelSubtabInfo> element,
        string path, string categoryName, int depth)
    {
        if (depth > MaxDepth) return;
        if (element == null)
        {
            Degrade.Note($"[NavigatorIndex] null subtab element under \"{path}\"; emitting marker.");
            entries.Add(Marker(depth, path, categoryName));
            return;
        }

        Entry entry;
        try
        {
            var info = element.PropertyValue;
            var label = info.TranslationString;
            if (string.IsNullOrWhiteSpace(label)) label = element.DisplayValue.TranslatedString;
            // Object-bound entries (competitions, teams) arrive as inline entity-link tokens;
            // decode to the display segment the game itself renders, then normal display cleaning.
            label = TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(label));
            if (string.IsNullOrWhiteSpace(label))
            {
                // The live model reserves blank, condition-off rows for optional squads and
                // competitions. They have no destination yet (uid may be shared, object id -1),
                // so exposing them as unknown controls creates dead tree items. If the game later
                // enables one, it no longer matches this branch and is rendered normally.
                if (NavigatorEntryPolicy.IsInactivePlaceholder(info.Condition, label))
                    return;

                Degrade.Note($"[NavigatorIndex] subtab under \"{path}\" (uid {info.PanelUID}) has no readable label; emitting marker.");
                entries.Add(Marker(depth, path, categoryName));
                return;
            }
            entry = new Entry
            {
                Label = label,
                Path = path,
                PanelUid = info.PanelUID,
                ObjectUniqueId = info.ObjectUniqueID,
                Depth = depth,
                Disabled = element.Disable,
                ConditionMet = info.Condition,
                CategoryName = categoryName,
            };
        }
        catch (Exception e)
        {
            Degrade.Note($"[NavigatorIndex] subtab under \"{path}\" unreadable: {e.GetType().Name}: {e.Message}");
            entries.Add(Marker(depth, path, categoryName));
            return;
        }
        entries.Add(entry);

        var children = element.ChildList;
        if (children == null) return;
        for (int i = 0; i < children.Count; i++)
            AddElement(entries, children[i], path + " > " + entry.Label, categoryName, depth + 1);
    }

    private static Entry Marker(int depth, string path, string category) => new()
    {
        Label = Degrade.UnknownControl,
        Path = path,
        PanelUid = 0,
        ObjectUniqueId = -1,
        Depth = depth,
        Disabled = false,
        ConditionMet = false,
        CategoryName = category,
    };

    /// <summary>Readable dump for GET /navigator/list. Main thread only.</summary>
    internal static string DescribeText()
    {
        List<Entry> entries;
        try { entries = Build(); }
        catch (Exception e)
        {
            return $"navigator index failed: {e.GetType().Name}: {e.Message}\n";
        }
        if (entries == null) return "navigation module or map unavailable\n";

        var sb = new System.Text.StringBuilder();
        sb.Append("current: ").Append(DescribeCurrentTab()).Append('\n');
        foreach (var e in entries)
        {
            sb.Append(new string(' ', e.Depth * 2));
            sb.Append(e.Label);
            sb.Append("  [uid=").Append(e.PanelUid);
            if (e.ObjectUniqueId != -1) sb.Append(" obj=").Append(e.ObjectUniqueId);
            sb.Append(']');
            if (e.Depth == 0) sb.Append("  (").Append(e.CategoryName).Append(')');
            if (e.Disabled) sb.Append("  DISABLED");
            if (!e.ConditionMet) sb.Append("  CONDITION-OFF");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // Main tab asset name -> the /nav ribbon target whose click path navigates categories.
    // Categories never go through OpenSubtabEvent: dispatching a main-tab PanelID as SubtabPanelID
    // hard-crashed the game natively in the phase-0 probe (docs/navigator-plan.md).
    private static readonly Dictionary<string, string> CategoryRibbonTargets = new()
    {
        ["PortalScreen"] = "portal",
        ["SquadScreen"] = "squad",
        ["RecruitmentScreen"] = "recruitment",
        ["OnThePitchScreen"] = "matchday",
        ["ClubScreen"] = "club",
        ["CareerScreen"] = "career",
        ["TacticalPlannerTool"] = "tactics",
        ["TrainingScreen"] = "training",
    };

    /// <summary>
    /// Navigate to a destination from Build()'s tree, addressed by (PanelUID, ObjectUniqueID).
    /// Re-resolves the entry live before acting — the model may have changed since the caller's
    /// render, so a vanished or unavailable entry returns an honest error instead of misfiring.
    /// Marshals to the main thread itself (never call from inside another Invoke).
    /// </summary>
    internal static string Go(string uidStr, string objStr)
    {
        if (!uint.TryParse(uidStr, out uint uid)) return "Bad or missing uid.";
        int obj = int.TryParse(objStr, out int o) ? o : -1;
        try
        {
            return Navigate(uid, obj, false);
        }
        catch (Exception ex) { return ServerText.Failure("Navigation failed.", ex); }
    }

    /// <summary>
    /// Navigate to a subtab by PanelUID alone, taking whatever ObjectUniqueID the live entry
    /// carries — First Team's is the current club's id, which is save-specific, so a fixed
    /// shortcut can never hardcode it. The Alt-digit shortcuts' path: the same live re-resolve,
    /// availability checks, and OpenSubtabEvent dispatch as Go().
    /// </summary>
    internal static string GoToUid(uint uid)
    {
        try
        {
            return Navigate(uid, -1, true);
        }
        catch (Exception ex) { return ServerText.Failure("Navigation failed.", ex); }
    }

    private sealed record PendingAliasTab(SI.Bindable.PanelID Destination, int ObjectId,
        SI.Bindable.Events.OpenPanelEventContext Opening);

    private static string Navigate(uint uid, int obj, bool anyObj)
    {
        long generation = System.Threading.Interlocked.Increment(ref _navigationRequest);
        NativeDestinationTab localTab = null;
        try
        {
            uint parentCategory = 0;
            PendingAliasTab aliasTab = null;
            string result = MainThreadDispatcher.Invoke(() => GoOnMainThread(uid, obj, anyObj,
                true, out localTab, out parentCategory, out aliasTab));
            if (parentCategory != 0)
            {
                // FM27's OnOpenSubtabEvent does not consume MainTabPanelID.
                // Enter the actual owning category once, then wait for its
                // observed native selection before dispatching the subtab.
                // Otherwise an identical UID in another team can retain that
                // team's opening scope. Never rearm/replay the category action.
                long deadline = Environment.TickCount64 + 5000;
                while (!MainThreadDispatcher.Invoke(() =>
                {
                    if (generation != System.Threading.Volatile.Read(ref _navigationRequest))
                        throw new InvalidOperationException("A newer navigation request replaced this destination.");
                    return BookmarkBar.Module()?.m_currentMainTab.Value?.ID == parentCategory;
                }))
                {
                    if (Environment.TickCount64 >= deadline)
                        throw new TimeoutException("The destination's native category is not ready. Choose it again.");
                    System.Threading.Thread.Sleep(16);
                }
                result = MainThreadDispatcher.Invoke(() =>
                {
                    if (generation != System.Threading.Volatile.Read(ref _navigationRequest))
                        throw new InvalidOperationException("A newer navigation request replaced this destination.");
                    return GoOnMainThread(uid, obj, anyObj, false, out localTab, out _, out aliasTab);
                });
            }
            if (aliasTab != null)
            {
                // A proxy can open a shared panel asynchronously without
                // applying its local-tab argument on the first visit. Observe
                // that actual destination before resolving the same native
                // argument used by the already-open path. Never reopen/replay.
                long deadline = Environment.TickCount64 + 5000;
                while (localTab == null)
                {
                    localTab = MainThreadDispatcher.Invoke(() =>
                    {
                        if (generation != System.Threading.Volatile.Read(ref _navigationRequest))
                            throw new InvalidOperationException("A newer navigation request replaced this destination.");
                        var module = BookmarkBar.Module();
                        return module?.IsSameAsCurrentSubtab(aliasTab.Destination, aliasTab.ObjectId) == true
                            ? NativeDestinationTab.Prepare(module, aliasTab.Destination, aliasTab.ObjectId, aliasTab.Opening)
                            : null;
                    });
                    if (localTab != null) break;
                    if (Environment.TickCount64 >= deadline)
                        throw new TimeoutException("The destination's native local-tab controls are not ready. Choose it again.");
                    System.Threading.Thread.Sleep(16);
                }
            }
            if (localTab == null) return result;
            int value = localTab.WaitForValue();
            string baseline = SseStreamer.CaptureShapeBaseline();
            bool changed = MainThreadDispatcher.Invoke(() =>
            {
                if (generation != System.Threading.Volatile.Read(ref _navigationRequest))
                    throw new InvalidOperationException("A newer navigation request replaced this destination.");
                return localTab.Activate(value);
            });
            // Local native tabs keep the panel UID. Reuse ordinary /activate's
            // bounded shape-settle contract so late session rows reach HTML.
            if (changed) SseStreamer.ArmSettleAfterAction(baseline);
            return result;
        }
        finally
        {
            if (localTab != null) MainThreadDispatcher.Invoke(() => { localTab.Close(); return true; });
        }
    }

    private static string GoOnMainThread(uint uid, int obj, bool anyObj, bool allowCategory,
        out NativeDestinationTab localTab, out uint parentCategory, out PendingAliasTab aliasTab)
    {
        localTab = null;
        parentCategory = 0;
        aliasTab = null;
        NativeDestinationTab.RememberHistorySelection();
        // Navigating away replaces whatever modal interaction was in flight (the same hygiene
        // as ribbon/history/bookmark); the re-render after the screen change shows the
        // destination, not a stale mod page over it.
        DropdownSessionManager.CloseCurrentOnMainThread();
        NavigatorSurface.CloseIfOpen();
        BookmarkEditorSurface.CloseIfOpen();
        SoundCuesSurface.CloseIfOpen();
        FilterPopupSurface.CloseIfOpen();
        MessageViewSurface.CloseIfOpen();
        var module = BookmarkBar.Module();
        var tabs = module?.m_mainNavigationMap?.m_map;
        if (tabs == null) return "Navigation model unavailable.";

        // Category tier: the proven ribbon click path.
        for (int t = 0; t < tabs.Length; t++)
        {
            var pid = tabs[t]?.PanelID;
            if (pid == null || pid.ID != uid) continue;
            if (!CategoryRibbonTargets.TryGetValue(pid.name, out var target))
                return $"No ribbon target known for category {pid.name}.";
            return ServerHandles.ActivateRibbonButton(target);
        }

        // Subsection / dropdown-child tier: re-resolve live, then dispatch the game's own
        // OpenSubtabEvent (the TacticsPitchPositionEvent pattern) with the real subtab PanelID.
        for (int t = 0; t < tabs.Length; t++)
        {
            var subMenus = tabs[t]?.PanelSubTabsList?.SubMenus;
            if (subMenus == null) continue;
            for (int i = 0; i < subMenus.Count; i++)
            {
                var found = FindElement(subMenus[i], uid, obj, anyObj, depth: 1, out bool secondaryTab);
                if (found == null) continue;

                var info = found.PropertyValue;
                var label = TextFilters.CleanForDisplay(
                    ServerText.DecodeInlineEntityLinks(info.TranslationString));
                if (found.Disable) return $"{label} is disabled right now.";
                if (!info.Condition) return $"{label} is not available right now.";

                var owningCategory = tabs[t].PanelID;
                // Only a reused panel UID crossing entity/category scopes needs
                // this boundary. Ordinary direct destinations/shortcuts retain
                // their FM26 one-entry history instead of visiting a category
                // landing page first (notably Alt+1 from a club profile).
                bool reusedPanelForAnotherEntity = module.m_currentSecondaryTab.Value?.ID == info.PanelUID
                    && module.m_currentSecondaryTabObjectUniqueID != info.ObjectUniqueID;
                if (reusedPanelForAnotherEntity && module.m_currentMainTab.Value?.ID != owningCategory.ID)
                {
                    if (!allowCategory)
                        return "The destination's native category changed. Choose it again.";
                    if (!CategoryRibbonTargets.TryGetValue(owningCategory.name, out var categoryTarget))
                        return $"No ribbon target known for category {owningCategory.name}.";
                    string categoryResult = ServerHandles.ActivateRibbonButton(categoryTarget);
                    if (categoryResult?.StartsWith("Opening ", StringComparison.Ordinal) == true)
                        parentCategory = owningCategory.ID;
                    return categoryResult;
                }

                var subtabPid = LoadSubtabPanelId(info);
                if (subtabPid == null)
                    return Degrade.Value($"[NavigatorIndex] PanelID for \"{label}\" (uid {uid}) failed to load.");

                var openingDestination = NativeDestinationTab.OpeningDestination(subtabPid, info.OpenEvent);
                if (module.IsSameAsCurrentSubtab(openingDestination, info.ObjectUniqueID))
                {
                    localTab = NativeDestinationTab.Prepare(module, openingDestination, info.ObjectUniqueID, info.OpenEvent);
                    if (localTab != null) return "Opening " + label;
                }

                var events = SI.Core.GameSubsystems.Get<SI.Core.EventSubsystem>();
                if (events == null) return "Event system unavailable.";

                var evt = FM.UI.OpenSubtabEvent.GetPooled();
                evt.SubtabPanelID = subtabPid;
                evt.MainTabPanelID = tabs[t].PanelID;
                // The found entry's own object id, not the caller's: identical on an exact-match
                // resolve, and the live club/entity id on a uid-only (anyObj) resolve.
                evt.ObjectUniqueID = info.ObjectUniqueID;
                // Pooled events must carry this destination's actual tier.
                // A child of a primary subtab belongs to its secondary choice;
                // otherwise native navigation can retain the parent's default.
                evt.SecondaryTab = secondaryTab;
                events.SendEvent(evt);
                if (openingDestination.ID != subtabPid.ID && info.OpenEvent?.m_argumentBindings != null)
                    aliasTab = new(openingDestination, info.ObjectUniqueID, info.OpenEvent);
                return "Opening " + label;
            }
        }
        return "Destination not found; the screen set may have changed.";
    }

    private static SI.Bindable.SIDropdownElement<SI.Bindable.PanelSubtabInfo> FindElement(
        SI.Bindable.SIDropdownElement<SI.Bindable.PanelSubtabInfo> element, uint uid, int obj, bool anyObj, int depth,
        out bool secondaryTab)
    {
        secondaryTab = false;
        if (element == null || depth > MaxDepth) return null;
        try
        {
            var info = element.PropertyValue;
            if (info.PanelUID == uid && (anyObj || info.ObjectUniqueID == obj))
            {
                secondaryTab = depth > 1;
                return element;
            }
        }
        catch (Exception e)
        {
            Degrade.Note($"[NavigatorIndex] element unreadable during Go resolve: {e.GetType().Name}: {e.Message}");
            return null;
        }
        var children = element.ChildList;
        if (children == null) return null;
        for (int i = 0; i < children.Count; i++)
        {
            var hit = FindElement(children[i], uid, obj, anyObj, depth + 1, out bool childSecondary);
            if (hit != null) { secondaryTab = childSecondary; return hit; }
        }
        return null;
    }

    // The BookmarkBar.LoadPanelId pattern, against the subtab's backing field (the PanelIDRef
    // property is typed as the non-generic AssetReference through interop; the field keeps the
    // AssetReferenceT<PanelID> the load needs). Null when the reference is empty or the load
    // fails — the caller degrades, never navigates to a guessed panel.
    private static SI.Bindable.PanelID LoadSubtabPanelId(SI.Bindable.PanelSubtabInfo info)
    {
        var refT = info.m_panelIDRef;
        if (refT == null) return null;
        var asset = refT.Asset;
        if (asset != null) return asset.TryCast<SI.Bindable.PanelID>();
        return refT.LoadAssetAsync().WaitForCompletion();
    }

    private static string DescribeCurrentTab()
    {
        try
        {
            // The backing field, not the CurrentMainTab property: the interop for that
            // ref-returning property throws ObjectCollectedException (the m_currentSelectedBookmarksList
            // field-access pattern in BookmarkBar is the one that works).
            var module = BookmarkBar.Module();
            var current = module?.m_currentMainTab.Value;
            return current == null ? "<none>" : current.name;
        }
        catch (Exception e)
        {
            Degrade.Note($"[NavigatorIndex] CurrentMainTab unreadable: {e.GetType().Name}: {e.Message}");
            return Degrade.Unavailable;
        }
    }
}
