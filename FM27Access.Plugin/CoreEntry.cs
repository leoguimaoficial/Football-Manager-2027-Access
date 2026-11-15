using FM27Access.Host;
using FM27Access.Navigation;
using FM27Access.Navigation.Profiles;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Contexts;
using FM27Access.Plugin.Patches;
using HarmonyLib;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

/// <summary>
/// The reloadable core entry point. The permanent host (FM27Access.Host) loads this assembly and
/// calls <see cref="Mount"/> once at startup and again after every POST /reload (each time on a
/// fresh copy of the assembly). All mod logic lives below here — Harmony patches, the navigation
/// wiring, the HTTP server, the dev endpoints — so it can be swapped without restarting the game.
/// The four un-reloadable resources (IL2CPP injection, Prism, the companion, Speech init) stay in
/// the host. See docs/hot-reload-plan.md.
/// </summary>
public static class CoreEntry
{
    private static readonly ClassificationCache Cache = new();
    private static IHostBridge _bridge;
    private static Harmony _harmony;
    private static CoreLoop _loop;

    public static void Mount(IHostBridge bridge)
    {
        _bridge = bridge;
        // The version identifies which release a field Player.log came from — without it a shared
        // log cannot be matched to the fixes it does or does not contain. Release builds carry
        // "X.Y.Z+commit" (stamped by package.ps1); local builds read as a bare assembly version.
        var asm = typeof(CoreEntry).Assembly;
        var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        var version = info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
        Speech.Log.Info($"[CoreEntry] Mounting core (firstMount={bridge.IsFirstMount}, version={version}).");

        // Create Harmony instance for patching
        _harmony = new Harmony("com.fm27access.mod");

        // The per-frame loop, driven by the host's injected behaviour via the bridge tick.
        _loop = new CoreLoop(isReload: !bridge.IsFirstMount);

        // Safety net: Kill() unpatches and pauses the core (the server is left up so /reload can recover).
        AccessibilityMod.Init(_harmony, Pause);

        // Start the server and wire the dev-driver ring buffers BEFORE the wiring and patches that can
        // throw. If a later Mount step fails, the server is already up so /reload (AlwaysReady) can load
        // a fix without a game restart, and /log (also AlwaysReady) shows why Mount failed. Normal
        // routes still serve their loading body until the dispatcher is Ready, so starting early is safe.
#if FM27_DEVELOPER_TOOLS
        Speech.SpeechPipeline.Observer = _bridge.SpeechHistory.Add;
        Server.Dev.DevEndpoints.Attach(_bridge.SpeechHistory, _bridge.LogHistory);
#endif
        Server.RuntimeSurface.Configure(_bridge.CompanionSessionToken,asm.ManifestModule.ModuleVersionId.ToString());
        Server.AccessibilityServer.Start();

        // Wire the navigation seam: one host object, built in full and handed over. A capability
        // missing here that the core requires is a compile error, and a reload drops the whole
        // host at Unmount — there is no per-delegate reset list to keep in sync.
        NavigationManager.Initialize(new NavigationHost
        {
            Speak = text =>
            {
                // The arrival quiet window mutes navigation announcements (panel entries,
                // restores) whose call sites wrapped themselves in NavigationSpeechMute.Run —
                // the arrival report speaks the landing instead. Never silent: every dropped
                // line is logged.
                var announcement = NavigationSpeechMute.Resolve(text);
                if (announcement == null)
                {
                    Speech.Log.Info($"[ArrivalQuiet] suppressed: \"{text}\"");
                    return;
                }
                Speech.SpeechPipeline.SpeakInterrupt(announcement);
            },

            Stops = new NavigationHost.StopFinding
            {
                FindFirst = panel =>
                {
                    Cache.Clear();
                    var panelElement = (SI.Bindable.Panel)panel;
                    var screenContext = new LinearContext(panelElement, Cache);
                    Cache.AttachContext(panelElement, screenContext);
                    return TreeWalker.FindFirstStop(panelElement, Cache);
                },
                FindLast = panel =>
                {
                    Cache.Clear();
                    var panelElement = (SI.Bindable.Panel)panel;
                    return TreeWalker.FindLastStopInContainer(panelElement, Cache);
                },
                FindNext = (current, direction) =>
                {
                    var element = ((PluginAdapterBase)current).BackingElement;
                    var boundary = (VisualElement)NavigationManager.GetActivePanel();
                    return direction == NavigationDirection.Up
                        ? TreeWalker.FindPreviousStopBounded(element, boundary, Cache)
                        : TreeWalker.FindNextStopBounded(element, boundary, Cache);
                },
                FindNextAfterContainer = (scope, direction) =>
                {
                    var container = (VisualElement)scope;
                    var boundary = (VisualElement)NavigationManager.GetActivePanel();
                    return direction == NavigationDirection.Up
                        ? TreeWalker.FindPreviousStopBounded(container, boundary, Cache)
                        : TreeWalker.FindNextStopBounded(container, boundary, Cache);
                },
                FindByAnnouncement = (panel, announcement, index) =>
                {
                    var panelElement = (SI.Bindable.Panel)panel;
                    return TreeWalker.FindStopByAnnouncement(panelElement, Cache, announcement, index);
                },
                AnnouncementOccurrenceIndex = (panel, element, announcement) =>
                {
                    var panelElement = (SI.Bindable.Panel)panel;
                    var backing = ((PluginAdapterBase)element).BackingElement;
                    return TreeWalker.GetOccurrenceIndex(panelElement, Cache, backing, announcement);
                },
                EnsureContext = current =>
                {
                    var element = ((PluginAdapterBase)current).BackingElement;
                    TreeWalker.EnsureAncestorContexts(element, Cache);
                },
                IsDetached = current =>
                {
                    var element = ((PluginAdapterBase)current).BackingElement;
                    return element.panel == null;
                },
                IsInActivePanel = current =>
                {
                    var activePanel = NavigationManager.GetActivePanel();
                    if (activePanel == null) return false;
                    var element = ((PluginAdapterBase)current).BackingElement;
                    return TreeWalker.IsDescendantOf(element, (VisualElement)activePanel);
                },
            },

            Contexts = new NavigationHost.ContextLookup
            {
                FindContext = current =>
                {
                    var element = ((PluginAdapterBase)current).BackingElement;
                    return Cache.FindContextForElement(element);
                },
                FindParentContext = context =>
                {
                    var container = (VisualElement)context.Scope;
                    var parent = container.hierarchy.parent;
                    while (parent != null)
                    {
                        var parentContext = Cache.GetContext(parent);
                        if (parentContext != null)
                            return parentContext;
                        parent = parent.hierarchy.parent;
                    }
                    return null;
                },
            },

            // Canonical order — it fixes both the transition-prefix composition and the
            // entry-behavior sequence. A new container kind is one registration here plus
            // its IContainerContext; the navigation core is not edited.
            ContainerKinds = new[]
            {
                new ContainerKind
                {
                    Name = ContainerKind.Table,
                    EnterAnnouncement = "table",
                    ExitAnnouncement = "out of table",
                    FindContext = current => FindAncestorContext<Contexts.TableContext>(current),
                    EntryBehavior = ContainerEntry.None,
                },
                new ContainerKind
                {
                    Name = ContainerKind.TabGroup,
                    EnterAnnouncement = "tab selection",
                    ExitAnnouncement = "out of tab selection",
                    FindContext = current => FindAncestorContext<Contexts.TabContext>(current),
                    EntryBehavior = ContainerEntry.WrapOrEntry,
                },
                new ContainerKind
                {
                    Name = ContainerKind.ButtonBar,
                    EnterAnnouncement = "button bar",
                    ExitAnnouncement = "out of button bar",
                    FindContext = current => FindAncestorContext<Contexts.ButtonBarContext>(current),
                    EntryBehavior = ContainerEntry.EntryUnlessInside,
                },
                new ContainerKind
                {
                    Name = ContainerKind.Calendar,
                    EnterAnnouncement = "calendar",
                    ExitAnnouncement = "out of calendar",
                    FindContext = current => FindAncestorContext<Contexts.CalendarContext>(current),
                    EntryBehavior = ContainerEntry.FirstChildOnDownUnlessInside,
                },
                new ContainerKind
                {
                    Name = "message list",
                    EnterAnnouncement = "message list",
                    ExitAnnouncement = "out of message list",
                    FindContext = current => FindAncestorContext<Contexts.MessagesListContext>(current),
                    EntryBehavior = ContainerEntry.None,
                },
                new ContainerKind
                {
                    // Any other under-realised streamed list (ElementBridge promotes it to a
                    // Section with StreamedListContext). The Messages subclass is claimed by the
                    // "message list" kind above, so it is excluded here.
                    Name = "list",
                    EnterAnnouncement = "list",
                    ExitAnnouncement = "out of list",
                    FindContext = current =>
                        FindAncestorContext<Contexts.StreamedListContext>(current) is
                            not Contexts.MessagesListContext and { } ctx ? ctx : null,
                    EntryBehavior = ContainerEntry.None,
                },
            },

            Panels = new NavigationHost.PanelAccess
            {
                GetIdentity = panel =>
                {
                    var p = (SI.Bindable.Panel)panel;
                    return p.PanelID.ID.ToString();
                },
                // Native-pointer identity: Harmony can deliver the same native panel as different
                // managed wrappers across callbacks, so wrapper references cannot answer "is this
                // the panel I stored?". A collected wrapper throws on Pointer; that panel is gone,
                // so "different" is the honest answer.
                IsSameInstance = (a, b) =>
                {
                    try
                    {
                        return ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)a).Pointer
                            == ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)b).Pointer;
                    }
                    catch (Exception e)
                    {
                        Speech.Log.Warn($"[CoreEntry] panel pointer comparison failed, treating as different: {e.GetType().Name}");
                        return false;
                    }
                },
                GetName = panel =>
                {
                    var p = (SI.Bindable.Panel)panel;
                    return p.PanelID.name ?? "";
                },
                OnActivePanelChanged = panelName =>
                {
                    ProfileRegistry.ActivateForPanel(panelName);
                },
                NavigableLayers = new HashSet<int>(PanelObservationPolicy.NavigableLayers),
                GetAllOpen = () =>
                {
                    var result = new List<OpenPanel>();
                    var pm = PanelManager.Instance;
                    var layers = FM27PanelCatalog.GetLayers(pm);
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var layerInfo = layers[i];
                        var openPanels = layerInfo.Open;
                        for (int j = 0; j < openPanels.Count; j++)
                        {
                            var panel = openPanels[j];
                            var title = panel.PanelID.Title.TranslatedString;
                            if (string.IsNullOrEmpty(title))
                                title = panel.PanelID.name ?? $"Layer {i}";
                            result.Add(new OpenPanel(panel, i, title));
                        }
                    }
                    return result;
                },
                FindContinueButton = ContinueButtonLocator.Find,
                NavigateBack = () =>
                {
                    var pm = PanelManager.Instance;
                    var layers = FM27PanelCatalog.GetLayers(pm);
                    // FM27 keeps the covered title bar alive below cards.
                    // Close the actual foreground card before considering a
                    // BackButton from that background's navigation history.
                    var foreground = FM27PanelCatalog.GetForegroundPanel();
                    if (foreground != null && layers.Skip(FM27PanelCatalog.SemanticLayer("Card"))
                        .Any(layer => layer.Open.Any(panel => panel.Pointer == foreground.Pointer)))
                        return FM27PanelCatalog.TryCloseForeground(pm) ? null : "back not available";
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var openPanels = layers[i].Open;
                        for (int j = 0; j < openPanels.Count; j++)
                        {
                            var panel = openPanels[j];
                            var btn = panel.Q<SI.Bindable.SIButton>("BackButton");
                            if (btn != null && ElementBridge.IsVisible(btn))
                            {
                                if (!btn.enabledInHierarchy)
                                    return "back not available";
                                Patches.PanelHistoryBackPatch.AllowNextBackEvent = true;
                                btn.SimulateButtonClick();
                                return null;
                            }
                        }
                    }
                    // FM27 can host a closable foreground card without a title-bar BackButton.
                    // Use its native layer policy and close API, never the covered background.
                    return FM27PanelCatalog.TryCloseForeground(pm)?null:"back not available";
                },
                NavigateForward = () =>
                {
                    var pm = PanelManager.Instance;
                    var layers = FM27PanelCatalog.GetLayers(pm);
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var openPanels = layers[i].Open;
                        for (int j = 0; j < openPanels.Count; j++)
                        {
                            var panel = openPanels[j];
                            var btn = panel.Q<SI.Bindable.SIButton>("ForwardButton");
                            if (btn != null && ElementBridge.IsVisible(btn))
                            {
                                if (!btn.enabledInHierarchy)
                                    return "forward not available";
                                btn.SimulateButtonClick();
                                return null;
                            }
                        }
                    }
                    return "forward not available";
                },
            },

            Profile = new NavigationHost.ProfilePolicy
            {
                IsHeading = IsHeadingStop,
                RenameLabel = current =>
                {
                    if (current is not PluginAdapterBase pab) return null;

                    // The Match Detail Level card carries a game mislabel: both bulk-set labels
                    // read "Set International Competition Detail Levels To", but the second one
                    // labels the set-ALL-competitions dropdown (its sibling container:
                    // set-all-competition-detail-levels, vs set-all-international-detail-levels
                    // beside the first). Correct the second so the two heavyweight bulk actions
                    // are distinguishable; a profile rule cannot split the twin texts.
                    var el = pab.BackingElement;
                    if (el.name == "text"
                        && ElementBridge.HasSiblingNamed(el, "set-all-competition-detail-levels")
                        && ElementBridge.IsInPanel(el, "MatchDetailLevel"))
                        return "Set All Competition Detail Levels To";

                    var wizardStep = TacticWizardForwardLabel(pab);
                    if (wizardStep != null) return wizardStep;

                    var profile = OriginalProfiles.ForElement(el);
                    if (profile == null) return null;
                    return profile.TryGetRename(BuildMatchInfo(el));
                },
                IsHidden = current =>
                {
                    if (current is not PluginAdapterBase pab) return false;
                    if (PairedEntityLink.IsRedundantIcon(pab.BackingElement)) return true;
                    if (current.Role == "image" && SpriteHelper.IsCaptionedGenericCompetitionBadge(pab.BackingElement)) return true;
                    var profile = OriginalProfiles.ForElement(pab.BackingElement);
                    if (PairedEntityLink.IsRedundantCaption(current)
                        && (profile == null || profile.TryGetHeadingLevel(BuildMatchInfo(pab.BackingElement)) == 0)) return true;
                    if (profile == null) return false;
                    return profile.IsHidden(BuildMatchInfo(pab.BackingElement));
                },
                HeadingLevel = current =>
                {
                    if (current is not PluginAdapterBase pab) return 0;
                    var profile = OriginalProfiles.ForElement(pab.BackingElement);
                    if (profile == null) return 0;
                    return profile.TryGetHeadingLevel(BuildMatchInfo(pab.BackingElement));
                },
            },

            CursorRestoreExcludedPanels = new HashSet<string> { "2208093440" }, // PlayerReport
        });

        // Apply Harmony patches — PatchOrDie ensures we crash if a target is missing
        // OnPanelLoadedInternal carries Layer by value. Even a postfix that
        // omits Layer makes Harmony marshal it in its original-call trampoline.
        // Observe the native Panel-only opened event instead (metadata 39).
        PatchOrDie(_harmony, typeof(PanelClosedPatch));
        PatchOrDie(_harmony, typeof(NotificationPatch));
        PatchOrDie(_harmony, typeof(PanelHistoryBackPatch));
        PatchOrDie(_harmony, typeof(ContinueStatePatch));
        PatchOrDie(_harmony, typeof(CascadingMenuSetCurrentFolderGuard));
        PatchOrDie(_harmony, typeof(CascadingMenuPopupShowInternalGuard));
        PatchOrDie(_harmony, typeof(ContextMenuItemsReceivedGuard));
        PatchOrDie(_harmony, typeof(ContextMenuManipulatorRegisterPatch));
        PatchOrDie(_harmony, typeof(ContextMenuManipulatorUnregisterPatch));

        // Load panel layout profiles
        var profileDir = Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", "panel-profiles");
        OriginalProfiles.Load(asm);
        ProfileLoader.LoadAll(profileDir);

        // Start driving the per-frame loop. Last, so the tick only runs once Mount has fully
        // succeeded; a throw anywhere above leaves the server up for /reload but never starts ticking.
        _bridge.Tick = _loop.Tick;

        Speech.Log.Info("[CoreEntry] Core mounted.");
    }

    private static bool IsHeadingStop(IAccessibleElement current)
    {
        // A synthesized stop with no backing element (e.g. a data-model context-menu entry) is
        // never a section heading and carries no profile rules.
        if (current is not PluginAdapterBase pab) return false;
        // Commands remain commands. Section headings are informational; a
        // native title inside a button does not give the control heading role.
        if (current.Role == "button") return false;
        var element = pab.BackingElement;

        // Generic, profile-independent rule: in the tiles-and-cards pattern a section
        // title is an SIText whose element name is "title-text" or a context-prefixed
        // variant ending "-title-text" (the player report's "player-pos-title-text" =
        // "Positions"). Promote it to a heading on every screen, regardless of any active
        // panel profile, but only when it actually carries text — an empty title-text tile
        // must stay a non-heading (failure mode is "missing heading", never an empty one).
        // A bad read degrades to "not a heading"; never a wrong heading. The "-title-text"
        // suffix is matched with its leading hyphen so "subtitle-text" (a subtitle, not a
        // heading) is excluded, and a navigation tab's title ("navigation-tab-title-text")
        // is excluded structurally — a nav tab is not a section heading.
        if (IsSectionTitleName(element.name ?? "") && !IsInsideNavigationFocusser(element))
        {
            try { if (!string.IsNullOrWhiteSpace(current.GetLabel())) return true; }
            catch { return false; }
        }

        // A2 fallback: some tiles hide their "title area frame" and leave "title-text" empty,
        // putting the section/column heading as the leading text of a two-column layout (the
        // Sounds / Graphics Quality tile on Preferences). Recover those headings — but narrowly,
        // so a control label is never promoted in error. Failure mode stays "missing heading".
        if (IsHeaderlessTileColumnHeading(element))
        {
            try { if (!string.IsNullOrWhiteSpace(current.GetLabel())) return true; }
            catch { return false; }
        }

        // A3: the Scouting Assignments master-detail titles its right pane only by the selected
        // scout's name, whose SIText is structurally identical to the role text beside it — no
        // profile matcher can single it out (same name, type, path, parent, and child index).
        // The one live difference is the game's embedded person link on the name. Scoped to the
        // panel so linked names in other person cells are never promoted by accident.
        if (IsAssignmentsDetailPersonName(element))
        {
            try { if (!string.IsNullOrWhiteSpace(current.GetLabel())) return true; }
            catch { return false; }
        }

        // A4: the Match Detail Level card titles its right pane by the selected competition
        // group ("International (Men)"), a bare SIText whose only structural mark is the
        // streamed competition list beside it — every named ancestor within matcher reach is a
        // generic scroll container. Scoped to the panel so list-adjacent texts elsewhere are
        // never promoted.
        if (IsMatchDetailGroupTitle(element))
        {
            try { if (!string.IsNullOrWhiteSpace(current.GetLabel())) return true; }
            catch { return false; }
        }

        var profile = OriginalProfiles.ForElement(element);
        if (profile == null) return false;
        return profile.IsHeading(BuildMatchInfo(element));
    }

    /// <summary>
    /// Tear the core down so the host can load a fresh copy. Order matters: stop being ticked, then
    /// restore game input, stop the server (frees the socket for the next Mount), unpatch Harmony
    /// (must happen before the assembly is discarded, or dangling trampolines crash), and drop the
    /// permanent NavigationManager's host + state so nothing dangles into this stale assembly.
    /// </summary>
    public static void Unmount()
    {
        Speech.Log.Info("[CoreEntry] Unmounting core...");
        try { Server.FilterPopupSurface.CloseIfOpen(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreEntry] Popup close during reload failed: {ex.Message}"); }
        // These popups were opened by this core and retain its native adapters.
        // Close through the existing owner lifecycle before stopping the pump;
        // never leave callbacks or a visible popup owned by the discarded core.
        try { Server.DropdownSessionManager.CloseCurrentOnMainThread(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreEntry] Dropdown close during reload failed: {ex.Message}"); }

        // Requests already harvesting a table can outlive the listener. Stop
        // their dispatcher before removing its tick, so they cannot keep
        // queuing work against this discarded assembly after a reload.
        Server.MainThreadDispatcher.Stopped = true;
        if (_bridge != null) _bridge.Tick = null;

        try { _loop?.Shutdown(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreEntry] Loop shutdown failed: {ex.Message}"); }
        _loop = null;

        try { Server.AccessibilityServer.Stop(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreEntry] Server stop failed: {ex.Message}"); }

        // Assembly.Load(byte[]) does not unload the preceding core. Its static
        // registries otherwise keep native form/table adapters alive after the
        // server and NavigationManager have moved to the replacement assembly.
        // Drop only our managed registrations; do not reset native providers,
        // close business-value channels or invoke any game command.
        Cache.Clear();
        Server.ServerHandles.Clear();
        Server.LiveValues.Clear();

        // A failed unpatch is crash-affecting, not cosmetic: the next Mount would load fresh code while
        // stale patch trampolines still point into this discarded assembly. Log at Fatal so the
        // evidence is unmissable (the reload proceeds — there is no safe way to un-leave a patch).
        try { _harmony?.UnpatchSelf(); }
        catch (Exception ex) { Speech.Log.Fatal($"[CoreEntry] Harmony unpatch failed during reload; stale trampolines may remain: {ex}"); }
        _harmony = null;

        NavigationManager.ResetForReload();

        _bridge = null;
        Speech.Log.Info("[CoreEntry] Core unmounted.");
    }

    /// <summary>
    /// Ask the host to reload the core. Called by the POST /reload dev endpoint from a server
    /// thread; the host runs Unmount → load fresh bytes → Mount on the main thread, off this thread,
    /// so patching never races live game code. See docs/hot-reload-plan.md.
    /// </summary>
    public static void RequestReload() => _bridge?.RequestReload();

    // AccessibilityMod.Kill() runs this to pause the core without a full reload: stop the per-frame
    // tick and restore game input (the patches are already removed by Kill itself). The server is
    // deliberately left running, unlike Unmount, so a fixed rebuild + POST /reload can still recover
    // without restarting the game: the /reload handler posts to the host pump, which keeps draining
    // even with the core tick stopped.
    private static void Pause()
    {
        // Recorded before the tick is dropped: from here nothing drains the main-thread hand-off, and
        // every route would otherwise wait out its budget and report a timeout. The surface answers
        // "the mod has stopped" instead — the one failure the mod knows for certain about itself.
        Server.MainThreadDispatcher.Stopped = true;
        if (_bridge != null) _bridge.Tick = null;
        try { _loop?.Shutdown(); } catch (Exception ex) { Speech.Log.Warn($"[CoreEntry] Pause loop shutdown failed: {ex.Message}"); }
    }

    private static T FindAncestorContext<T>(IAccessibleElement current) where T : class, IContainerContext
    {
        var element = ((PluginAdapterBase)current).BackingElement;
        var ctx = Cache.FindContextForElement(element);
        while (ctx != null)
        {
            if (ctx is T match)
                return match;
            var container = (VisualElement)ctx.Scope;
            var parent = container.hierarchy.parent;
            ctx = null;
            while (parent != null)
            {
                var parentContext = Cache.GetContext(parent);
                if (parentContext != null)
                {
                    ctx = parentContext;
                    break;
                }
                parent = parent.hierarchy.parent;
            }
        }
        return null;
    }

    // The selected scout's name in the assignments card's detail pane: a linked SIText inside
    // the large horizontal person header cell ("…combo-2-cell"), on that panel only. The role
    // text beside it shares the entire structure but carries no link, so it stays a non-heading.
    // Any failed read degrades to "not a heading" — never a wrong one.
    private static bool IsAssignmentsDetailPersonName(VisualElement element)
    {
        string type;
        try { type = element.GetIl2CppType().Name; } catch { return false; }
        if (type != "SIText") return false;

        bool inPersonCell = false;
        var current = element.hierarchy.parent;
        for (int i = 0; i < 6 && current != null; i++)
        {
            if ((current.name ?? "").Contains("combo-2-cell")) { inPersonCell = true; break; }
            current = current.hierarchy.parent;
        }
        if (!inPersonCell) return false;

        if (!ElementBridge.IsInPanel(element, "RecruitmentScoutAssignmentsCard")) return false;

        try { return ElementBridge.HasEmbeddedLink(element); }
        catch { return false; }
    }

    // The selected competition group's title in the Match Detail Level card: an SIText named
    // "text" whose sibling is the streamed per-competition list, on that panel only. Any failed
    // read degrades to "not a heading" — never a wrong one.
    private static bool IsMatchDetailGroupTitle(VisualElement element)
    {
        string type;
        try { type = element.GetIl2CppType().Name; } catch { return false; }
        if (type != "SIText" || element.name != "text") return false;

        var parent = element.hierarchy.parent;
        if (parent == null) return false;
        bool hasList = false;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            string siblingType;
            try { siblingType = parent.hierarchy[i].GetIl2CppType().Name; } catch { continue; }
            if (siblingType == "StreamedObjectList") { hasList = true; break; }
        }
        if (!hasList) return false;

        return ElementBridge.IsInPanel(element, "MatchDetailLevel");
    }

    // The create-a-tactic wizard labels its forward controls with the destination alone
    // ("Formation (In Possession)", "Formation (Out of Possession)"), so nothing says they move the
    // flow on rather than open a screen — and its back control's "Back" is indistinguishable from
    // the title bar's screen-history Back (a profile rename replaces that one outright, since its
    // text carries no destination). Here the destination text IS the information, and it is the
    // game's own localized string, so keep it and prefix the same "Advance to " the continue button
    // uses. Read live at announcement time, so a step change can never leave a stale destination.
    // Direction comes from the slot the button sits in: the header wraps each control in an
    // SIVisible named for the step it leads to, and ip-vis / oop-vis are the forward pair. Null for
    // everything else, leaving the declarative profile rules to run.
    private const string TacticWizardPanel = "TacticalPlannerTool";
    private const string AdvancePrefix = "Advance to ";
    private static readonly string[] TacticWizardForwardSlots = { "ip-vis", "oop-vis" };

    private static string TacticWizardForwardLabel(PluginAdapterBase adapter)
    {
        if (adapter.Role != "button") return null;
        var element = adapter.BackingElement;
        if (!IsInSlot(element, TacticWizardForwardSlots)) return null;
        if (!ElementBridge.IsInPanel(element, TacticWizardPanel)) return null;

        var text = element.TryCast<TextElement>()?.text;
        var destination = text == null ? null : Speech.TextFilters.CleanForDisplay(text);
        if (string.IsNullOrEmpty(destination)) return null;
        return AdvancePrefix + destination;
    }

    // Whether one of the named wrappers is a near ancestor. The header nests each button at most an
    // SIEnabled below its slot, so a shallow walk both finds it and keeps this off the hot path for
    // every other stop on every other panel.
    private const int SlotSearchLevels = 3;

    private static bool IsInSlot(VisualElement element, string[] slotNames)
    {
        var current = element.hierarchy.parent;
        for (int i = 0; i < SlotSearchLevels && current != null; i++, current = current.hierarchy.parent)
        {
            var name = current.name;
            if (name == null) continue;
            for (int j = 0; j < slotNames.Length; j++)
                if (name == slotNames[j]) return true;
        }
        return false;
    }

    private static string BuildAncestorPath(VisualElement element, int levels)
    {
        var parts = new List<string>(levels);
        var current = element.hierarchy.parent;
        for (int i = 0; i < levels && current != null; i++)
        {
            var typeName = current.GetIl2CppType().Name;
            var name = current.name ?? "";
            parts.Add(string.IsNullOrEmpty(name) ? typeName : $"{typeName}(\"{name}\")");
            current = current.hierarchy.parent;
        }
        return string.Join(" > ", parts);
    }

    // BuildMatchInfo is called 3-4 times per rendered stop for the SAME element (the rename, hide,
    // and heading hooks each rebuild it, and the renderer asks IsHeading and HeadingLevel
    // back-to-back), and each build walks 8 ancestors with two marshals + an interpolation per
    // level. The info is purely structural, so within one frame the same element yields the same
    // info — memo the last build. ReferenceEquals as the key is sound here: a false negative
    // (same native element, different wrapper) only costs a rebuild, never a wrong answer.
    private static VisualElement _matchInfoElement;
    private static int _matchInfoFrame = -1;
    private static ElementMatchInfo _matchInfoCached;

    // The structural identity of a stop as profile rules see it, read live from the element.
    private static ElementMatchInfo BuildMatchInfo(VisualElement element)
    {
        if (ReferenceEquals(element, _matchInfoElement) && UnityEngine.Time.frameCount == _matchInfoFrame)
            return _matchInfoCached;

        var typeName = element.GetIl2CppType().Name;
        var classification = Cache.GetClassification(element)?.ToString() ?? "";
        var ancestorPath = BuildAncestorPath(element, 8);
        var parent = element.hierarchy.parent;
        var parentName = parent != null ? (parent.name ?? "") : "";
        int childIndex = -1;
        if (parent != null)
        {
            int n = parent.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                if (parent.hierarchy[i].Pointer == element.Pointer) { childIndex = i; break; }
        }
        var info = new ElementMatchInfo(element.name ?? "", typeName, classification, ancestorPath, parentName, childIndex);
        _matchInfoElement = element;
        _matchInfoFrame = UnityEngine.Time.frameCount;
        _matchInfoCached = info;
        return info;
    }

    // A2: recover a section/column heading from a tile that hides its title area frame (so A1's
    // "title-text" rule finds nothing). The heading is the leading text of a column that sits
    // directly under a "2cols frame"; the deeper control labels and values fail this test, so a
    // control label is never promoted by mistake. Read live; on any doubt, return false.
    private static bool IsHeaderlessTileColumnHeading(VisualElement element)
    {
        var column = element.hierarchy.parent;
        if (column == null) return false;

        // Must be the leading (first) child of its column container.
        if (column.hierarchy.childCount == 0 || column.hierarchy[0].Pointer != element.Pointer) return false;

        // The column must sit directly inside a two-column tile layout.
        var twoCols = column.hierarchy.parent;
        if (twoCols == null || (twoCols.name ?? "") != "2cols frame") return false;

        // The enclosing tile must have no visible title-text — otherwise A1 already headed it and
        // promoting the column-leading text would risk a duplicate/wrong heading.
        var tile = NearestAncestorNamed(twoCols, "states-templates-tile-content", 4);
        if (tile == null) return false;
        var blockContainer = tile.hierarchy.parent;
        if (blockContainer == null) return false;
        var titleArea = FindDirectChildNamed(blockContainer, "title area frame");
        if (titleArea == null) return false; // can't confirm headerless: stay conservative
        if (ElementBridge.IsVisible(titleArea))
        {
            var titleText = FindByNameLocal(titleArea, "title-text", 6);
            if (titleText != null && !string.IsNullOrWhiteSpace(ReadText(titleText)))
                return false;
        }
        return true;
    }

    // A section-title element name: the bare "title-text" or a context-prefixed variant
    // "<context>-title-text". The leading hyphen makes "title-text" a whole trailing segment, so
    // "subtitle-text" (subtitle, not a heading) and "title-text-alt" (a subtitle slot) are excluded.
    private static bool IsSectionTitleName(string name)
    {
        return name == "title-text" || name.EndsWith("-title-text", StringComparison.Ordinal);
    }

    // True when an ancestor is a NavigationFocusser (the game's tab/navigation collection). A nav
    // tab's title ("navigation-tab-title-text") matches the section-title name pattern but is a
    // navigation control, not a content section heading, so it must not be promoted.
    private static bool IsInsideNavigationFocusser(VisualElement element)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            string type;
            try { type = current.GetIl2CppType().Name; }
            catch { type = current.name; }
            if (type == "NavigationFocusser") return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    private static VisualElement NearestAncestorNamed(VisualElement start, string name, int maxLevels)
    {
        var current = start.hierarchy.parent;
        for (int i = 0; i < maxLevels && current != null; i++)
        {
            if ((current.name ?? "") == name) return current;
            current = current.hierarchy.parent;
        }
        return null;
    }

    private static VisualElement FindDirectChildNamed(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if ((child.name ?? "") == name) return child;
        }
        return null;
    }

    private static VisualElement FindByNameLocal(VisualElement root, string name, int maxDepth)
    {
        if (maxDepth < 0) return null;
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if ((child.name ?? "") == name) return child;
            var deeper = FindByNameLocal(child, name, maxDepth - 1);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string ReadText(VisualElement element)
    {
        try
        {
            var t = element.TryCast<UnityEngine.UIElements.TextElement>();
            return t != null ? t.text : null;
        }
        catch { return null; }
    }

    private static void PatchOrDie(Harmony harmony, Type patchType)
    {
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            Speech.Log.Info($"Patched {patchType.Name}");
        }
        catch (Exception ex)
        {
            Speech.Log.Fatal($"Failed to apply patch {patchType.Name}: {ex}");
            AccessibilityMod.Kill();
            throw;
        }
    }

}
