<#
.SYNOPSIS
Merges the Cobertura files from a test run into one markdown coverage summary.

.DESCRIPTION
`dotnet test` writes one Cobertura report per test project and target framework, so a run of this
repository produces a dozen partial views of the same two assemblies. This script unions them by
source line, then reports line, branch and method coverage over the merged result. A line covered by
the net10.0 leg and missed by the net472 leg counts once, as covered.

Method coverage is derived here rather than read off the report: Cobertura carries no method-level
totals, and ReportGenerator puts that metric behind sponsorship. A method counts as covered when any
of its lines was hit, and as fully covered when all of them were.

.PARAMETER ResultsDirectory
Directory searched recursively for *.cobertura.xml. Identical files are read once, which matters
because the collector leaves each report both in an attachment directory and in a staging directory.

.PARAMETER OutputFile
Markdown destination. The summary always goes to stdout as well.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [string] $OutputFile,
    [string] $Title = 'Code coverage'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ResultsDirectory)) { throw "Results directory not found: $ResultsDirectory" }

$reports = @(Get-ChildItem -Path $ResultsDirectory -Recurse -Filter '*.cobertura.xml' -File)
if ($reports.Count -eq 0) { throw "No *.cobertura.xml under $ResultsDirectory" }

# assembly name -> Lines: line key -> hit/branch counts, Methods: method key -> the line keys it owns
$assemblies = [ordered]@{}
$seen = [System.Collections.Generic.HashSet[string]]::new()
$read = 0

foreach ($report in $reports) {
    $hash = (Get-FileHash -Path $report.FullName -Algorithm SHA256).Hash
    if (-not $seen.Add($hash)) { continue }
    $read++

    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $false
    $doc.Load($report.FullName)

    foreach ($package in $doc.SelectNodes('/coverage/packages/package')) {
        $name = $package.GetAttribute('name')
        if (-not $assemblies.Contains($name)) {
            $assemblies[$name] = @{
                Lines   = [System.Collections.Generic.Dictionary[string, object]]::new()
                Methods = [System.Collections.Generic.Dictionary[string, object]]::new()
            }
        }
        $lines = $assemblies[$name].Lines
        $methods = $assemblies[$name].Methods

        foreach ($class in $package.SelectNodes('classes/class')) {
            $file = $class.GetAttribute('filename')
            $className = $class.GetAttribute('name')

            foreach ($method in $class.SelectNodes('methods/method')) {
                $methodKey = "$className|$($method.GetAttribute('name'))$($method.GetAttribute('signature'))"
                if (-not $methods.ContainsKey($methodKey)) {
                    $methods[$methodKey] = [System.Collections.Generic.HashSet[string]]::new()
                }
                $methodLines = $methods[$methodKey]

                foreach ($line in $method.SelectNodes('lines/line')) {
                    $key = "$file|$($line.GetAttribute('number'))"
                    $hits = [int]$line.GetAttribute('hits')

                    $branchCovered = 0
                    $branchTotal = 0
                    if ($line.GetAttribute('condition-coverage') -match '\((\d+)/(\d+)\)') {
                        $branchCovered = [int]$Matches[1]
                        $branchTotal = [int]$Matches[2]
                    }

                    if ($lines.ContainsKey($key)) {
                        $existing = $lines[$key]
                        if ($hits -gt $existing.Hits) { $existing.Hits = $hits }
                        if ($branchCovered -gt $existing.BranchCovered) { $existing.BranchCovered = $branchCovered }
                        if ($branchTotal -gt $existing.BranchTotal) { $existing.BranchTotal = $branchTotal }
                    }
                    else {
                        $lines[$key] = [pscustomobject]@{ Hits = $hits; BranchCovered = $branchCovered; BranchTotal = $branchTotal }
                    }

                    [void]$methodLines.Add($key)
                }
            }
        }
    }
}

function Get-Metrics {
    param($Lines, $Methods)

    $linesCovered = 0
    $branchesCovered = 0
    $branchesTotal = 0
    foreach ($line in $Lines.Values) {
        if ($line.Hits -gt 0) { $linesCovered++ }
        $branchesCovered += $line.BranchCovered
        $branchesTotal += $line.BranchTotal
    }

    $methodsCovered = 0
    $methodsFull = 0
    $methodsTotal = 0
    foreach ($methodLines in $Methods.Values) {
        if ($methodLines.Count -eq 0) { continue }
        $methodsTotal++
        $hit = 0
        foreach ($key in $methodLines) { if ($Lines[$key].Hits -gt 0) { $hit++ } }
        if ($hit -gt 0) { $methodsCovered++ }
        if ($hit -eq $methodLines.Count) { $methodsFull++ }
    }

    [pscustomobject]@{
        LinesCovered    = $linesCovered
        LinesTotal      = $Lines.Count
        BranchesCovered = $branchesCovered
        BranchesTotal   = $branchesTotal
        MethodsCovered  = $methodsCovered
        MethodsFull     = $methodsFull
        MethodsTotal    = $methodsTotal
    }
}

function Format-Percent {
    param([int] $Covered, [int] $Total)
    if ($Total -le 0) { return 'n/a' }
    '{0:0.0}%' -f (100.0 * $Covered / $Total)
}

# The two assemblies never share a source line, so the repository total is the union of both maps.
$totalLines = [System.Collections.Generic.Dictionary[string, object]]::new()
$totalMethods = [System.Collections.Generic.Dictionary[string, object]]::new()
foreach ($name in $assemblies.Keys) {
    foreach ($pair in $assemblies[$name].Lines.GetEnumerator()) { $totalLines[$pair.Key] = $pair.Value }
    foreach ($pair in $assemblies[$name].Methods.GetEnumerator()) { $totalMethods["$name|$($pair.Key)"] = $pair.Value }
}
$total = Get-Metrics -Lines $totalLines -Methods $totalMethods

$md = [System.Collections.Generic.List[string]]::new()
$md.Add("## $Title")
$md.Add('')
$md.Add('| Metric | Covered | Total | Coverage |')
$md.Add('| :--- | ---: | ---: | ---: |')
$md.Add("| Lines | $($total.LinesCovered) | $($total.LinesTotal) | $(Format-Percent $total.LinesCovered $total.LinesTotal) |")
$md.Add("| Branches | $($total.BranchesCovered) | $($total.BranchesTotal) | $(Format-Percent $total.BranchesCovered $total.BranchesTotal) |")
$md.Add("| Methods | $($total.MethodsCovered) | $($total.MethodsTotal) | $(Format-Percent $total.MethodsCovered $total.MethodsTotal) |")
$md.Add("| Methods (fully covered) | $($total.MethodsFull) | $($total.MethodsTotal) | $(Format-Percent $total.MethodsFull $total.MethodsTotal) |")
$md.Add('')
$md.Add('| Assembly | Lines | Branches | Methods |')
$md.Add('| :--- | ---: | ---: | ---: |')
foreach ($name in $assemblies.Keys) {
    $m = Get-Metrics -Lines $assemblies[$name].Lines -Methods $assemblies[$name].Methods
    $line = '| `{0}` | {1} ({2}/{3}) | {4} ({5}/{6}) | {7} ({8}/{9}) |' -f $name,
        (Format-Percent $m.LinesCovered $m.LinesTotal), $m.LinesCovered, $m.LinesTotal,
        (Format-Percent $m.BranchesCovered $m.BranchesTotal), $m.BranchesCovered, $m.BranchesTotal,
        (Format-Percent $m.MethodsCovered $m.MethodsTotal), $m.MethodsCovered, $m.MethodsTotal
    $md.Add($line)
}
$md.Add('')
$reportWord = if ($read -eq 1) { 'report' } else { 'reports' }
$md.Add("<sub>Merged from $read Cobertura $reportWord; a line counts as covered when any target framework covered it.</sub>")

$text = $md -join [Environment]::NewLine
Write-Output $text

if ($OutputFile) {
    $dir = Split-Path -Parent $OutputFile
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    Set-Content -Path $OutputFile -Value $text -Encoding utf8
}
