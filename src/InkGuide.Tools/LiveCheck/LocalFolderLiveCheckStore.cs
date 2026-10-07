using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InkGuide.Tools.LiveCheck;

/// <summary>
/// Minimal filesystem-backed store that mirrors the provider semantics the
/// live-check exercises (folders, upload, list, rename, trash, change journal).
/// Test-only scaffolding for the smoke test; not a product provider.
/// </summary>
internal sealed class LocalFolderLiveCheckStore
{
    private readonly string _root;
    private readonly TimeProvider _timeProvider;

    public LocalFolderLiveCheckStore(string root, TimeProvider timeProvider)
    {
        _root = Path.GetFullPath(root);
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_root);
    }

    public Task<string> CreateFolderAsync(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        AppendJournal("create-folder|" + name);
        return Task.FromResult(name);
    }

    public async Task<string> UploadFileAsync(string folderId, string name, byte[] content)
    {
        string path = Inside(folderId, name);
        await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
        AppendJournal("upload|" + folderId + "/" + name);
        return name;
    }

    public Task<string[]> ListChildrenAsync(string folderId)
    {
        string dir = Inside(folderId);
        string[] names = Directory.Exists(dir)
            ? Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToArray()
            : Array.Empty<string>();
        return Task.FromResult(names);
    }

    public Task RenameAsync(string folderId, string fileId, string newName)
    {
        File.Move(Inside(folderId, fileId), Inside(folderId, newName));
        AppendJournal("rename|" + folderId + "/" + fileId + "->" + newName);
        return Task.CompletedTask;
    }

    public Task TrashAsync(string folderId, string fileId)
    {
        string trash = Path.Combine(_root, ".trash");
        Directory.CreateDirectory(trash);
        string dest = Path.Combine(trash, folderId + "__" + fileId);
        if (File.Exists(Inside(folderId, fileId)))
        {
            File.Move(Inside(folderId, fileId), dest, overwrite: true);
        }

        AppendJournal("trash|" + folderId + "/" + fileId);
        return Task.CompletedTask;
    }

    public Task<int> GetChangeCountAsync()
    {
        string journal = Path.Combine(_root, ".journal.log");
        if (!File.Exists(journal))
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(File.ReadAllLines(journal).Length);
    }

    public Task CleanupAsync(string folderName)
    {
        string dir = Path.Combine(_root, folderName);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        AppendJournal("cleanup|" + folderName);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string folderName) =>
        Task.FromResult(Directory.Exists(Path.Combine(_root, folderName)));

    private string Inside(params string[] parts)
    {
        string combined = _root;
        foreach (string part in parts)
        {
            combined = Path.Combine(combined, part);
        }

        string full = Path.GetFullPath(combined);
        if (!full.Equals(_root, StringComparison.Ordinal) && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path escapes the live-check root.");
        }

        return full;
    }

    private void AppendJournal(string line)
    {
        File.AppendAllText(
            Path.Combine(_root, ".journal.log"),
            _timeProvider.GetUtcNow().ToString("O") + " " + line + Environment.NewLine);
    }
}
