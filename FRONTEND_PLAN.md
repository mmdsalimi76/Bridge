# Frontend Modernization Plan

## Goal

Polish VPS Manager into a cohesive Windows 11-style operations app while retaining Avalonia, the Fluent theme, and the existing MVVM architecture.

## Design Direction

- Keep Fluent controls and a restrained dark graphite palette with teal as the primary accent.
- Favor compact operational layouts, clear hierarchy, and useful information density over decorative cards and repeated hero banners.
- Use consistent vector icons, spacing, typography, borders, and semantic status colors.
- Keep destructive operations distinct and easy to confirm.

## Work Plan

1. **[x] Shell navigation:** Keep a persistent slim navigation rail with the hamburger in the rail. Start collapsed at 52 px; expand to 264 px. Keep the VPS selector and active view available in either state.
2. **[x] Shared design tokens:** Common graphite/teal surfaces, semantic colors, labels, panels, and action-button defaults are centralized in application resources; duplicate base palettes and obsolete per-view headers are removed. Specialized controls and operation-state colors remain view-specific.
3. **In progress: Overview and shell:** Compact headers, active-server context, status, and focused actions are implemented. Manual visual review at the default and minimum window sizes remains.
4. **In progress: Operations views:** Compact headers, shared base styling, and semantic log surfaces are in place for Deploy, Patch, Docker, Database, Setup, SSH, and Settings. Manual workflow behavior review remains. Sideload was intentionally removed and is out of scope; do not restore its navigation.
5. **In progress: Responsive and accessibility pass:** The collapsed rail retains 18 px icons in 24 px tracks with tooltips and accessible names, buttons have keyboard-focus styling, Setup's two-column workspace scales, and a 960 x 640 minimum window size is set. Manual resize, keyboard, focus, and contrast checks remain.

## Verification

- [x] Client build with output isolated from the running app: `dotnet build src/VpsManager.Client/VpsManager.Client.csproj --no-restore --output "$env:TEMP\VpsManager.Client.Validation" -p:UseAppHost=false`.
- [x] Structural check confirms the hamburger is inside the rail, 18 px icons use 24 px tracks, collapsed destinations remain accessible, active navigation follows the current view, and widths are 52 px/264 px.
- [x] Common style/resource scan confirms shared label, panel, and action styles are centralized and no duplicate base palettes remain in views.
- [x] Targeted Avalonia-view scan finds no purple/violet/lavender resource names or palette values, obsolete header gradients, or macOS traffic-light decorations.
- [ ] Manually verify the rail toggle and confirm selected VPS/current view remain unchanged.
- [ ] Check the main workflows, keyboard/focus behavior, and status contrast at the default and minimum window sizes.
