#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Unattended Claude Code run against CrimeIncMVPPlan.md. Invoked by the
    "CrimeInc-GruSession" scheduled task on weekdays at 08:00 and 15:00.

.DESCRIPTION
    Starts a headless Claude Code session in the repo root and hands it the standing
    prompt: execute the next task in CrimeIncMVPPlan.md, then test it.

    Everything is logged under %LOCALAPPDATA%\crimeInc-scheduled\logs so an
    unattended run can be audited afterwards.

.NOTES
    Three guards exist because this runs with nobody watching.

    1. Dirty-tree refusal. By default the run aborts if the working tree has
       uncommitted changes. Stacking autonomous work on top of unreviewed work is how
       a repo becomes unreviewable. Override with -AllowDirty.

    2. Timeout. Caps a single run so a non-converging loop cannot burn tokens all
       day. Default 180 minutes.

    3. Overlap lock. The 08:00 run may still be going at 15:00; the second run exits
       rather than running two sessions over the same files.

    This file is deliberately ASCII-only. PowerShell 5.1 reads a BOM-less script as
    ANSI, which corrupts non-ASCII punctuation and can break string quoting.
#>
[CmdletBinding()]
param(
    # Repo to run in.
    [string]$RepoRoot = 'C:\Users\Jason_Weiss\personal\crimeInc',

    # Permission mode handed to Claude Code.
    #
    # 'bypassPermissions' is the default because it is the only mode in which this
    # job does anything: a headless session cannot answer permission prompts, and
    # /execute-plan needs to edit files AND run builds and tests. Understand what
    # that means before leaving it on - an unsupervised agent may run arbitrary
    # commands in this directory. 'acceptEdits' is safer but stalls on the first
    # build.
    [ValidateSet('bypassPermissions', 'acceptEdits', 'plan', 'default')]
    [string]$PermissionMode = 'bypassPermissions',

    # Minutes before the run is killed.
    [int]$TimeoutMinutes = 180,

    # Run even when the working tree has uncommitted changes.
    [switch]$AllowDirty,

    # Print what would happen and exit.
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

# The standing prompt, verbatim as specified.
$Prompt = ' /goal /execute-plan for the next task in the CrimeIncMVPPlan.md then run /test-feature on that task specifically until it is implemented correctly and at a satisfactory level. Any additions to the plan need to be add to an appendix at the end of the plan that is not included as a task and is to be reviewed later. Stop after goal has been completed'

# ---------------------------------------------------------------- logging

$LogDir = Join-Path $env:LOCALAPPDATA 'crimeInc-scheduled\logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$LogFile = Join-Path $LogDir ($stamp + '.log')

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '[{0}] {1,-5} {2}' -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message
    Add-Content -Path $LogFile -Value $line
    Write-Host $line
}

Write-Log '=== scheduled-gru starting ==='
Write-Log ('repo            : ' + $RepoRoot)
Write-Log ('permission mode : ' + $PermissionMode)
Write-Log ('timeout         : ' + $TimeoutMinutes + ' min')
Write-Log ('log             : ' + $LogFile)

# ---------------------------------------------------------------- preflight

if (-not (Test-Path $RepoRoot)) {
    Write-Log ('Repo root not found: ' + $RepoRoot) 'ERROR'
    exit 2
}

Set-Location $RepoRoot

$planFile = Join-Path $RepoRoot 'CrimeIncMVPPlan.md'
if (-not (Test-Path $planFile)) {
    Write-Log ('Plan file not found: ' + $planFile + '. Nothing to execute.') 'ERROR'
    exit 2
}

$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if ($claudeCmd) {
    $claudeExe = $claudeCmd.Source
}
else {
    $fallback = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
    if (Test-Path $fallback) {
        $claudeExe = $fallback
    }
    else {
        Write-Log ('claude executable not found on PATH or at ' + $fallback) 'ERROR'
        exit 2
    }
}
Write-Log ('claude          : ' + $claudeExe)

# ---- Guard 3: overlap lock ------------------------------------------------
# Taken before the dirty check so a long-running session cannot be tripped up by a
# second run observing files it is midway through editing.

$LockFile = Join-Path $env:LOCALAPPDATA 'crimeInc-scheduled\run.lock'
if (Test-Path $LockFile) {
    $lockPid = (Get-Content $LockFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    $alive = $false
    if ($lockPid -match '^\d+$') {
        $alive = $null -ne (Get-Process -Id ([int]$lockPid) -ErrorAction SilentlyContinue)
    }

    if ($alive) {
        Write-Log ('A previous run (PID ' + $lockPid + ') is still going. Exiting rather than overlapping.') 'WARN'
        exit 0
    }

    Write-Log ('Found a stale lock from PID ' + $lockPid + '; clearing it.') 'WARN'
    Remove-Item $LockFile -Force -ErrorAction SilentlyContinue
}

# ---- Guard 1: dirty tree -------------------------------------------------

$dirty = @(& git -C $RepoRoot status --porcelain 2>$null)
if ($dirty.Count -gt 0 -and -not $AllowDirty) {
    Write-Log ('Working tree has ' + $dirty.Count + ' uncommitted change(s). Refusing to run.') 'WARN'
    Write-Log 'Autonomous work stacked on unreviewed work becomes unreviewable, so this is' 'WARN'
    Write-Log 'the deliberate default. Commit or stash, or re-run with -AllowDirty.' 'WARN'
    $dirty | Select-Object -First 15 | ForEach-Object { Write-Log ('  ' + $_) 'WARN' }
    if ($dirty.Count -gt 15) {
        Write-Log ('  ... and ' + ($dirty.Count - 15) + ' more') 'WARN'
    }
    exit 0
}
if ($dirty.Count -gt 0) {
    Write-Log ('Working tree is dirty (' + $dirty.Count + ' change(s)) but -AllowDirty was set. Proceeding.') 'WARN'
}

$branch = (& git -C $RepoRoot branch --show-current 2>$null)
Write-Log ('branch          : ' + $branch)

if ($WhatIfOnly) {
    Write-Log '-WhatIfOnly set. Would run:'
    Write-Log ('  ' + $claudeExe + ' -p [prompt] --permission-mode ' + $PermissionMode)
    Write-Log ('Prompt: ' + $Prompt)
    exit 0
}

# ---------------------------------------------------------------- run

Set-Content -Path $LockFile -Value $PID -Encoding ASCII
Write-Log ('Lock taken (PID ' + $PID + ').')

$exitCode = 1
try {
    Write-Log 'Launching headless Claude Code session...'

    $sessionLog = Join-Path $LogDir ($stamp + '.session.log')
    $errLog = Join-Path $LogDir ($stamp + '.session.err.log')

    # -p runs headless: one shot, no interactive prompts. Output is captured rather
    # than streamed so the transcript survives for review.
    $proc = Start-Process -FilePath $claudeExe `
        -ArgumentList @('-p', $Prompt, '--permission-mode', $PermissionMode) `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $sessionLog `
        -RedirectStandardError $errLog `
        -NoNewWindow -PassThru

    Write-Log ('Session PID ' + $proc.Id + '; transcript -> ' + $sessionLog)

    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Write-Log ('Timed out after ' + $TimeoutMinutes + ' minutes. Killing the session.') 'ERROR'
        try { $proc.Kill() } catch { }
        $exitCode = 124
    }
    else {
        $exitCode = $proc.ExitCode
        Write-Log ('Session exited with code ' + $exitCode + '.')
    }

    foreach ($f in @($sessionLog, $errLog)) {
        if ((Test-Path $f) -and ((Get-Item $f).Length -gt 0)) {
            Write-Log ('---- tail of ' + (Split-Path $f -Leaf) + ' ----')
            Get-Content $f -Tail 40 | ForEach-Object { Write-Log ('  ' + $_) }
        }
    }

    $after = @(& git -C $RepoRoot status --porcelain 2>$null)
    Write-Log ('Working tree now has ' + $after.Count + ' uncommitted change(s).')
}
catch {
    Write-Log ('Run failed: ' + $_.Exception.Message) 'ERROR'
    $exitCode = 1
}
finally {
    Remove-Item $LockFile -Force -ErrorAction SilentlyContinue
    Write-Log 'Lock released.'
    Write-Log ('=== scheduled-gru finished (exit ' + $exitCode + ') ===')
}

exit $exitCode
