using System.Collections.Generic;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>Drive v3 wire constants (endpoints, scopes, fields, appProperties keys).</summary>
internal static class DriveConstants
{
    public const string ApiBase = "https://www.googleapis.com/drive/v3";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    public const string ScopeFile = "https://www.googleapis.com/auth/drive.file";
    public const string ScopeDrive = "https://www.googleapis.com/auth/drive";

    public const string FolderMime = "application/vnd.google-apps.folder";
    public const string ShortcutMime = "application/vnd.google-apps.shortcut";

    /// <summary>Partial-response fields for every file resource (Section 7.2).</summary>
    public const string FileFields = "id, name, mimeType, parents, size, md5Checksum, modifiedTime, createdTime, version, headRevisionId, trashed, explicitlyTrashed, description, appProperties, webViewLink, capabilities(canEdit,canDelete,canTrash,canRename,canMoveItemWithinDrive)";

    /// <summary>Partial-response fields for changes.list.</summary>
    public const string ChangesFields = "nextPageToken,newStartPageToken,changes(fileId,removed,time,file(" + FileFields + "))";

    /// <summary>appProperties keys (inkguide.* per D-002).</summary>
    public static class AppKeys
    {
        public const string Kind = "inkguide.k";
        public const string Source = "inkguide.s";
        public const string DescriptionHash = "inkguide.dh";
        public const string ContentHash = "inkguide.ch";
        public const string OperationId = "inkguide.op";
    }

    public static IReadOnlyList<string> ScopesFor(bool fullDrive)
    {
        return fullDrive ? new[] { ScopeDrive } : new[] { ScopeFile };
    }
}
