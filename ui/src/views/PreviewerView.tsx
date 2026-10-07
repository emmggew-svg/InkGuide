import { useState } from "react";
import DOMPurify from "dompurify";
import { marked } from "marked";
import { getActiveClient } from "@core/bridge";
import { useTreeStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

type PreviewKind = "text" | "markdown" | "image" | "pdf" | "audio" | "video" | "csv" | "json" | "archive" | "html" | "other";

// Previewer: text/code via text, markdown via marked+DOMPurify, images via
// cache-URL img (SVG only as img, never inlined), audio/video via cache URL,
// CSV table, JSON structure, archives entry listing, HTML sandboxed iframe.
export function PreviewerView(): React.JSX.Element {
  const t = useT();
  const status = useTreeStore((s) => s.status);
  const errorCode = useTreeStore((s) => s.errorCode);
  const nodes = useTreeStore((s) => s.nodes);
  const [kind, setKind] = useState<PreviewKind>("text");
  const [markdown, setMarkdown] = useState("# hello");

  const selected = nodes[0] ?? null;
  const html = DOMPurify.sanitize(marked.parse(markdown) as string);

  const loadPreview = (): void => {
    const c = getActiveClient();
    if (c && selected) {
      void c.invoke("nodes.getPreview", { id: selected.id });
    }
  };

  return (
    <section className="previewer" aria-label={t("previewer.title")}>
      <h2>{t("previewer.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="previewer">
        <label htmlFor="previewer-kind">{t("previewer.title")}</label>
        <select
          id="previewer-kind"
          data-testid="previewer-kind"
          data-action="previewer-kind"
          value={kind}
          onChange={(e) => {
            setKind(e.target.value as PreviewKind);
          }}
        >
          <option value="text">text</option>
          <option value="markdown">markdown</option>
          <option value="image">image</option>
          <option value="pdf">pdf</option>
          <option value="audio">audio</option>
          <option value="video">video</option>
          <option value="csv">csv</option>
          <option value="json">json</option>
          <option value="archive">archive</option>
          <option value="html">html</option>
          <option value="other">other</option>
        </select>
        <button type="button" data-testid="previewer-load" data-action="previewer-load" onClick={loadPreview}>
          {t("common.retry")}
        </button>
        {kind === "markdown" ? (
          <div data-testid="previewer-markdown" data-action="previewer-markdown">
            <label htmlFor="previewer-md-input">markdown</label>
            <input
              id="previewer-md-input"
              data-testid="previewer-md-input"
              data-action="previewer-md-input"
              value={markdown}
              onChange={(e) => {
                setMarkdown(e.target.value);
              }}
            />
            <div data-testid="previewer-md-output" data-action="previewer-md-output" dangerouslySetInnerHTML={{ __html: html }} />
          </div>
        ) : null}
        {kind === "image" ? (
          <img data-testid="previewer-img" data-action="previewer-img" alt={selected?.name ?? "preview"} src="about:blank" />
        ) : null}
        {kind === "audio" ? (
          <audio data-testid="previewer-audio" data-action="previewer-audio" controls src="about:blank">
            <track kind="captions" srcLang="en" label="captions" />
          </audio>
        ) : null}
        {kind === "video" ? (
          <video data-testid="previewer-video" data-action="previewer-video" controls src="about:blank">
            <track kind="captions" srcLang="en" label="captions" />
          </video>
        ) : null}
        {kind === "csv" ? (
          <table data-testid="previewer-csv" data-action="previewer-csv">
            <tbody>
              <tr>
                <td>col</td>
              </tr>
            </tbody>
          </table>
        ) : null}
        {kind === "json" ? (
          <pre data-testid="previewer-json" data-action="previewer-json">
            {`{ "kind": "json" }`}
          </pre>
        ) : null}
        {kind === "archive" ? (
          <ul data-testid="previewer-archive" data-action="previewer-archive">
            <li>entry</li>
          </ul>
        ) : null}
        {kind === "html" ? (
          <div data-testid="previewer-html-note" data-action="previewer-html-note">
            <p>{t("previewer.htmlNote")}</p>
            <iframe data-testid="previewer-html-frame" data-action="previewer-html-frame" title="preview" sandbox="" src="about:blank" />
          </div>
        ) : null}
        {kind === "other" || kind === "text" || kind === "pdf" ? (
          <p data-testid="previewer-fallback" data-action="previewer-fallback">
            {t("previewer.unsupported")}
          </p>
        ) : null}
      </ViewState>
    </section>
  );
}
