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

    The prompt is delivered on STDIN, not as a command-line argument. This is not a
    style choice - it is the fix for the bug that made every run before 2026-08-19 a
    silent no-op. Start-Process -ArgumentList @(...) splits each array element on
    whitespace, so a multi-word prompt arrived as a dozen separate argv entries:
    claude saw "-p /goal" with an empty condition, printed "No goal set. Usage:
    /goal <condition>", and exited in two seconds while the scheduled task recorded
    a successful run. Every argument this script now passes on the command line is a
    single token with no spaces, and anything containing user text goes over STDIN
    where no quoting rules apply. Do not "simplify" the prompt back into -ArgumentList.
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

# The standing prompt, verbatim as specified. Delivered on STDIN - see .NOTES.
#
# The leading '/goal' is intentional and correct: it sets a session-scoped goal whose
# condition is the rest of the line, and the session cannot stop until that condition
# holds. Everything after it is the condition text, not a chain of commands to run, so
# the '/execute-plan' and '/test-feature' inside it are read as instructions rather
# than invoked. Neither of those slash commands exists in this repo; the model reads
# the intent and uses the equivalent skills.
$Prompt = '/goal /execute-plan for the next task in the CrimeIncMVPPlan.md then run /test-feature on that task specifically until it is implemented correctly and at a satisfactory level. Any additions to the plan need to be add to an appendix at the end of the plan that is not included as a task and is to be reviewed later. Stop after goal has been completed'

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
    Write-Log ('  ' + $claudeExe + ' -p --permission-mode ' + $PermissionMode + '  < [prompt file]')
    Write-Log ('Prompt (' + $Prompt.Length + ' chars, delivered on STDIN):')
    Write-Log ('  ' + $Prompt)
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

    # The prompt goes to a file and is piped in on STDIN. Keeping it off the command
    # line is what stops PowerShell splitting it on spaces (see .NOTES); writing it
    # next to the logs also means an unattended run can be audited against the exact
    # prompt it was given, rather than the one the script says it sends today.
    $promptFile = Join-Path $LogDir ($stamp + '.prompt.txt')
    Set-Content -Path $promptFile -Value $Prompt -Encoding ASCII -NoNewline
    Write-Log ('prompt          : ' + $promptFile)

    # -p runs headless: no interactive prompts. Every argument here is a single token
    # with no spaces, so the -ArgumentList array cannot mangle it. Output is captured
    # rather than streamed so the transcript survives for review.
    $proc = Start-Process -FilePath $claudeExe `
        -ArgumentList @('-p', '--permission-mode', $PermissionMode) `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardInput $promptFile `
        -RedirectStandardOutput $sessionLog `
        -RedirectStandardError $errLog `
        -NoNewWindow -PassThru

    # Reading .Handle makes this object hold the process handle open, which is the only
    # reason .ExitCode is still readable after the process ends. Without it every run
    # logged "Session exited with code ." and the runner's own exit code was a guess.
    $null = $proc.Handle

    Write-Log ('Session PID ' + $proc.Id + '; transcript -> ' + $sessionLog)

    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Write-Log ('Timed out after ' + $TimeoutMinutes + ' minutes. Killing the session.') 'ERROR'
        try { $proc.Kill() } catch { }
        $exitCode = 124
    }
    else {
        $exitCode = $proc.ExitCode
        if ($null -eq $exitCode) {
            # Never let an unreadable exit code masquerade as success - that is how the
            # 2026-08-19 no-op run came to be recorded as a clean pass.
            Write-Log 'Session exited but the exit code could not be read; treating as failure.' 'ERROR'
            $exitCode = 1
        }
        else {
            Write-Log ('Session exited with code ' + $exitCode + '.')
        }

        # A real session opens files, builds and tests; it does not finish in seconds. A
        # near-instant exit is the signature of a prompt that never reached the model -
        # the exact failure that ran silently twice a day and reported success.
        $elapsed = [int]($proc.ExitTime - $proc.StartTime).TotalSeconds
        Write-Log ('Session ran for ' + $elapsed + ' second(s).')
        if ($elapsed -lt 30) {
            Write-Log 'That is too fast to have done any work. Check the transcript below:' 'WARN'
            Write-Log 'a usage message or an auth prompt there means the session never started.' 'WARN'
        }
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
