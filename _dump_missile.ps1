$p = "D:\SteamLibrary\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Assembly-CSharp.dll"
$a = [Reflection.Assembly]::LoadFrom($p)
$t = $a.GetType("Missile")
if ($null -eq $t) { "Missile not found"; exit 1 }
"--- Fields ---"
$t.GetFields([Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance) | ForEach-Object { "$($_.FieldType.Name) $($_.Name)" }
"--- Props ---"
$t.GetProperties([Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance) | ForEach-Object { "$($_.PropertyType.Name) $($_.Name)" }
