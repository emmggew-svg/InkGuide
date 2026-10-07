using System;
using System.Windows;

namespace InkGuide.App.Hosting;

/// <summary>
/// Plain native dialogs only. No styling, no branding — purely functional,
/// as required for startup/error paths (MASTER_PROMPT Sections 4 and 14.1).
/// </summary>
internal static class NativeDialog
{
    private const string TitlePrefix = "InkGuide";
    private const string MissingRuntimeText =
        "The Microsoft Edge WebView2 Runtime was not found on this computer.\n" +
        "\n" +
        "InkGuide needs it to display its interface.\n" +
        "Download the Evergreen Bootstrapper from:\n";
    private const string MissingUiText =
        "The bundled user interface was not found.\n" +
        "\n" +
        "Expected a UI bundle next to the executable or a valid ui-override folder.\n" +
        "Run build\\publish.cmd or point --ui-dev-url at a dev server.";
    private const string ResetConfirmText =
        "Clear InkGuide cache, settings and window state?\n" +
        "\n" +
        "Your files on Google Drive are not affected. Queued (not yet synced) changes are preserved.";
    private const string FatalTextPrefix = "An unexpected error occurred. Details were written to the log file.\n\n";

    public static void ShowMissingRuntime(string evergreenLink)
    {
        MessageBox.Show(
            MissingRuntimeText + evergreenLink,
            TitlePrefix,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public static void ShowMissingUi(string details)
    {
        MessageBox.Show(
            MissingUiText + "\n\n" + details,
            TitlePrefix,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public static void ShowInfo(string text)
    {
        MessageBox.Show(text, TitlePrefix, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void ShowFatal(string detailLine)
    {
        MessageBox.Show(
            FatalTextPrefix + detailLine,
            TitlePrefix,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public static bool ConfirmReset()
    {
        return MessageBox.Show(
            ResetConfirmText,
            TitlePrefix,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
