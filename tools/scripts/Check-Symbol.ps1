<#
.SYNOPSIS
Show every place this repo records a fact about one symbol, so disagreements are visible at a glance.

.DESCRIPTION
An RE finding can live in four places: tools/ghidra_scripts/known_symbols_<binary>.json, Herculan/docs/**,
the C# port under Herculan/src/**, and the Ghidra database itself. Nothing keeps them in step, and
each refers to the same function differently -- one doc says FUN_00467944, another says
CountdownTimerTick, the C# says Math_CountdownTimerTick. A grep for any single spelling therefore
misses most of the copies, which is how a corrected finding stays wrong in two places for months.

This resolves one identifier (address OR name, partial names allowed) to every alias the repo uses
for it, then reports every mention of every alias, grouped by location.

Run it BEFORE deriving something from disassembly -- the answer may already be recorded, most often
in a C# doc comment written during the port, which is the copy least likely to be reflected upstream.
Run it AFTER recording a finding, to catch the older copies that now contradict it.

It reports, never edits. Reconciling what it finds is a judgement call.

.PARAMETER Query
An address (00467944), a symbol name, or a fragment of one (case-insensitive).

.PARAMETER RepoRoot
Repo root. Defaults to two levels above this script.

.EXAMPLE
.\Check-Symbol.ps1 00467944
.EXAMPLE
.\Check-Symbol.ps1 CountdownTimer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Query,

    [string] $RepoRoot,

    # Broad queries match many symbols; searching docs and source for all of them at once produces
    # tens of KB. Without -Force a broad match lists the symbols only, which is the useful answer
    # to "what is in this family?" anyway.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}

$jsonPaths = @('dbsim', 'vshell' | ForEach-Object { Join-Path $RepoRoot "tools/ghidra_scripts/known_symbols_$_.json" })
$docsPath = Join-Path $RepoRoot 'Herculan/docs'
$srcPath  = Join-Path $RepoRoot 'Herculan/src'

# Each file carries a UTF-8 BOM, which ConvertFrom-Json will not accept.
$symbols = @(foreach ($jsonPath in $jsonPaths) {
    if (-not (Test-Path $jsonPath)) { throw "known_symbols file not found at $jsonPath" }
    $raw = (Get-Content -Raw -Encoding UTF8 $jsonPath).TrimStart([char]0xFEFF)
    ($raw | ConvertFrom-Json).entries
})


$q = $Query.Trim()
$matched = @($symbols | Where-Object {
    $_.address -eq $q -or
    ($_.name -and $_.name -like "*$q*") -or
    ($_.address -like "*$q*")
})

# Aliases: every spelling the repo might use for this symbol. Older docs were written before the
# symbol was named and still refer to it by raw address, usually as FUN_/DAT_/LAB_; Show-Hits
# matches an address alias with any such prefix, so the prefixed forms need no alias of their own.
$aliases = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
$rejected = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)

# A bare name is only safe to search if it cannot collide with ordinary prose or common identifiers.
# Bullet_Draw -> "Draw" and ESButtonFont_Ctor -> "Ctor" match thousands of unrelated lines and bury the
# real hits. Require multi-word CamelCase (an internal capital) and a reasonable length: that keeps
# CountdownTimerTick, RandomNext and Q16Divide while rejecting Draw, Ctor, Blit and Fire.
function Test-DistinctiveAlias {
    param([string] $Name)
    if ($Name.Length -lt 6) { return $false }
    if ($Name.Substring(1) -cnotmatch '[A-Z]') { return $false }
    return $true
}

[void]$aliases.Add($q)
foreach ($e in $matched) {
    [void]$aliases.Add($e.address)
    if ($e.name) {
        [void]$aliases.Add($e.name)
        # The C# port drops the module prefix: Math_CountdownTimerTick becomes
        # SimMath.CountdownTimerTick, so the bare name is the only spelling that finds engine call
        # sites -- but only when it is distinctive enough to be worth searching.
        if ($e.name -match '^[A-Za-z0-9]+_(.+)$') {
            $bare = $Matches[1]
            if (Test-DistinctiveAlias $bare) { [void]$aliases.Add($bare) }
            else { [void]$rejected.Add($bare) }
        }
    }
}

# Broad queries are a symbol lookup, not a cross-check. Listing 19 symbols' descriptions plus every
# doc and source mention of all of them runs to tens of KB, which is never what was wanted.
if ($matched.Count -gt 5 -and -not $Force) {
    Write-Host ''
    Write-Host "=== $($matched.Count) entries match '$q' ===" -ForegroundColor Cyan
    foreach ($e in $matched) {
        $label = $e.name
        if (-not $label) { $label = '<unnamed, comment-only>' }
        Write-Host "  $($e.address) [$($e.binary)] $label"
    }
    Write-Host ''
    Write-Host "  Narrow the query to cross-check one symbol, or pass -Force to search docs and" -ForegroundColor Yellow
    Write-Host "  source for all $($matched.Count)." -ForegroundColor Yellow
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "=== known_symbols ===" -ForegroundColor Cyan
if ($matched.Count -eq 0) {
    Write-Host "  (no entry matches '$q')" -ForegroundColor Yellow
} else {
    foreach ($e in $matched) {
        $label = $e.name
        if (-not $label) { $label = '<unnamed, comment-only>' }
        Write-Host "  $($e.address) [$($e.binary)/$($e.type)/$($e.confidence)] $label"
        if ($e.signature) { Write-Host "    signature: $($e.signature)" -ForegroundColor Green }
        # A stamped description has been checked against the body, so it is shown whole: it is the answer,
        # and cutting it would send the reader back to the decompile. See es2_stamp.py.
        if ($e.verified) {
            Write-Host "    VERIFIED ($($e.verified.scope), $($e.verified.bytes)) - what the description says this code does is checked; claims about other code only as the negatives below" -ForegroundColor Green
            foreach ($n in @($e.verified.negatives)) {
                if ($n) { Write-Host "    negative [$($n.evidence)]: $($n.claim) -- $($n.how)" -ForegroundColor Green }
            }
        }
        if ($e.description) {
            $d = $e.description
            if (-not $e.verified -and $d.Length -gt 300) { $d = $d.Substring(0, 300) + ' ...' }
            Write-Host "    $d" -ForegroundColor DarkGray
        }
        if ($e.source) { Write-Host "    source: $($e.source)" -ForegroundColor DarkGray }
    }
}

Write-Host ''
Write-Host "=== aliases searched ===" -ForegroundColor Cyan
Write-Host "  $(($aliases | Sort-Object) -join ', ')"
if ($rejected.Count -gt 0) {
    Write-Host "  not searched (too generic, would swamp the results): $(($rejected | Sort-Object) -join ', ')" -ForegroundColor DarkYellow
    Write-Host "  grep for those by hand if you need them." -ForegroundColor DarkYellow
}

function Show-Hits {
    param([string] $Title, [string] $Root, [string[]] $Include)

    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    if (-not (Test-Path $Root)) {
        Write-Host "  (path not found: $Root)" -ForegroundColor Yellow
        return 0
    }

    # Anchor on word boundaries. Substring matching makes a bare alias like "Draw" hit "drawn",
    # "redraw" and "DrawHeadsDown". [char]92 is a backslash, built this way rather than written
    # literally so the pattern survives being generated through a shell heredoc.
    # An address is the exception: `_` is a word character, so \b never fires inside FUN_00442394,
    # and an address with no known_symbols entry would go unfound. Addresses instead refuse only an
    # adjacent letter or digit, which admits every Ghidra prefix (FUN_, DAT_, LAB_, PTR_, s_, ...)
    # while still rejecting 00442394 inside 1004423940.
    $wb = [string][char]92 + 'b'
    $patterns = @($aliases | ForEach-Object {
        if ($_ -match '^[0-9A-Fa-f]{8}$') { '(?<![0-9A-Za-z])' + $_ + '(?![0-9A-Za-z])' }
        else { $wb + [regex]::Escape($_) + $wb }
    })
    $files = Get-ChildItem -Path $Root -Recurse -File -Include $Include -ErrorAction SilentlyContinue
    $hits = @($files | Select-String -Pattern $patterns -SimpleMatch:$false -ErrorAction SilentlyContinue)

    if ($hits.Count -eq 0) {
        Write-Host "  (no mentions)" -ForegroundColor Yellow
        return 0
    }

    foreach ($g in $hits | Group-Object Path) {
        $rel = $g.Name.Replace($RepoRoot, '').TrimStart('\', '/')
        Write-Host "  $rel"
        foreach ($h in $g.Group) {
            $line = $h.Line.Trim()
            if ($line.Length -gt 150) { $line = $line.Substring(0, 150) + ' ...' }
            Write-Host "    $($h.LineNumber): $line" -ForegroundColor DarkGray
        }
    }
    return $hits.Count
}

$docHits = Show-Hits -Title 'Herculan/docs' -Root $docsPath -Include '*.md'
$srcHits = Show-Hits -Title 'Herculan/src (C# port)' -Root $srcPath -Include '*.cs'

# The drift signal: a fact recorded in exactly one place has nothing to disagree with, and nothing
# to keep it honest. That is the state every stale finding was in before it went stale.
$places = 0
if ($matched.Count -gt 0) { $places++ }
if ($docHits -gt 0) { $places++ }
if ($srcHits -gt 0) { $places++ }

Write-Host ''
Write-Host "=== summary ===" -ForegroundColor Cyan
Write-Host "  known_symbols entries: $($matched.Count)   doc mentions: $docHits   code mentions: $srcHits"
if ($places -eq 0) {
    Write-Host "  Nothing recorded anywhere -- this is genuinely new ground." -ForegroundColor Yellow
} elseif ($places -eq 1) {
    Write-Host "  Recorded in ONE location only. Check whether the other two should know about it." -ForegroundColor Yellow
} else {
    Write-Host "  Recorded in $places locations -- read them against each other before trusting any one." -ForegroundColor Yellow
}
Write-Host ''
