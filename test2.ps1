$CWD = Get-Location
"Outer Script @ $CWD"
"Launching Inner Script at some location"
& ".\test.ps1"
