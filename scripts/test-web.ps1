[CmdletBinding()]
param([switch]$CareerShell, [switch]$Squads, [switch]$Maintenance, [switch]$Lineup, [switch]$FunctionalMenus, [switch]$Widgets, [switch]$Biography, [switch]$ManagementStyle, [switch]$MatchDugout, [switch]$DataHubCharts, [switch]$Conversation, [switch]$HistoricalReplay, [switch]$LoadingProgress, [switch]$Finance, [switch]$ClubHistory, [switch]$NativeDialogs, [switch]$PitchMove, [switch]$DateField, [switch]$SearchDropdown, [switch]$CommandFailures, [switch]$MessageIdentity, [switch]$MessageReturnFocus, [switch]$DocumentLanguage, [switch]$TileAvailability, [switch]$ReadIsolation, [switch]$InitialReadRecovery, [switch]$TacticalAdvice, [switch]$SetPieceTakers, [switch]$PenaltyOptions, [switch]$FixtureIdentity, [switch]$HeaderMenu, [switch]$ConditionIdentity, [switch]$DuplicateCondition, [switch]$AttributeCondition, [switch]$LeagueGameplay, [switch]$ConditionDate, [switch]$DropdownFinalRefresh, [switch]$PrecareerSetup, [switch]$TrainingArtwork, [switch]$CardOptionsCycle, [switch]$Responsibilities, [switch]$ResponsibilityPersistence, [switch]$MedicalTabRefresh, [switch]$TableRowIdentity, [switch]$ResponsibilityCategories, [switch]$AdviceReports, [switch]$AdviceCustomization, [switch]$Mentoring, [switch]$SoundSettings, [switch]$SoundPersistenceFailure, [switch]$MessageSearchFocus, [switch]$OppositionLifecycle, [switch]$TrainingEffectiveness, [switch]$RecruitmentEffectiveness, [switch]$CoachAvailability, [switch]$HttpRequestFraming, [switch]$OfferDraft, [switch]$LoanDraft, [switch]$ContractReturn, [switch]$ContractNegotiation, [switch]$NativeDestinationTab, [switch]$NativeHistoryTabs, [switch]$HistoryPendingCancel, [switch]$MessageWindowIdentity, [switch]$NativePagerOwner, [switch]$NewsTitle, [switch]$NativeBookmarks, [switch]$RouteAudit, [switch]$CustomPlanDayIdentity, [switch]$CustomPlanPersistence, [switch]$TrainingCalendar, [switch]$TrainingEdits, [switch]$CalendarPopupOwner, [switch]$CalendarCommandIdentity, [switch]$CalendarHeader, [switch]$HeadingDescriptions)
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
$companionPath=$null
$gamePid=$null
try {
    $health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 3
    if($health.adapter -ne 'fm26-original-core' -or $health.screen -ne 'Live'){throw 'The production core must be live. Main suites require the main menu; -CareerShell requires an owned QA career at Portal.'}
    # The inherited server shares native state and update baselines. Use one UI
    # client during automation, restoring only this game's owned companion afterwards.
    $gamePid=$health.pid
    $gameProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$gamePid"
    $expected=Join-Path ([IO.Path]::GetDirectoryName($gameProcess.ExecutablePath)) 'BepInEx/plugins/FM27Access/companion/FM27AccessCompanion.exe'
    foreach($process in Get-CimInstance Win32_Process -Filter "Name='FM27AccessCompanion.exe'"){
        if([IO.Path]::GetFullPath($process.ExecutablePath) -eq [IO.Path]::GetFullPath($expected) -and $process.CommandLine -match "--watch-pid $gamePid(?:\s|$)"){
            if(-not $env:FM27_COMPANION_CDP){
                $companionPath=$process.ExecutablePath
                Stop-Process -Id $process.ProcessId
            }
        }
    }
    if(-not(Test-Path tests/companion/node_modules)){
        & npm ci --prefix tests/companion --no-audit --no-fund
        if($LASTEXITCODE -ne 0){throw 'npm ci failed.'}
    }
    $tests=if($Finance){@('live-finance.cjs')}elseif($ClubHistory){@('live-club-history.cjs')}elseif($LoadingProgress){@('live-loading-progress.cjs')}elseif($HistoricalReplay){@('live-historical-replay.cjs')}elseif($Conversation){@('live-conversation.cjs')}elseif($DataHubCharts){@('live-datahub-charts.cjs')}elseif($MatchDugout){@('live-match-dugout.cjs')}elseif($ManagementStyle){@('live-management-style.cjs')}elseif($Biography){@('live-fm27-widgets.cjs')}elseif($Widgets){@('live-fm27-widgets.cjs','live-dropdown-refresh.cjs')}elseif($FunctionalMenus){@('live-functional-menus.cjs')}elseif($Lineup){@('live-lineup.cjs')}elseif($Maintenance){@('live-maintenance.cjs','live-card-customization.cjs')}elseif($Squads){@('live-squads.cjs')}elseif($CareerShell){@('live-career-shell.cjs')}else{@('live-full-core.cjs','live-fm27-widgets.cjs','live-dropdown-refresh.cjs')}
    if($NativeDialogs){$tests=@('live-native-dialogs.cjs')}
    if($PitchMove){$tests=@('live-pitch-move.cjs')}
    if($DateField){$tests=@('live-date-field.cjs')}
    if($SearchDropdown){$tests=@('live-search-dropdown.cjs')}
    if($CommandFailures){$tests=@('live-command-failures.cjs')}
    if($MessageIdentity){$tests=@('live-message-identity.cjs')}
    if($MessageReturnFocus){$tests=@('live-message-return-focus.cjs')}
    if($DocumentLanguage){$tests=@('live-document-language.cjs')}
    if($TileAvailability){$tests=@('live-tile-availability.cjs')}
    if($ReadIsolation){$tests=@('live-read-isolation.cjs')}
    if($InitialReadRecovery){$tests=@('live-initial-read-recovery.cjs')}
    if($TacticalAdvice){$tests=@('live-tactical-advice.cjs')}
    if($SetPieceTakers){$tests=@('live-setpiece-takers.cjs')}
    if($PenaltyOptions){$tests=@('live-penalty-options.cjs')}
    if($FixtureIdentity){$tests=@('live-fixture-identity.cjs')}
    if($HeaderMenu){$tests=@('live-header-menu.cjs')}
    if($ConditionIdentity){$tests=@('live-condition-identity.cjs')}
    if($DuplicateCondition){$tests=@('live-duplicate-condition.cjs')}
    if($AttributeCondition){$tests=@('live-attribute-condition.cjs')}
    if($LeagueGameplay){$tests=@('live-league-gameplay.cjs')}
    if($ConditionDate){$tests=@('live-condition-date.cjs')}
    if($DropdownFinalRefresh){$tests=@('live-dropdown-final-refresh.cjs')}
    if($PrecareerSetup){$tests=@('live-precareer-setup.cjs')}
    if($TrainingArtwork){$tests=@('live-training-artwork.cjs')}
    if($CardOptionsCycle){$tests=@('live-card-options-cycle.cjs')}
    if($Responsibilities -or $ResponsibilityPersistence){$tests=@('live-responsibilities.cjs')}
    if($MedicalTabRefresh){$tests=@('live-medical-tab-refresh.cjs')}
    if($TableRowIdentity){$tests=@('live-table-row-identity.cjs')}
    if($ResponsibilityCategories){$tests=@('live-responsibility-categories.cjs')}
    if($AdviceReports){$tests=@('live-advice-reports.cjs')}
    if($AdviceCustomization){$tests=@('live-advice-customization.cjs')}
    if($Mentoring){$tests=@('live-mentoring.cjs')}
    if($SoundSettings){$tests=@('live-sound-settings.cjs')}
    if($SoundPersistenceFailure){$tests=@('live-sound-persistence-failure.cjs')}
    if($MessageSearchFocus){$tests=@('live-message-search-focus.cjs')}
    if($OppositionLifecycle){$tests=@('live-opposition-lifecycle.cjs')}
    if($TrainingEffectiveness){$tests=@('live-training-effectiveness.cjs')}
    if($RecruitmentEffectiveness){$tests=@('live-recruitment-effectiveness.cjs')}
    if($CoachAvailability){$tests=@('live-coach-availability.cjs')}
    if($HttpRequestFraming){$tests=@('live-http-request-framing.cjs')}
    if($OfferDraft){$tests=@('live-offer-draft.cjs')}
    if($LoanDraft){$tests=@('live-loan-draft.cjs')}
    if($ContractReturn){$tests=@('live-contract-return.cjs')}
    if($ContractNegotiation){$tests=@('live-contract-negotiation.cjs')}
    if($NativeDestinationTab){$tests=@('live-native-destination-tab.cjs')}
    if($NativeHistoryTabs){$tests=@('live-native-history-tabs.cjs')}
    if($HistoryPendingCancel){$tests=@('live-history-pending-cancel.cjs')}
    if($MessageWindowIdentity){$tests=@('live-message-window-identity.cjs')}
    if($NativePagerOwner){$tests=@('live-native-pager-owner.cjs')}
    if($NewsTitle){$tests=@('live-news-title.cjs')}
    if($NativeBookmarks){$tests=@('live-native-bookmarks.cjs')}
    if($RouteAudit){$tests=@('live-route-audit.cjs')}
    if($CustomPlanDayIdentity){$tests=@('live-custom-plan-day-identity.cjs')}
    if($CustomPlanPersistence){$tests=@('live-custom-plan-persistence.cjs')}
    if($TrainingCalendar){$tests=@('live-training-calendar.cjs')}
    if($TrainingEdits){$tests=@('live-training-edits.cjs')}
    if($CalendarPopupOwner){$tests=@('live-calendar-popup-owner.cjs')}
    if($CalendarCommandIdentity){$tests=@('live-calendar-command-identity.cjs')}
    if($CalendarHeader){$tests=@('live-calendar-header.cjs')}
    if($HeadingDescriptions){$tests=@('live-heading-descriptions.cjs')}
    $nodeArguments=if($ResponsibilityPersistence){@('--disk-persistence')}elseif($Biography){@('--biography-only')}else{@()}
    foreach($test in $tests){
        & node "tests/companion/$test" @nodeArguments
        if($LASTEXITCODE -ne 0){throw "Live UI test failed: $test"}
    }
} finally {
    if($companionPath -and (Get-Process -Id $gamePid -ErrorAction SilentlyContinue)){
        Start-Process -FilePath $companionPath -ArgumentList '--watch-pid',$gamePid -WindowStyle Hidden
    }
    Pop-Location
}






