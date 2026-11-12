$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SkeletonUnityMenu {
    [DllImport("user32.dll")] public static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
    [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
}
'@
$editor = Get-Process Unity | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like '*WorstKnights*' } | Select-Object -First 1
if (-not $editor) { throw 'WorstKnights Unity Editor window was not found.' }
$menu = [SkeletonUnityMenu]::GetMenu($editor.MainWindowHandle)
for ($i = 0; $i -lt [SkeletonUnityMenu]::GetMenuItemCount($menu); $i++) {
    $label = New-Object System.Text.StringBuilder 256
    [void][SkeletonUnityMenu]::GetMenuString($menu, $i, $label, 256, 0x400)
    if ($label.ToString().Replace('&','') -ne 'Assets') { continue }
    $assets = [SkeletonUnityMenu]::GetSubMenu($menu, $i)
    for ($j = 0; $j -lt [SkeletonUnityMenu]::GetMenuItemCount($assets); $j++) {
        $item = New-Object System.Text.StringBuilder 256
        [void][SkeletonUnityMenu]::GetMenuString($assets, $j, $item, 256, 0x400)
        if (($item.ToString().Replace('&','') -split "`t")[0] -ne 'Refresh') { continue }
        $identifier = [SkeletonUnityMenu]::GetMenuItemID($assets, $j)
        [void][SkeletonUnityMenu]::PostMessage($editor.MainWindowHandle, 0x111, [UIntPtr]::new($identifier), [IntPtr]::Zero)
        Write-Output "Requested Assets/Refresh in process $($editor.Id)."
        exit 0
    }
}
throw 'Assets/Refresh native menu entry was not found.'
