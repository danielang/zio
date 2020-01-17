using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Zio.Exceptions;

namespace Zio.FileSystems
{
    public class GitFileSystem : ReadOnlyFileSystem
    {

        /// <summary>
        /// Gets the sub path relative to the delegate <see cref="ComposeFileSystem.NextFileSystem"/>
        /// </summary>
        public UPath SubPath { get; }

        public Repository Repository { get; }

        protected Tree Tree { get => Commit?.Tree; }

        private Commit _commit;
        public Commit Commit
        {
            get
            {
                if (_commit != null)
                {
                    return _commit;
                }
                else if (Branch != null)
                {
                    return Repository.Lookup<Commit>(Branch.Reference.CanonicalName);
                }

                return null;
            }

            private set
            {
                _commit = value;
            }
        }

        private Branch Branch { get; }

        // ----------------------------------------------
        // Constructor as SubFileSystem
        // ----------------------------------------------

        protected GitFileSystem(IFileSystem fileSystem, UPath subPath, bool owned) : base(fileSystem, owned)
        {
            SubPath = subPath.AssertAbsolute(nameof(subPath));

            if (!fileSystem.DirectoryExists(SubPath))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path `{SubPath}`.");
            }

            var ioPath = fileSystem.ConvertPathToInternal(SubPath);

            if (!Repository.IsValid(ioPath))
            {
                throw new InvalidRepositoryException($"Could not find a valid repository in path `{SubPath}`.");
            }

            Repository = new Repository(ioPath);
        }

        internal GitFileSystem(IFileSystem fileSystem, UPath subPath, string committishOrBranchSpec, bool owned = true) : this(fileSystem, subPath, owned)
        {
            Reference reference;
            GitObject obj;

            Repository.RevParse(committishOrBranchSpec, out reference, out obj);
            if (reference != null && reference.IsLocalBranch)
            {
                Branch = Repository.Branches[reference.CanonicalName];
            }
            else
            {
                Commit = obj.Peel<Commit>();
            }
        }

        // ----------------------------------------------
        // Constructor from Repository
        // ----------------------------------------------

        protected GitFileSystem(Repository repository) : base(new PhysicalFileSystem(), true)
        {
            Repository = repository;

            var subPath = NextFileSystemSafe.ConvertPathFromInternal(Repository.Info.WorkingDirectory);

            SubPath = subPath.AssertAbsolute(nameof(subPath));

            if (!NextFileSystemSafe.DirectoryExists(SubPath))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path `{SubPath}`.");
            }
        }

        public GitFileSystem(Repository repository, string committishOrBranchSpec) : this(repository)
        {
            Reference reference;
            GitObject obj;

            Repository.RevParse(committishOrBranchSpec, out reference, out obj);
            if (reference != null && reference.IsLocalBranch)
            {
                Branch = Repository.Branches[reference.CanonicalName];
            }
            else
            {
                Commit = obj.Peel<Commit>();
            }
        }

        public GitFileSystem(Repository repository, Commit commit) : this(repository)
        {
            Commit = commit;
        }

        public GitFileSystem(Repository repository, Branch branch) : this(repository)
        {
            if (branch.IsRemote)
            {
                throw new Exception("Branch must be locale");
            }

            Branch = branch;
        }

        // ----------------------------------------------
        // Disposing
        // ----------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Repository?.Dispose();
            }
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(UPath path)
        {
            if (path == "/")
            {
                return true;
            }

            var treeEntry = GetTreeEntryFromPath(path);

            return treeEntry != null && treeEntry.TargetType == TreeEntryTargetType.Tree;
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override long GetFileLengthImpl(UPath path)
        {
            var blob = GetBlobFromPath(path);

            return blob.Size;
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(UPath path)
        {
            var treeEntry = GetTreeEntryFromPath(path);

            return treeEntry != null && treeEntry.TargetType == TreeEntryTargetType.Blob;
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            if ((access & FileAccess.Write) != 0)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            var blob = GetBlobFromPath(path);

            return blob.GetContentStream();
        }

        private TreeEntry GetTreeEntryFromPath(UPath path)
        {
            var treePath = path.ToRelative();

            return Tree[treePath.FullName];
        }

        private Blob GetBlobFromPath(UPath path)
        {
            var treeEntry = GetTreeEntryFromPath(path);

            if (treeEntry == null || treeEntry.TargetType != TreeEntryTargetType.Blob)
            {
                throw new Exception("Not a file.");
            }

            return treeEntry.Target as Blob;
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            var treeEntry = GetTreeEntryFromPath(path);

            if (treeEntry == null && path == "/")
            {
                // the root of a git repository is a directory
                return FileAttributes.Directory | FileAttributes.ReadOnly;
            }
            else
            {
                switch (treeEntry.TargetType)
                {
                    case TreeEntryTargetType.Blob:
                        return FileAttributes.Normal | FileAttributes.ReadOnly;

                    case TreeEntryTargetType.Tree:
                        return FileAttributes.Directory | FileAttributes.ReadOnly;
                }
            }

            return FileAttributes.ReadOnly;
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            // TODO
            return Commit.Author.When.UtcDateTime;
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            // TODO
            return Commit.Author.When.UtcDateTime;
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            // TODO
            return Commit.Author.When.UtcDateTime;

            //var treeEntry = GetTreeEntryFromPath(path);

            //var commit = Repository.Lookup<Commit>(treeEntry.Target.Id);

            //// return new DateTime(commit.Author.When);

            //return NextFileSystemSafe.GetLastWriteTime(ConvertPathToDelegate(path));
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var treePath = path.ToRelative();
            Tree tree;

            if (treePath.IsEmpty)
            {
                tree = Tree;
            }
            else
            {
                var treeEntry = GetTreeEntryFromPath(path);

                tree = Repository.Lookup<Tree>(treeEntry.Target.Sha);
            }

            var enumerator = tree.GetEnumerator();

            while (enumerator.MoveNext())
            {
                TreeEntry subTreeEntry = enumerator.Current;
                UPath subTreeEntryPath = new UPath(subTreeEntry.Path);

                if (subTreeEntry.TargetType == TreeEntryTargetType.Tree && searchOption == SearchOption.AllDirectories)
                {
                    foreach (var subpath in EnumeratePathsImpl(path / subTreeEntryPath, searchPattern, searchOption, searchTarget))
                    {
                        yield return subpath;
                    }
                }

                if (searchTarget == SearchTarget.Directory && subTreeEntry.TargetType != TreeEntryTargetType.Tree)
                {
                    continue;
                }
                else if (searchTarget == SearchTarget.File && subTreeEntry.TargetType != TreeEntryTargetType.Blob || !search.Match(Path.GetFileName(subTreeEntryPath.GetName())))
                {
                    continue;
                }

                yield return path / subTreeEntryPath;
            }
        }

        // ----------------------------------------------
        // Watch API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override bool CanWatchImpl(UPath path)
        {
            return false;
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            return null;
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override UPath ConvertPathToDelegate(UPath path)
        {
            var safePath = path.ToRelative();
            return SubPath / safePath;
        }

        /// <inheritdoc />
        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            var fullPath = path.FullName;
            if (!fullPath.StartsWith(SubPath.FullName) || (fullPath.Length > SubPath.FullName.Length && fullPath[SubPath.FullName.Length] != UPath.DirectorySeparator))
            {
                // More a safe guard, as it should never happen, but if a delegate filesystem doesn't respect its root path
                // we are throwing an exception here
                throw new InvalidOperationException($"The path `{path}` returned by the delegate filesystem is not rooted to the subpath `{SubPath}`");
            }

            var subPath = fullPath.Substring(SubPath.FullName.Length);
            return subPath == string.Empty ? UPath.Root : new UPath(subPath); // TODO: original: new UPath(subPath, true);
        }
    }
}
