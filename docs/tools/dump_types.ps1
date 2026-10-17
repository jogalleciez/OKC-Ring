<#
.SYNOPSIS
  Dump fields, properties and method signatures of IL2CPP proxy types with Mono.Cecil
  (the copy MelonLoader ships). Use this after every game update to re-check that the
  type/field names mirrored by PWS2LevelKit stubs and PWS2LevelLoader still hold.

.EXAMPLE
  pwsh docs/tools/dump_types.ps1 -Types GameJob,Location,WashTarget
  pwsh docs/tools/dump_types.ps1 -Types Campaign -MethodRegex '^(LoadContent|FindJobPack)$'
  pwsh docs/tools/dump_types.ps1 -MethodRegex '^LoadContentCatalog' -Types @()   # method search only
#>
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\PowerWash Simulator 2",
  [string[]]$Types = @('GameJob','Location','JobPackManifest','ContentPackManifest','SceneWashableData','WashTarget','WashTargetData','UniqueId','CharacterSpawnPosition'),
  [string]$MethodRegex = '.',
  [string[]]$Assemblies = @(),
  [int]$MaxMethods = 60
)
Add-Type -Path "$GameDir\MelonLoader\net6\Mono.Cecil.dll"
$dir = "$GameDir\MelonLoader\Il2CppAssemblies"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($dir); $resolver.AddSearchDirectory("$GameDir\MelonLoader\net6")
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver = $resolver
if ($Assemblies.Count -eq 0) {
  $Assemblies = (Get-ChildItem $dir -Filter 'Il2CppFuturLab*.dll' | % Name) + 'Il2CppFuturlab.PW2.Boot.dll','Assembly-CSharp.dll'
}
function Short($t){ if($null -eq $t){return '?'}; $n=$t.Name; if($t.IsGenericInstance){ $n = $t.ElementType.Name.Split('`')[0] + '<' + (($t.GenericArguments | % { Short $_ }) -join ',') + '>' }; return $n }
$found=@{}; $hits=@()
foreach($f in $Assemblies){
  $p = Join-Path $dir $f; if(-not (Test-Path $p)){ continue }
  $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($p,$rp)
  foreach($t in $asm.MainModule.GetTypes()){
    if($MethodRegex -ne '.'){
      foreach($m in $t.Methods){ if($m.Name -match $MethodRegex){ $ps = ($m.Parameters | % { (Short $_.ParameterType) + ' ' + $_.Name }) -join ', '; $hits += ("  {0}::{1}({2}) : {3}   [{4}]" -f $t.FullName, $m.Name, $ps, (Short $m.ReturnType), $f) } }
    }
    if($t.Name -notin $Types){ continue }
    if($found.ContainsKey($t.FullName)){ continue }; $found[$t.FullName]=1
    Write-Output ""; Write-Output ("===== {0}  [{1}]  : {2}" -f $t.FullName, $f, (Short $t.BaseType))
    foreach($fd in $t.Fields | ? { $_.Name -notmatch '^(NativeFieldInfoPtr_|NativeMethodInfoPtr_|Il2CppClassPointer|NativeClassPtr)' }){ Write-Output ("  F {0} : {1}" -f $fd.Name, (Short $fd.FieldType)) }
    foreach($pr in $t.Properties){ Write-Output ("  P {0} : {1}" -f $pr.Name, (Short $pr.PropertyType)) }
    $ms = $t.Methods | ? { -not $_.IsConstructor -and -not $_.IsGetter -and -not $_.IsSetter -and $_.Name -notmatch '^(op_|MemberwiseClone|Finalize|GetHashCode|Equals|ToString)' -and $_.Name -match $MethodRegex }
    foreach($m in $ms | Select-Object -First $MaxMethods){ $ps = ($m.Parameters | % { (Short $_.ParameterType) + ' ' + $_.Name }) -join ', '; Write-Output ("  M {0}({1}) : {2}" -f $m.Name, $ps, (Short $m.ReturnType)) }
  }
}
if($hits.Count){ Write-Output ""; Write-Output "===== METHOD HITS ====="; $hits | Sort-Object -Unique | % { Write-Output $_ } }
$missing = $Types | ? { $n=$_; -not ($found.Keys | ? { $_ -match "\.$n`$" -or $_ -eq $n }) }
if($missing){ Write-Output ""; Write-Output ("NOT FOUND: " + ($missing -join ', ')) }
