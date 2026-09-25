<#
.SYNOPSIS
  Export the IL2CPP field names of every type the PWS2LevelKit stubs mirror, for PackValidator.

  For each stub asmdef under PWS2LevelKit/Assets/Stubs, reads the matching proxy assembly
  (Il2Cpp<asmdef>.dll) and records every native field of every class/struct whose simple name
  is declared in the stub sources. Proxy fields are recognised by their NativeFieldInfoPtr_<name>
  statics, so auto-property backing fields appear as _X_k__BackingField, exactly as Il2CppInterop
  mangles <X>k__BackingField. Namespaces lose their Il2Cpp prefix; nested types use '/'.

  Re-run after every game update and commit the JSON; PackBuilder fails the build on a mismatch.

.EXAMPLE
  & docs/tools/export_stub_fields.ps1                     # run in-process from the PowerShell tool
  & docs/tools/export_stub_fields.ps1 -GameVersion 1.3.1
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\PowerWash Simulator 2",
  [string]$StubDir = (Join-Path $PSScriptRoot "..\..\PWS2LevelKit\Assets\Stubs"),
  [string]$Out = (Join-Path $PSScriptRoot "stub-fields.json"),
  [string]$GameVersion = "1.3.0"
)
Add-Type -Path "$GameDir\MelonLoader\net6\Mono.Cecil.dll"
$dir = "$GameDir\MelonLoader\Il2CppAssemblies"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($dir); $resolver.AddSearchDirectory("$GameDir\MelonLoader\net6")
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver = $resolver

$types = @()
foreach ($asmdef in Get-ChildItem $StubDir -Recurse -Filter '*.asmdef') {
  $asmName = (Get-Content $asmdef.FullName -Raw | ConvertFrom-Json).name
  $names = @{}
  foreach ($cs in Get-ChildItem $asmdef.DirectoryName -Recurse -Filter '*.cs') {
    foreach ($m in [regex]::Matches((Get-Content $cs.FullName -Raw), '\b(?:class|struct)\s+(\w+)')) { $names[$m.Groups[1].Value] = 1 }
  }
  $proxy = Join-Path $dir "Il2Cpp$asmName.dll"
  if (-not (Test-Path $proxy)) { Write-Warning "no proxy assembly for stub asmdef $asmName"; continue }
  $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($proxy, $rp)
  foreach ($t in $asm.MainModule.GetTypes()) {
    if (-not $names.ContainsKey(($t.Name -split '`')[0]) -or $t.IsEnum) { continue }
    $full = $t.FullName -replace '^Il2Cpp', ''
    $fields = @($t.Fields | ? { $_.Name -like 'NativeFieldInfoPtr_*' } | % { $_.Name.Substring('NativeFieldInfoPtr_'.Length) })
    $types += [ordered]@{ name = $full; assembly = $asmName; fields = $fields }
  }
}
$types = @($types | Sort-Object { $_.name })
[ordered]@{ gameVersion = $GameVersion; types = $types } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $Out
Write-Output ("wrote {0} types to {1}" -f $types.Count, (Resolve-Path $Out))
