// Keyboard-shortcut registry (data only). Handlers live in views/shell.
export interface ShortcutDef {
  id: string;
  keys: string;
  labelKey: string;
  action: string;
}

export const SHORTCUTS: readonly ShortcutDef[] = [
  { id: "save", keys: "Ctrl+S", labelKey: "editor.save", action: "editor-save" },
  { id: "search", keys: "Ctrl+F", labelKey: "search.title", action: "search-focus" },
  { id: "sync", keys: "Ctrl+R", labelKey: "sync.syncNow", action: "sync-now" },
  { id: "new-folder", keys: "Ctrl+Shift+N", labelKey: "browser.createFolder", action: "browser-new-folder" },
  { id: "undo", keys: "Ctrl+Z", labelKey: "activity.undo", action: "activity-undo" },
];

export function findShortcut(action: string): ShortcutDef | null {
  for (const s of SHORTCUTS) {
    if (s.action === action) {
      return s;
    }
  }
  return null;
}
