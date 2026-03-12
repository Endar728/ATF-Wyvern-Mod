# Release Notes - v1.0.0

## Bug Fixes
- **Fixed Harmony patching error**: Resolved exception in `LaserReticleUIPatch` that was preventing mod from loading correctly
- **Fixed NullReferenceException errors**: Added comprehensive null checks and defensive handling in `LaserDesignator.LaseTargets()` and `WeaponManager.SetTargetList()` patches

## Improvements
- **Toggle button now works**: F9 key now properly toggles all mod features on/off with detailed status feedback
- **All features respect global toggle**: Individual features now check both their config setting and the global `modEnabled` flag
- **Enhanced logging**: Added detailed status messages when toggling features, showing which features are active

## Documentation
- **Updated README**: Added clear instructions for Master Safe Slot feature usage
- **Added toggle key documentation**: Explained how to use F9 to toggle all features in-game

## Technical Changes
- Disabled problematic `LaserReticleUIPatch` (other patches handle reticle visibility)
- Improved null safety in laser deconfliction system
- Better list reference preservation to prevent breaking game state
