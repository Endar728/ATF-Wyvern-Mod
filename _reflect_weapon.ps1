$paths = @(
    'D:\SteamLibrary\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Assembly-CSharp.dll',
    'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Assembly-CSharp.dll'
)
$dll = $paths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) { Write-Output 'DLL not found'; exit 1 }
$asm = [Reflection.Assembly]::LoadFrom($dll)
$bf = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$m = $asm.GetType('Missile')
if ($m) {
    Write-Output "--- Missile ---"
    foreach ($f in $m.GetFields($bf)) { Write-Output ("  field {0} : {1}" -f $f.Name, $f.FieldType.Name) }
    Write-Output "--- Missile props ---"
    foreach ($p in $m.GetProperties($bf)) {
        if ($p.GetIndexParameters().Count -eq 0) { Write-Output ("  prop {0} : {1}" -f $p.Name, $p.PropertyType.Name) }
    }
}
foreach ($name in @('MountedMissile','MissileLauncher')) {
    $t = $asm.GetType($name)
    if (-not $t) { continue }
    Write-Output "--- $name Fire methods ---"
    foreach ($mi in $t.GetMethods([Reflection.BindingFlags]'Instance,Public,NonPublic')) {
        if ($mi.Name -eq 'Fire') {
            $ps = @($mi.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ', '
            Write-Output ("  Fire({0})" -f $ps)
        }
    }
}
foreach ($name in @('MountedMissile','MissileLauncher','Weapon','Laser')) {
    $t = $asm.GetType($name)
    if (-not $t) { Write-Output "--- $name : NOT FOUND"; continue }
    Write-Output "--- $name ---"
    foreach ($f in $t.GetFields($bf)) {
        Write-Output ("  field {0} : {1}" -f $f.Name, $f.FieldType.Name)
    }
    foreach ($p in $t.GetProperties($bf)) {
        if ($p.GetIndexParameters().Count -eq 0) {
            Write-Output ("  prop {0} : {1}" -f $p.Name, $p.PropertyType.Name)
        }
    }
}
