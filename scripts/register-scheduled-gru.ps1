# Registers (or re-registers) the CrimeInc-GruSession scheduled task.
# Weekdays only, at 08:00 and 15:00, running as the current user.
#
# Re-runnable: an existing task of the same name is replaced, so this is safe to
# run again after editing the runner script.

$ErrorActionPreference = 'Stop'

$TaskName = 'CrimeInc-GruSession'
$Runner = 'C:\Users\Jason_Weiss\personal\crimeInc\scripts\scheduled-gru.ps1'
$RepoRoot = 'C:\Users\Jason_Weiss\personal\crimeInc'

if (-not (Test-Path $Runner)) {
    throw ('Runner script not found: ' + $Runner)
}

# -WindowStyle Hidden so a scheduled run does not throw a console window in your face
# mid-work. The transcript goes to the log directory instead.
$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Runner + '"') `
    -WorkingDirectory $RepoRoot

$weekdays = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')

$morning = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $weekdays -At '08:00'
$afternoon = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $weekdays -At '15:00'

# StartWhenAvailable catches a missed run if the machine was asleep at the trigger
# time. ExecutionTimeLimit is a backstop above the runner's own timeout.
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopIfGoingOnBatteries `
    -AllowStartIfOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4)

$principal = New-ScheduledTaskPrincipal `
    -UserId ($env:USERDOMAIN + '\' + $env:USERNAME) `
    -LogonType Interactive `
    -RunLevel Limited

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host ('Removed existing task: ' + $TaskName)
}

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger @($morning, $afternoon) `
    -Settings $settings `
    -Principal $principal `
    -Description 'Runs a headless Claude Code session against CrimeIncMVPPlan.md: execute the next task, then test it. Refuses to run on a dirty working tree unless -AllowDirty is set in the runner.' | Out-Null

Write-Host ('Registered: ' + $TaskName)

$task = Get-ScheduledTask -TaskName $TaskName
Write-Host ('State      : ' + $task.State)
foreach ($t in $task.Triggers) {
    Write-Host ('Trigger    : ' + $t.StartBoundary + '  days=' + $t.DaysOfWeek)
}

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host ('Next run   : ' + $info.NextRunTime)
