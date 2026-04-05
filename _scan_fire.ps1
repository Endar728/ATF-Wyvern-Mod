$path = 'D:\SteamLibrary\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Assembly-CSharp.dll'
if (-not (Test-Path $path)) { Write-Output 'DLL missing'; exit 1 }
$a = [Reflection.Assembly]::LoadFrom($path)
$types = @()
try { $types = $a.GetTypes() }
catch [Reflection.ReflectionTypeLoadException] {
  $types = $_.Exception.Types | Where-Object { $_ -ne $null }
}
foreach ($t in $types) {
  if ($null -eq $t) { continue }
  if ($t.Name -notmatch 'Missile|Bomb|Laser|Mounted|Weapon') { continue }
  foreach ($m in $t.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly')) {
    if ($m.Name -ne 'Fire') { continue }
    $pn = @()
    foreach ($par in $m.GetParameters()) { $pn += $par.ParameterType.Name }
    Write-Output ($t.FullName + ' Fire(' + ($pn -join ', ') + ')')
  }
}
