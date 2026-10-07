import { describe, expect, it, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { OnboardingView } from "@views/OnboardingView";
import { RepoListView } from "@views/RepoListView";
import { RepoOverviewView } from "@views/RepoOverviewView";
import { BrowserView } from "@views/BrowserView";
import { InspectorView } from "@views/InspectorView";
import { EditorView } from "@views/EditorView";
import { PreviewerView } from "@views/PreviewerView";
import { SearchView } from "@views/SearchView";
import { TagsView } from "@views/TagsView";
import { InboxOrganizerView } from "@views/InboxOrganizerView";
import { DuplicatesView } from "@views/DuplicatesView";
import { SnapshotsView } from "@views/SnapshotsView";
import { ActivityView } from "@views/ActivityView";
import { SyncView } from "@views/SyncView";
import { TransfersView } from "@views/TransfersView";
import { TrashView } from "@views/TrashView";
import { SettingsView } from "@views/SettingsView";
import { AboutDiagnosticsView } from "@views/AboutDiagnosticsView";
import {
  useAccountsStore,
  useActivityStore,
  useConflictsStore,
  useDuplicatesStore,
  useEditorStore,
  useOrganizerStore,
  useReposStore,
  useSearchStore,
  useSettingsStore,
  useSnapshotsStore,
  useSyncStore,
  useTagsStore,
  useTransfersStore,
  useTrashStore,
  useTreeStore,
  type StoreStatus,
} from "@core/stores";
import type { ErrorCode } from "@core/contract";

const STATUSES: { status: StoreStatus; code: ErrorCode | null; suffix: string }[] = [
  { status: "loading", code: null, suffix: "loading" },
  { status: "empty", code: null, suffix: "empty" },
  { status: "error", code: "NOT_FOUND", suffix: "error" },
  { status: "offline", code: "OFFLINE", suffix: "offline" },
  { status: "reauth", code: "REAUTH_REQUIRED", suffix: "reauth" },
  { status: "not-configured", code: "OAUTH_NOT_CONFIGURED", suffix: "not-configured" },
  { status: "denied", code: "PERMISSION_DENIED", suffix: "denied" },
];

const NODE = { id: "n1", parentId: null, name: "a", isFolder: false, mime: "x", size: 1, modifiedTime: "2026-01-01T00:00:00Z", trashed: false };

function seedReady(): void {
  useAccountsStore.setState({ status: "ready", errorCode: null, accounts: [{ id: "a1", email: "e@x.com", state: "connected" }] });
  useReposStore.setState({ status: "ready", errorCode: null, repos: [{ id: "r1", name: "repo", kind: "single" }], overview: { id: "r1", name: "repo", totalFiles: 1, totalBytes: 1, readmeExcerpt: "hi", projects: [], byKind: {} } });
  useTreeStore.setState({ status: "ready", errorCode: null, nodes: [NODE], expanded: {} });
  useEditorStore.setState({ status: "ready", errorCode: null, nodeId: "n1", text: "hi", dirty: false, version: "v1" });
  useSearchStore.setState({ status: "ready", errorCode: null, query: "q", results: [{ nodeId: "n1", name: "a", snippet: "s" }] });
  useTagsStore.setState({ status: "ready", errorCode: null, tags: [{ id: "t1", name: "x", origin: "user" }], suggestions: [] });
  useOrganizerStore.setState({ status: "ready", errorCode: null, inbox: [NODE], suggestions: [] });
  useDuplicatesStore.setState({ status: "ready", errorCode: null, groups: [] });
  useSnapshotsStore.setState({ status: "ready", errorCode: null, snapshots: [{ id: "s1", message: "m", createdAt: "2026-01-01T00:00:00Z" }], diff: null });
  useActivityStore.setState({ status: "ready", errorCode: null, entries: [{ id: "a1", when: "2026-01-01T00:00:00Z", what: "did", undoable: true }] });
  useSyncStore.setState({ status: "ready", errorCode: null, syncState: "idle", outbox: [] });
  useConflictsStore.setState({ status: "ready", errorCode: null, conflicts: [] });
  useTransfersStore.setState({ status: "ready", errorCode: null, jobs: [{ id: "j1", name: "f", state: "done", progress: 1 }] });
  useTrashStore.setState({ status: "ready", errorCode: null, items: [NODE] });
  useSettingsStore.setState({ status: "ready", errorCode: null, language: "en-US", settings: {} });
}

beforeEach(() => {
  cleanup();
  seedReady();
});

describe("views render ready state with testids and actions", () => {
  it("onboarding", () => {
    render(<OnboardingView />);
    expect(screen.getByTestId("onboarding-connect")).toHaveAttribute("data-action", "accounts-connect");
  });
  it("repos", () => {
    render(<RepoListView />);
    expect(screen.getByTestId("repos-create")).toBeDefined();
  });
  it("overview", () => {
    render(<RepoOverviewView />);
    expect(screen.getByTestId("overview-stats")).toBeDefined();
  });
  it("browser", () => {
    render(<BrowserView />);
    expect(screen.getByTestId("browser-sort")).toBeDefined();
    expect(screen.getByTestId("browser-filter")).toBeDefined();
  });
  it("inspector", () => {
    render(<InspectorView />);
    expect(screen.getByTestId("inspector-save")).toBeDefined();
  });
  it("editor", () => {
    render(<EditorView />);
    expect(screen.getByTestId("editor-save")).toBeDefined();
  });
  it("previewer", () => {
    render(<PreviewerView />);
    expect(screen.getByTestId("previewer-load")).toBeDefined();
  });
  it("search", () => {
    render(<SearchView />);
    expect(screen.getByTestId("search-run")).toBeDefined();
  });
  it("tags", () => {
    render(<TagsView />);
    expect(screen.getByTestId("tags-create")).toBeDefined();
  });
  it("inbox", () => {
    render(<InboxOrganizerView />);
    expect(screen.getByTestId("inbox-suggest")).toBeDefined();
  });
  it("duplicates", () => {
    render(<DuplicatesView />);
    expect(screen.getByTestId("duplicates-scan")).toBeDefined();
  });
  it("snapshots", () => {
    render(<SnapshotsView />);
    expect(screen.getByTestId("snapshots-create")).toBeDefined();
  });
  it("activity", () => {
    render(<ActivityView />);
    expect(screen.getByTestId("activity-item-a1")).toBeDefined();
  });
  it("sync", () => {
    render(<SyncView />);
    expect(screen.getByTestId("sync-now")).toBeDefined();
  });
  it("transfers", () => {
    render(<TransfersView />);
    expect(screen.getByTestId("transfers-upload")).toBeDefined();
  });
  it("trash", () => {
    render(<TrashView />);
    expect(screen.getByTestId("trash-empty")).toBeDefined();
  });
  it("settings", () => {
    render(<SettingsView />);
    expect(screen.getByTestId("settings-save")).toBeDefined();
  });
  it("about", () => {
    render(<AboutDiagnosticsView />);
    expect(screen.getByTestId("about-export")).toBeDefined();
  });
});

describe("views render every required state", () => {
  it("onboarding states", () => {
    for (const s of STATUSES) {
      cleanup();
      useAccountsStore.setState({ status: s.status, errorCode: s.code, accounts: [] });
      render(<OnboardingView />);
      expect(screen.getByTestId(`onboarding-${s.suffix}`)).toBeDefined();
      cleanup();
    }
  });
  it("repos states", () => {
    for (const s of STATUSES) {
      cleanup();
      useReposStore.setState({ status: s.status, errorCode: s.code, repos: [], overview: null });
      render(<RepoListView />);
      expect(screen.getByTestId(`repos-${s.suffix}`)).toBeDefined();
      cleanup();
    }
  });
  it("browser states", () => {
    for (const s of STATUSES) {
      cleanup();
      useTreeStore.setState({ status: s.status, errorCode: s.code, nodes: [] });
      render(<BrowserView />);
      expect(screen.getByTestId(`browser-${s.suffix}`)).toBeDefined();
      cleanup();
    }
  });
  it("editor states", () => {
    for (const s of STATUSES) {
      cleanup();
      useEditorStore.setState({ status: s.status, errorCode: s.code });
      render(<EditorView />);
      expect(screen.getByTestId(`editor-${s.suffix}`)).toBeDefined();
      cleanup();
    }
  });
  it("search/tags/inbox/duplicates/snapshots/activity/transfers/trash/settings states", () => {
    const cases: { render: () => void; set: (s: (typeof STATUSES)[number]) => void; prefix: string }[] = [
      { render: () => { render(<SearchView />); }, set: (s) => { useSearchStore.setState({ status: s.status, errorCode: s.code, results: [] }); }, prefix: "search" },
      { render: () => { render(<TagsView />); }, set: (s) => { useTagsStore.setState({ status: s.status, errorCode: s.code, tags: [], suggestions: [] }); }, prefix: "tags" },
      { render: () => { render(<InboxOrganizerView />); }, set: (s) => { useOrganizerStore.setState({ status: s.status, errorCode: s.code, inbox: [] }); }, prefix: "inbox" },
      { render: () => { render(<DuplicatesView />); }, set: (s) => { useDuplicatesStore.setState({ status: s.status, errorCode: s.code, groups: [] }); }, prefix: "duplicates" },
      { render: () => { render(<SnapshotsView />); }, set: (s) => { useSnapshotsStore.setState({ status: s.status, errorCode: s.code, snapshots: [] }); }, prefix: "snapshots" },
      { render: () => { render(<ActivityView />); }, set: (s) => { useActivityStore.setState({ status: s.status, errorCode: s.code, entries: [] }); }, prefix: "activity" },
      { render: () => { render(<TransfersView />); }, set: (s) => { useTransfersStore.setState({ status: s.status, errorCode: s.code, jobs: [] }); }, prefix: "transfers" },
      { render: () => { render(<TrashView />); }, set: (s) => { useTrashStore.setState({ status: s.status, errorCode: s.code, items: [] }); }, prefix: "trash" },
      { render: () => { render(<SettingsView />); }, set: (s) => { useSettingsStore.setState({ status: s.status, errorCode: s.code }); }, prefix: "settings" },
    ];
    for (const c of cases) {
      for (const s of STATUSES) {
        cleanup();
        c.set(s);
        c.render();
        expect(screen.getByTestId(`${c.prefix}-${s.suffix}`)).toBeDefined();
        cleanup();
      }
    }
  });
  it("sync/conflicts/about/overview/inspector/previewer states", () => {
    for (const s of STATUSES) {
      cleanup();
      useSyncStore.setState({ status: s.status, errorCode: s.code, syncState: "idle", outbox: [] });
      useConflictsStore.setState({ status: s.status, errorCode: s.code, conflicts: [] });
      render(<SyncView />);
      expect(screen.getByTestId(`sync-${s.suffix}`)).toBeDefined();
      cleanup();
    }
    for (const s of STATUSES) {
      cleanup();
      useSettingsStore.setState({ status: s.status, errorCode: s.code });
      render(<AboutDiagnosticsView />);
      expect(screen.getByTestId(`about-${s.suffix}`)).toBeDefined();
      cleanup();
    }
    for (const s of STATUSES) {
      cleanup();
      useReposStore.setState({ status: s.status, errorCode: s.code, repos: [], overview: null });
      render(<RepoOverviewView />);
      expect(screen.getByTestId(`overview-${s.suffix}`)).toBeDefined();
      cleanup();
    }
    for (const s of STATUSES) {
      cleanup();
      useTreeStore.setState({ status: s.status, errorCode: s.code, nodes: [] });
      render(<InspectorView />);
      expect(screen.getByTestId(`inspector-${s.suffix}`)).toBeDefined();
      cleanup();
      useTreeStore.setState({ status: s.status, errorCode: s.code, nodes: [] });
      render(<PreviewerView />);
      expect(screen.getByTestId(`previewer-${s.suffix}`)).toBeDefined();
      cleanup();
    }
  });
});

describe("a11y hooks", () => {
  it("every button has accessible name and hooks", () => {
    render(<BrowserView />);
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThan(0);
    for (const b of buttons) {
      const hasText = b.textContent.trim() !== "";
      expect(hasText || b.getAttribute("aria-label")).toBeTruthy();
      expect(b).toHaveAttribute("data-testid");
      expect(b).toHaveAttribute("data-action");
    }
  });
});
