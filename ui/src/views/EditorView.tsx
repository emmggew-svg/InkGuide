import { useEffect, useRef } from "react";
import { EditorState, type Transaction } from "@codemirror/state";
import { EditorView as CodeMirrorView } from "@codemirror/view";
import { javascript } from "@codemirror/lang-javascript";
import { useEditorStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

// CodeMirror 6 is styled solely through CSS vars from tokens.css
// (see .editor__cm rules). No author inline colors/fonts are used.
export function EditorView(): React.JSX.Element {
  const t = useT();
  const status = useEditorStore((s) => s.status);
  const errorCode = useEditorStore((s) => s.errorCode);
  const dirty = useEditorStore((s) => s.dirty);
  const edit = useEditorStore((s) => s.edit);
  const save = useEditorStore((s) => s.save);
  const reload = useEditorStore((s) => s.reload);
  const hasFile = useEditorStore((s) => s.nodeId !== null);
  const ref = useRef<HTMLDivElement>(null);
  const viewRef = useRef<CodeMirrorView | null>(null);

  useEffect(() => {
    if (status === "idle" && hasFile) {
      void reload();
    }
  }, [status, hasFile, reload]);

  useEffect(() => {
    const host = ref.current;
    if (!host) {
      return;
    }
    const st = EditorState.create({ doc: "", extensions: [javascript()] });
    const v = new CodeMirrorView({
      state: st,
      parent: host,
      dispatch: (tr: Transaction) => {
        v.update([tr]);
        if (tr.docChanged) {
          edit(v.state.doc.toString());
        }
      },
    });
    viewRef.current = v;
    return () => {
      v.destroy();
      viewRef.current = null;
    };
  }, [edit]);

  return (
    <section className="editor" aria-label={t("editor.title")}>
      <h2>{t("editor.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="editor">
        <button type="button" data-testid="editor-refresh" data-action="editor-refresh" onClick={() => { void reload(); }}>
          {t("common.refresh")}
        </button>
        {dirty ? (
          <p data-testid="editor-dirty" data-action="editor-dirty">
            {t("editor.dirty")}
          </p>
        ) : null}
        {errorCode === "TOO_LARGE_TO_EDIT" ? (
          <p data-testid="editor-too-large" data-action="editor-too-large">
            {t("editor.tooLarge")}
          </p>
        ) : null}
        <div ref={ref} className="editor__cm" data-testid="editor-cm" data-action="editor-cm" />
        <button
          type="button"
          data-testid="editor-save"
          data-action="editor-save"
          onClick={() => {
            void save();
          }}
        >
          {t("editor.save")}
        </button>
      </ViewState>
    </section>
  );
}
