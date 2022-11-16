using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RepoLink
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var activeDocument = await VS.Documents.GetActiveDocumentViewAsync();
            if (activeDocument == null)
            {
                // TODO: There's no file selected. Show error.
                return;
            }
            var filePath = activeDocument.Document.FilePath;
            var directoryName = Path.GetDirectoryName(filePath);

            // Confirm that the directory contains a git repo.
            var gitDir = RunCommand("git", "rev-parse --git-dir", directoryName);
            if (string.IsNullOrWhiteSpace(gitDir))
            {
                // TODO: This is not a git repository. Show error.
                return;
            }

            // Get misc information about the repo. Remote, branch, etc.
            var upstream = RunCommand("git", "rev-parse --abbrev-ref --symbolic-full-name @{upstream}", directoryName);
            if (string.IsNullOrWhiteSpace(upstream))
            {
                // TODO: No remote configured, show an error message.
                return;
            }
            var splitUpstream = upstream.Split(new[] { '/' }, 2);
            var remote = splitUpstream[0];
            var branch = splitUpstream[1];

            // Get the git repo url.
            var repo = RunCommand("git", $"config --get remote.{remote}.url", directoryName);

            // Get the repo path.
            var prefix = RunCommand("git", "rev-parse --show-prefix", directoryName);
            var fileName = Path.GetFileName(filePath);
            var path = $"{prefix}{fileName}";

            string link;
            if (repo.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) != -1)
            {
                link = GetDevopsLink(activeDocument, repo, branch, path);
            }
            else if (repo.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) != -1)
            {
                var commit = RunCommand("git", "rev-parse HEAD", directoryName);

                link = GetGithubLink(activeDocument, repo, commit, path);
            }
            else
            {
                // TODO: Repo not supported. Show error message.
                return;
            }

            Clipboard.SetText(link);

            VS.StatusBar.ShowMessageAsync("Link copied to clipboard.").FireAndForget();
        }

        private static string GetDevopsLink(DocumentView activeDocument, string repo, string branch, string path)
        {
            var version = $"GB{branch}"; // I don't understand why the 'GB', but is needed in order to access the branch.
            int line;
            int lineEnd;
            int lineStartColumn = 1;
            int lineEndColumn = 1;

            var selection = activeDocument.TextView.Selection;
            if (selection.IsEmpty)
            {
                // If there's no selection, we just get the current line.
                line = activeDocument.TextView.Caret.Position.BufferPosition.GetContainingLineNumber() + 1;
                lineEnd = line + 1;
            }
            else
            {
                // If there's selection, we get the caret positions.
                line = selection.Start.Position.GetContainingLineNumber() + 1;
                lineEnd = selection.End.Position.GetContainingLineNumber() + 1;

                lineStartColumn = selection.Start.Position.Position - selection.Start.Position.GetContainingLine().Start.Position + 1;
                lineEndColumn = selection.End.Position.Position - selection.End.Position.GetContainingLine().Start.Position + 1;
            }

            return $"{repo}?path=/{path}&version={version}&line={line}&lineEnd={lineEnd}&lineStartColumn={lineStartColumn}&lineEndColumn={lineEndColumn}";
        }

        private string GetGithubLink(DocumentView activeDocument, string repo, string commit, string path)
        {
            // By default, github adds a .git suffix to all repos.
            repo = repo.TrimSuffix(".git", StringComparison.OrdinalIgnoreCase);

            var line = activeDocument.TextView.Caret.Position.BufferPosition.GetContainingLineNumber() + 1;

            return $"{repo}/blob/{commit}/{path}#L{line}";
        }

        private string RunCommand(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
            };

            // TODO: Read the StandardError and report back issues.
            using var process = Process.Start(psi);
            return process.StandardOutput.ReadToEnd().TrimEnd('\n');
        }
    }
}
