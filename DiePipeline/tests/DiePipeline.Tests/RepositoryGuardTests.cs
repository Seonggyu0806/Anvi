using DiePipeline.Core.Configuration;

namespace DiePipeline.Tests;

/// <summary>
/// 저장하려는 곳이 레포 안인가 — 원본에서 나온 것은 레포 밖에만 둔다. ★ 임시 폴더에 가짜 저장소를 만들어 본다.
/// </summary>
public sealed class RepositoryGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dp_guard_" + Guid.NewGuid().ToString("N"));

    public RepositoryGuardTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_folder, "repo", "samples", "wafer"));
        Directory.CreateDirectory(Path.Combine(_folder, "outside"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>★ 저장소 안 깊은 곳 · 아직 없는 파일도 안으로 본다 — 저장하기 전에 묻는다.</summary>
    [Fact]
    public void 저장소_안이면_그_저장소를_알려준다()
    {
        string repo = Path.Combine(_folder, "repo");

        Assert.Equal(repo, RepositoryGuard.RepositoryOf(Path.Combine(repo, "samples", "wafer", "new.case.json")));
        Assert.Equal(repo, RepositoryGuard.RepositoryOf(Path.Combine(repo, "samples")));
        Assert.True(RepositoryGuard.IsInsideRepository(Path.Combine(repo, "golden.png")));
    }

    /// <summary>저장소 밖이면 null — 이름이 비슷해도(repo2) 밖이다.</summary>
    [Fact]
    public void 저장소_밖이면_없다()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "repo2"));

        Assert.Null(RepositoryGuard.RepositoryOf(Path.Combine(_folder, "outside", "a.case.json")));
        Assert.Null(RepositoryGuard.RepositoryOf(Path.Combine(_folder, "repo2", "a.png")));
    }

    /// <summary>작업 트리(worktree)는 .git 이 폴더가 아니라 파일이다 — 그것도 저장소다.</summary>
    [Fact]
    public void 작업_트리의_git_파일도_저장소()
    {
        string tree = Path.Combine(_folder, "outside", "tree");
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(tree, ".git"), "gitdir: ../../repo/.git/worktrees/tree");

        Assert.Equal(tree, RepositoryGuard.RepositoryOf(Path.Combine(tree, "x.png")));
    }
}