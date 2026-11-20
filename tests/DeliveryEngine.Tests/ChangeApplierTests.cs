using DeliveryEngine.Delivery;
using DeliveryEngine.Tasks;
using DeliveryEngine.Tests.Support;

namespace DeliveryEngine.Tests;

public class ChangeApplierTests
{
    private static Change Write(string file, string content) => new() { Op = ChangeOperation.Write, File = file, Content = content };

    private static Change Delete(string file) => new() { Op = ChangeOperation.Delete, File = file };

    // Diff в тестах написан raw-строками, а файл с тестами на Windows может быть сохранён с CRLF
    // Тестовые файлы в репозитории с LF, поэтому приводим diff к LF
    private static Change Patch(string diff) => new() { Op = ChangeOperation.Patch, Diff = diff.ReplaceLineEndings("\n") };

    [Fact]
    public async Task Write_CreatesFileWithNestedFolders()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Write("src/deep/a.txt", "hello")]);

        Assert.Equal("hello", repo.ReadFile("src/deep/a.txt"));
    }

    [Fact]
    public async Task Write_ReplacesExistingFile()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Write("README.md", "new")]);

        Assert.Equal("new", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task Write_KeepsContentBytesExactly_NoBomNoLineEndingChanges()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Write("a.txt", "привет\r\nмир\n")]);

        var bytes = File.ReadAllBytes(repo.FullPath("a.txt"));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("привет\r\nмир\n"), bytes);
    }

    [Fact]
    public async Task Write_BackslashPath_IsTreatedAsFolder()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Write(@"src\a.txt", "x")]);

        Assert.Equal("x", repo.ReadFile("src/a.txt"));
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Delete("README.md")]);

        Assert.False(File.Exists(repo.FullPath("README.md")));
    }

    [Fact]
    public async Task Delete_MissingFile_Throws()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var ex = await Assert.ThrowsAsync<ChangeApplyException>(() =>
            ChangeApplier.ApplyAsync(repo.Git, [Delete("no-such.txt")]));

        Assert.Contains("changes[0]", ex.Message);
    }

    [Fact]
    public async Task Patch_AppliesUnifiedDiff()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git, [Patch("""
            --- a/README.md
            +++ b/README.md
            @@ -1 +1 @@
            -# Test repo
            +# Patched repo

            """)]);

        Assert.Equal("# Patched repo\n", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task Patch_NotMatchingFile_Throws()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var ex = await Assert.ThrowsAsync<ChangeApplyException>(() => ChangeApplier.ApplyAsync(repo.Git, [Patch("""
            --- a/README.md
            +++ b/README.md
            @@ -1 +1 @@
            -# Something else
            +# Patched repo

            """)]));

        Assert.Contains("patch не применился", ex.Message);
        Assert.Equal("# Test repo\n", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task Patch_PathOutsideRepo_IsRejectedByGit()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await Assert.ThrowsAsync<ChangeApplyException>(() => ChangeApplier.ApplyAsync(repo.Git, [Patch("""
            --- /dev/null
            +++ b/../outside.txt
            @@ -0,0 +1 @@
            +x

            """)]));

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(repo.Path)!, "outside.txt")));
    }

    [Fact]
    public async Task Changes_AreAppliedInOrder()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await ChangeApplier.ApplyAsync(repo.Git,
        [
            Write("a.txt", "line\n"),
            Patch("""
                --- a/a.txt
                +++ b/a.txt
                @@ -1 +1 @@
                -line
                +patched

                """),
        ]);

        Assert.Equal("patched\n", repo.ReadFile("a.txt"));
    }

    [Fact]
    public async Task Write_ThroughSymlinkedFolder_IsRejected()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var outside = Directory.CreateTempSubdirectory("delivery-engine-outside-").FullName;
        try
        {
            try
            {
                Directory.CreateSymbolicLink(repo.FullPath("link"), outside);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // На Windows без режима разработчика создавать симлинки нельзя — тогда проверять нечего.
                return;
            }

            var ex = await Assert.ThrowsAsync<ChangeApplyException>(() =>
                ChangeApplier.ApplyAsync(repo.Git, [Write("link/evil.txt", "x")]));

            Assert.Contains("симлинк", ex.Message);
            Assert.False(File.Exists(Path.Combine(outside, "evil.txt")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}