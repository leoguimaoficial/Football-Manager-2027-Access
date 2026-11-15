[CmdletBinding()]
param([Parameter(Mandatory)][string]$GamePath,[ValidateSet('Debug','Release')][string]$Configuration='Release')
. "$PSScriptRoot/Common.ps1"
$core=Join-Path $ProjectRoot "FM27Access.Plugin/bin/$Configuration/net6.0/FM27Access.Plugin.dll"
$installed=Join-Path $GamePath 'BepInEx/plugins/FM27Access/FM27Access.Plugin.dll'
$stream=[IO.File]::OpenRead($core)
try{
    $pe=[System.Reflection.PortableExecutable.PEReader]::new($stream)
    $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $module=$metadata.GetModuleDefinition()
    $mvid=$metadata.GetGuid($module.Mvid).ToString()
}finally{if($pe){$pe.Dispose()};$stream.Dispose()}
$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 5
if($health.adapter -ne 'fm26-original-core' -or $health.screen -ne 'Live' -or $health.coreMvid -ne $mvid){throw 'Running core does not match the built production Plugin.'}
$builtHash=(Get-FileHash -LiteralPath $core).Hash
$installedHash=(Get-FileHash -LiteralPath $installed).Hash
if($builtHash -ne $installedHash){throw 'Installed Plugin does not match the current build.'}
$relativeSources=@(
    'FM27Access.Navigation/DisplayText.cs',
    'FM27Access.Navigation/StreamedTableReader.cs',
    'FM27Access.Plugin/Server/TablePages.cs','FM27Access.Navigation/TabBarHtml.cs',
    'FM27Access.Navigation/TablePaging.cs','FM27Access.Navigation/TablePagerHtml.cs',
    'FM27Access.Speech/SpeechPipeline.cs'
)
$reuse=foreach($relative in $relativeSources){
    $source=Get-Content -LiteralPath (Join-Path $ProjectRoot $relative) -Raw
    $baseline=Get-Content -LiteralPath (Join-Path $ProjectRoot ('.upstream/fm26/'+$relative.Replace('FM27Access','FM26Access'))) -Raw
    $same=$source.Replace('FM27Access','FM26Access').Replace("`r`n","`n").TrimEnd() -eq $baseline.Replace("`r`n","`n").TrimEnd()
    if(!$same){throw "REUSE EXACT source diverged: $relative"}
    @{file=$relative;identityRenameOnly=$true}
}
# The FM27 loader adds schema/attachment guards and applies the inherited shared
# settle to each newly realized window. This is adapted source, not exact reuse.
$loaderRelative='FM27Access.Plugin/Server/StreamedTableLoader.cs'
$loader=(Get-Content -LiteralPath (Join-Path $ProjectRoot $loaderRelative) -Raw).Replace('FM27Access','FM26Access').Replace("`r`n","`n").TrimEnd()
$loaderBaseline=(Get-Content -LiteralPath (Join-Path $ProjectRoot '.upstream/fm26/FM26Access.Plugin/Server/StreamedTableLoader.cs') -Raw).Replace("`r`n","`n").TrimEnd()
$entrySignature='    internal static void Load(ScreenWalker.TableItem item)'
$coreSignature='    private static void LoadCore(ScreenWalker.TableItem item)'
$entry=$loader.IndexOf($entrySignature,[StringComparison]::Ordinal)
$originalEntry=$loaderBaseline.IndexOf($entrySignature,[StringComparison]::Ordinal)
$coreEntry=$loader.IndexOf($coreSignature,[StringComparison]::Ordinal)
if($entry -lt 0 -or $originalEntry -lt 0 -or $coreEntry -lt 0){throw 'Native-adapted streamed loader entry points are missing.'}
if($loader.Substring(0,$entry) -ne $loaderBaseline.Substring(0,$originalEntry)){
    throw 'The inherited streamed-loader budgets or paging constants changed.'
}
foreach($contract in @('internal const int PageRows = 50;',
    'new StreamedTableReader<TableRow>(PageRows, RowReadyBudgetMs, rowBudget)',
    'SettleWindow(startIndex, pendingBefore, tHarvestStart, outcome);',
    'SettleWindow(idx, observed.window, observed.at, harvest);',
    'ctx.ReadRowIfPendingStill(idx, observedPending[idx])',
    'Thread.Sleep(SliceYieldMs)', 'ContextMenuGuard.MenuActive()')){
    if(!$loader.Contains($contract)){throw "Adapted streamed-loader contract missing: $contract"}
}
$adapted=@(@{file=$loaderRelative;identityRenameOnly=$false;adaptation='Native schema/attachment guards and inherited shared pending settlement per realized window; unchanged budgets and pure 50-row reader. Structural checks plus live empty-lineup regression, not whole-file equality.';currentSha256=(Get-FileHash (Join-Path $ProjectRoot $loaderRelative)).Hash})

# The editable search session is inherited verbatim apart from validating its
# rendered native owner before opening or closing another session.
$searchRelative='FM27Access.Plugin/Server/SearchDropdownSession.cs'
$search=(Get-Content -LiteralPath (Join-Path $ProjectRoot $searchRelative) -Raw).Replace('FM27Access','FM26Access').Replace("`r`n","`n").TrimEnd()
$searchBaseline=(Get-Content -LiteralPath (Join-Path $ProjectRoot '.upstream/fm26/FM26Access.Plugin/Server/SearchDropdownSession.cs') -Raw).Replace("`r`n","`n").TrimEnd()
$searchGuard="                var stale = ServerHandles.ValidateScreen(expectedScreen);`n                if (stale != null) return stale;`n"
if (!$search.Contains($searchGuard) -or $search.Replace('Open(string id, string expectedScreen = null)','Open(string id)').Replace($searchGuard,'') -ne $searchBaseline) {
    throw 'Editable search session changed outside its native opening-owner guard.'
}
$adapted+=@{file=$searchRelative;identityRenameOnly=$false;adaptation='Validate the rendered native owner before opening; remaining FM26 popup, filter, selection, session gate and listbox contracts are exact.'}

# The F169 adaptation atomically rejects queued callers at Stop, including a
# producer racing enqueue. Normalize only those reviewed deltas, then compare
# the complete remainder with FM26. Behavioral tests link the actual dispatcher.
$dispatcherRelative='FM27Access.Plugin/Server/MainThreadDispatcher.cs'
$dispatcher=(Get-Content -LiteralPath (Join-Path $ProjectRoot $dispatcherRelative) -Raw).Replace('FM27Access','FM26Access').Replace("`r`n","`n").TrimEnd()
$dispatcherBaseline=(Get-Content -LiteralPath (Join-Path $ProjectRoot '.upstream/fm26/FM26Access.Plugin/Server/MainThreadDispatcher.cs') -Raw).Replace("`r`n","`n").TrimEnd()
$stoppedGuard='if (Stopped) throw new InvalidOperationException("The accessibility mod has stopped.");'
if(([regex]::Matches($dispatcher,[regex]::Escape($stoppedGuard))).Count -ne 3){throw 'Dispatcher stopped-lifecycle guards are missing.'}
$dispatcherRest=[regex]::Replace($dispatcher,'(?m)^ *'+[regex]::Escape($stoppedGuard)+'\n','')
$stopLifecycle=@'
    private static volatile bool _stopped;
    public static bool Stopped
    {
        get => _stopped;
        set
        {
            lock (QueueGate)
            {
                _stopped = value;
                if (!value) return;
                // No further Unity frame will drain this core. Release waiting
                // callers and their captured native references immediately,
                // without ever running an undispatched game command. The same
                // gate prevents a racing producer from enqueueing after Stop.
                while (_queue.TryDequeue(out var work)) work.Reject();
                Volatile.Write(ref _deferredDepth, 0);
            }
        }
    }
'@
$rejectAndEnqueue=@'
        }, ticket, () =>
        {
            ticket.Cancelled = true;
            error = new InvalidOperationException("The accessibility mod has stopped.");
            done.Set();
        });
        lock (QueueGate)
        {
            _queue.Enqueue(work);
        }
'@
$stopLifecycle=$stopLifecycle.Replace("`r`n","`n")
$rejectAndEnqueue=$rejectAndEnqueue.Replace("`r`n","`n")
if(!$dispatcherRest.Contains($stopLifecycle) -or !$dispatcherRest.Contains($rejectAndEnqueue)){throw 'Atomic stop/rejection adaptation differs from the reviewed contract.'}
$dispatcherRest=$dispatcherRest.Replace($stopLifecycle,'    public static volatile bool Stopped;').Replace($rejectAndEnqueue,'        }, ticket));')
$dispatcherRest=$dispatcherRest.Replace('public Work(string label, Action action, Ticket ticket, Action reject) { Label = label; Action = action; Ticket = ticket; Reject = reject; }','public Work(string label, Action action, Ticket ticket) { Label = label; Action = action; Ticket = ticket; }')
$dispatcherRest=$dispatcherRest.Replace('        public Action Reject { get; }'+"`n",'').Replace('    private static readonly object QueueGate = new();'+"`n",'')
$dispatcherRest=$dispatcherRest.Replace('        var work = new Work($"{caller}:{line}", () =>','        _queue.Enqueue(new Work($"{caller}:{line}", () =>')
$expandedTry="            try`n            {`n                result = fn();`n            }"
$dispatcherRest=$dispatcherRest.Replace($expandedTry,'            try { result = fn(); }')
if($dispatcherRest -ne $dispatcherBaseline){throw 'Dispatcher changed outside its stopped-lifecycle guard.'}
$adapted+=@{file=$dispatcherRelative;identityRenameOnly=$false;adaptation='F169 atomic stop/enqueue gate, rejection callbacks and three stop checks; exact remaining FM26 source. Actual linked dispatcher lifetime/producer-race tests provide behavioral evidence.'}
# The native submenu controller belongs to a recycled row. Additional FM27
# adaptations validate opening ownership and recover a root popup lost during
# native option binding. Strip ONLY these exact reviewed additions before the
# remaining full-file comparison; retain original gates, budgets and selection.
$dropdownRelative='FM27Access.Plugin/Server/DropdownSession.cs'
$dropdown=(Get-Content -LiteralPath (Join-Path $ProjectRoot $dropdownRelative) -Raw).Replace('FM27Access','FM26Access').Replace("`r`n","`n").TrimEnd()
$dropdownBaseline=(Get-Content -LiteralPath (Join-Path $ProjectRoot '.upstream/fm26/FM26Access.Plugin/Server/DropdownSession.cs') -Raw).Replace("`r`n","`n").TrimEnd()
$nativeOpen=@'
            var openError = MainThreadDispatcher.Invoke(() =>
            {
                var level = ResolveAndPop(path, out var error);
                error ??= _conditionGuard?.Invoke();
                error ??= level == null ? SessionGoneMessage : ChoiceAvailabilityError(level, index);
                if (error != null) return error;
                level.Adapter.TriggerChildOpen(index);
                return (string)null;
            });
            if (openError != null) return openError;

'@
$nativeOpen=$nativeOpen.Replace("`r`n","`n")+"`n"
$oldOpen='                level.Adapter.TriggerChildOpen(index);'+"`n"
$nativeChild='top.Adapter.GetVisibleChildAdapter(index)'
$oldChild='top.Adapter.GetVisibleChildAdapter()'
$popupRecovery=@'
                var current = TopOrNull;
                if (current == null) return (State: SessionGone, Fragment: (string)null);
                // A recycled native anchor can close the popup AFTER its first
                // visible frame, while its option rows are still binding. Reuse
                // opening recovery within this existing bounded read loop. Resolve
                // the same current handle again; never reuse a recycled control or
                // reopen after a screen change/explicit session cancellation.
                if (!current.Adapter.IsPopupVisible())
                {
                    // The registered handle owns the root, not a child submenu.
                    // Never replace a vanished child with its parent's options.
                    if (_levels.Count != 1)
                        return (State: -2, Fragment: "The submenu closed before its options were ready. Close and reopen the dropdown.");
                    var stale = ServerHandles.ValidateScreen(_ownerScreen);
                    if (stale != null) return (State: -2, Fragment: stale);
                    var adapter = ServerHandles.ResolveDropdownHandle(_ownerId, out var error);
                    if (adapter == null) return (State: -2, Fragment: error);
                    if (adapter.IsDisabled()) return (State: -2, Fragment: "This control is not available right now.");
                    current.Adapter = adapter;
                    adapter.ShowPopup();
                    Log("Root popup closed while options were binding; reopened the validated current control.");
                    return (State: 2, Fragment: (string)null);
                }
'@
$popupDiagnostic=@'
        try
        {
            MainThreadDispatcher.Invoke(() =>
            {
                var level = TopOrNull;
                if (level != null)
                {
                    var element = level.Adapter.BackingElement;
                    Log($"Unbound options: anchor={element.worldBound} panel={element.panel != null} visible={ElementBridge.IsVisibleInHierarchy(element)} popup={level.Adapter.IsPopupVisible()}\n{level.Adapter.DescribeActiveItems()}");
                }
                return true;
            });
        }
        catch (Exception ex) { Log("unbound-options diagnostic failed: " + ex.GetType().Name); }
'@
$dropdownRest=$dropdown
# Reviewed FM27-only search-rule capability/context adapter. Ordinary sessions,
# gates, budgets and hierarchy remain the exact FM26 implementation below.
$conditionSelectionGuard=@'
                var conditionError = _conditionGuard?.Invoke();
                if (conditionError != null)
                {
                    CloseCurrentOnMainThread();
                    return (Label: (string)null, Err: conditionError, Defer: false, Now: (bool?)null);
                }
'@
$conditionSelectionGuard=$conditionSelectionGuard.Replace("`r`n","`n")+"`n"
$conditionAdditions=@(
    ('    private static Func<string> _conditionGuard;'+"`n"),
    ('    private static string _ownerContext;'+"`n"),
    ('                    _conditionGuard = ServerHandles.CaptureConditionValidation(id);'+"`n"),
    ('                    _ownerContext = expectedContext;'+"`n"),
    $conditionSelectionGuard
)
foreach($addition in $conditionAdditions){
    if(!$dropdownRest.Contains($addition)){throw 'Reviewed native search-rule selection guard changed.'}
    $dropdownRest=$dropdownRest.Replace($addition,'')
}
if(([regex]::Matches($dropdownRest,'(?m)^\s*_conditionGuard = null;\n')).Count -ne 4 -or
   ([regex]::Matches($dropdownRest,'(?m)^\s*_ownerContext = null;\n')).Count -ne 6){throw 'Reviewed search-rule session cleanup changed.'}
$dropdownRest=[regex]::Replace($dropdownRest,'(?m)^[ ]*_(conditionGuard|ownerContext) = null;\n','')
$dropdownRest=$dropdownRest.Replace('Open(string id, string expectedScreen = null, string expectedContext = null)','Open(string id, string expectedScreen = null)')
$dropdownRest=$dropdownRest.Replace('ResolveDropdownHandle(id, out var error, expectedContext)','ResolveDropdownHandle(id, out var error)')
$dropdownRest=$dropdownRest.Replace('ResolveDropdownHandle(id, out var error, expectedContext, scroll)','ResolveDropdownHandle(id, out var error)')
$dropdownRest=$dropdownRest.Replace('ResolveDropdownHandle(_ownerId, out var error, _ownerContext)','ResolveDropdownHandle(_ownerId, out var error)')
# Reviewed F175 native availability and one-scroll realization adaptation.
$availabilityHelpers=@'
    private static string ChoiceAvailabilityError(Level level, int index)
    {
        if (!level.Adapter.TryReadChoiceDisabled(index, out var disabled))
            return "The option row is not ready. Close and reopen the dropdown.";
        return disabled ? "This option is not available right now." : null;
    }

    // Realize an offscreen option once, then observe binding across frames. Do
    // not restart its asynchronous binding by scrolling on every observation.
    // Only the caller's later guarded main-thread dispatch may invoke a command.
    private static string PrepareRequestedChoice(string path, int index)
    {
        string error = null;
        bool scrolled = false;
        bool ready = WaitFor(() =>
        {
            var level = ResolveAndPop(path, out error);
            error ??= _conditionGuard?.Invoke();
            if (level == null || error != null) return true;
            if (index < 0 || index >= level.Choices.Count)
            {
                error = "That option is no longer available. Refresh the screen.";
                return true;
            }
            if (level.Adapter.TryReadChoiceDisabled(index, out var disabled))
            {
                if (disabled) error = "This option is not available right now.";
                return true;
            }
            if (!scrolled)
            {
                scrolled = true;
                level.Adapter.ScrollToChoice(index);
            }
            return false;
        }, VisibleTimeoutMs, "realizeOptionRow");
        return error ?? (ready ? null : "The option row is not ready. Close and reopen the dropdown.");
    }

'@
$scrollOnce=@'
                    // Scroll once. Re-scrolling every read restarts the native
                    // row's asynchronous bind and can keep its control disabled.
                    bool scroll = !scrolled;
                    scrolled = true;
'@
$captureAvailability=@'
            l.Disabled = new List<bool>(l.Adapter.BuildIsDisabledArray());
            if (l.Disabled.Count != choices.Count)
                throw new InvalidOperationException("Native dropdown labels and availability are not aligned.");
'@
$selectAvailability=@'
                var availabilityError = ChoiceAvailabilityError(level, index);
                if (availabilityError != null)
                    return (Label: (string)null, Err: availabilityError, Defer: false, Now: (bool?)null);
'@
$prepareChoice="            var preparation = PrepareRequestedChoice(path, index);`n            if (preparation != null) return preparation;`n"
if(([regex]::Matches($dropdownRest,[regex]::Escape($prepareChoice))).Count -ne 2){throw 'Reviewed F175 expand/select preparation changed.'}
foreach($addition in @($availabilityHelpers,$scrollOnce,$captureAvailability,$selectAvailability)){
    $addition=$addition.Replace("`r`n","`n")+"`n"
    if(!$dropdownRest.Contains($addition)){throw 'Reviewed F175 native availability/realization contract changed.'}
    $dropdownRest=$dropdownRest.Replace($addition,'')
}
$dropdownRest=$dropdownRest.Replace($prepareChoice,'').Replace('            bool scrolled = false;'+"`n",'')
$dropdownRest=$dropdownRest.Replace('        public List<bool> Disabled = new();'+"`n",'').Replace('            l.Disabled = new List<bool>();'+"`n",'')
$dropdownRest=$dropdownRest.Replace('l.Checked, l.Disabled)','l.Checked)').Replace('ReadRealizedWindow(out var disabled)','ReadRealizedWindow()').Replace('AppendContiguous(l.Choices, l.Disabled, window, disabled)','AppendContiguous(l.Choices, window)')
$dropdownRest=$dropdownRest.Replace('private const int RealizeBudgetMs = 1000;  // Same native recycled-row budget as CellHandle.Activate.','private const int RealizeBudgetMs = 400;   // CellHandle.Activate''s recycled-row re-bind budget')
$oldAvailability="                var disabled = level.Adapter.BuildIsDisabledArray();`n                if (index < disabled.Length && disabled[index])`n                    return `"This option is not available right now.`";`n"
$dropdownRest=$dropdownRest.Replace('                // Names the choice the request actually landed on.', $oldAvailability+'                // Names the choice the request actually landed on.')
$openingGuard="                    var stale = ServerHandles.ValidateScreen(expectedScreen);`n                    if (stale != null) return (stale, false);`n"
$nativeAdditions=@(
    $openingGuard,
    ($popupRecovery.Replace("`r`n","`n")+"`n"),
    ($popupDiagnostic.Replace("`r`n","`n")+"`n"),
    ('    private static string _ownerScreen;'+"`n"),
    ('                    _ownerScreen = ServerText.ScreenToken(InteractivePanels.Current());'+"`n"),
    ('                _ownerScreen = ServerText.ScreenToken(InteractivePanels.Current());'+"`n"),
    ('        _ownerScreen = null;'+"`n"),
    ('            if (r.State == -2) return FailAndClose(r.Fragment);'+"`n")
)
foreach($addition in $nativeAdditions){
    if(!$dropdownRest.Contains($addition)){throw 'Reviewed native dropdown owner/recovery adaptation changed; review against FM26 before updating this audit.'}
    $dropdownRest=$dropdownRest.Replace($addition,'')
}
$dropdownRest=$dropdownRest.Replace('Open(string id, string expectedScreen = null)','Open(string id)')
if(!$dropdown.Contains($nativeOpen) -or !$dropdownBaseline.Contains($oldOpen) -or
   !$dropdown.Contains($nativeChild) -or !$dropdownBaseline.Contains($oldChild) -or
   $dropdownRest.Replace($nativeOpen,'').Replace($nativeChild,$oldChild) -ne $dropdownBaseline.Replace($oldOpen,'')){
    throw 'The dropdown session changed outside its reviewed native submenu, owner and bounded root-popup adaptations.'
}
$adapted+=@{file=$dropdownRelative;identityRenameOnly=$false;adaptation='F175 native option availability by absolute index, final native recheck, one-scroll realization, exact child adoption, source/condition guards and bounded popup recovery. Row-realization budget1000ms matches CellHandle; remaining full normalized source, gates and retry/selection budgets equal FM26.'}
$patch=Get-Content -LiteralPath (Join-Path $ProjectRoot 'FM27Access.Plugin/Patches/PanelManagerPatches.cs') -Raw
if($patch -match 'Postfix\([^)]*PanelLayerSettings\.Layer'){throw 'Unsafe Layer argument reintroduced in a metadata 39 Harmony callback.'}
if($patch -match '\[HarmonyPatch\(typeof\(PanelManager\),\s*"OnPanelLoadedInternal"\)'){
    throw 'Unsafe Layer-bearing original-call trampoline reintroduced. Use the native Panel-only opened event.'
}
$catalog=Get-Content -LiteralPath (Join-Path $ProjectRoot 'FM27Access.Plugin/FM27PanelCatalog.cs') -Raw
if(($patch+$catalog) -match 'TryGetLayerInfoForPanel\s*\('){throw 'Unsafe out LayerInfo API reintroduced at the metadata 39 boundary.'}
$report=@{adapter=$health.adapter;state=$health.screen;pid=$health.pid;coreMvid=$mvid;installedCoreSha256=$installedHash;reusedExact=$reuse;reusedWithAdapter=$adapted;baseline='4d38a0256f29b97376845ef59edfcd64d7f165ed';manualNVDA=$false}
$destination=Join-Path $ProjectRoot 'artifacts/analysis/runtime-reuse.json'
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Output "Production core active in PID $($health.pid); module identity and installed hash match. Exact central sources: $($reuse.Count); adapted: $($adapted.Count). Report: $destination"
