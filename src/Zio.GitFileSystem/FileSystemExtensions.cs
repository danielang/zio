using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Text;
using Zio.FileSystems;

namespace Zio
{
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Gets or create a <see cref="GitFileSystem"/> from an existing filesystem and the specified sub folder
        /// </summary>
        /// <param name="fs">The filesystem to derive a new sub-filesystem from it</param>
        /// <param name="subFolder">The folder of the sub-filesystem</param>
        /// <param name="committishOrBranchSpec"></param>
        /// <param name="owned"></param>
        /// <returns>A git-filesystem</returns>
        public static GitFileSystem GetOrCreateGitFileSystem(this PhysicalFileSystem fs, UPath subFolder, string committishOrBranchSpec, bool owned = true)
        {
            if (!fs.DirectoryExists(subFolder))
            {
                fs.CreateDirectory(subFolder);
            }

            return new GitFileSystem(fs, subFolder, committishOrBranchSpec, owned);
        }
    }
}
