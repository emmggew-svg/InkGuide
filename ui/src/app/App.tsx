import { useNavStore } from "@core/stores";
import { useT } from "@core/useT";
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

const ROUTES = [
  "onboarding",
  "repos",
  "overview",
  "browser",
  "inspector",
  "editor",
  "previewer",
  "search",
  "tags",
  "inbox",
  "duplicates",
  "snapshots",
  "activity",
  "sync",
  "transfers",
  "trash",
  "settings",
  "about",
] as const;

export type RouteId = (typeof ROUTES)[number];

export function routeView(route: string): React.JSX.Element {
  switch (route) {
    case "onboarding":
      return <OnboardingView />;
    case "repos":
      return <RepoListView />;
    case "overview":
      return <RepoOverviewView />;
    case "browser":
      return <BrowserView />;
    case "inspector":
      return <InspectorView />;
    case "editor":
      return <EditorView />;
    case "previewer":
      return <PreviewerView />;
    case "search":
      return <SearchView />;
    case "tags":
      return <TagsView />;
    case "inbox":
      return <InboxOrganizerView />;
    case "duplicates":
      return <DuplicatesView />;
    case "snapshots":
      return <SnapshotsView />;
    case "activity":
      return <ActivityView />;
    case "sync":
      return <SyncView />;
    case "transfers":
      return <TransfersView />;
    case "trash":
      return <TrashView />;
    case "settings":
      return <SettingsView />;
    case "about":
      return <AboutDiagnosticsView />;
    default:
      return <OnboardingView />;
  }
}

export function App(): React.JSX.Element {
  const t = useT();
  const route = useNavStore((s) => s.route);
  const go = useNavStore((s) => s.go);

  return (
    <div className="shell">
      <header className="shell__header">
        <h1 data-testid="app-title" data-action="app-title">
          {t("app.title")}
        </h1>
      </header>
      <nav className="shell__nav" aria-label={t("app.nav")}>
        <ul className="shell__list">
          {ROUTES.map((r) => (
            <li key={r} className="shell__item">
              <button
                type="button"
                data-testid={`nav-${r}`}
                data-action={`nav-${r}`}
                onClick={() => {
                  go(r);
                }}
              >
                {r}
              </button>
            </li>
          ))}
        </ul>
      </nav>
      <main className="shell__main">{routeView(route)}</main>
    </div>
  );
}

export { ROUTES };
