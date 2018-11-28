using System;
using Zio;
using Zio.FileSystems;
using Xunit;

namespace Zio.GitFileSystem.Test
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            var pfs = new PhysicalFileSystem();

            var gfs = pfs.GetOrCreateGitFileSystem("/mnt/d/DanielL/Documents/Repositories/ortec-connector/v2/test/GitDirectoryBrowser/GitDirectoryBrowser/bin/Debug/netcoreapp2.1/repository", "master");

            var dirs = gfs.EnumerateDirectories("/");
            foreach (var dir in dirs)
            {
                Console.WriteLine(dir);
            }
        }
    }
}
