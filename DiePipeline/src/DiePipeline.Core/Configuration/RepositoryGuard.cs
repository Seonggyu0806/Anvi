namespace DiePipeline.Core.Configuration;

/// <summary>
/// 저장하려는 곳이 git 저장소(레포) 안인가.
///
/// ★ 원본에서 나온 것(영역 좌표 · 잘라낸 조각 · 골든)은 회사 자료다 — <b>레포 밖에만</b> 둔다.
///   <c>.gitignore</c> 는 2차 방어선일 뿐이고, 1차 방어선은 애초에 레포 안에 쓰지 않는 것이다.
/// ★ 화면은 레포 안이면 <b>저장을 거부</b>하고, 명령행은 경고한다 — 판단은 여기 하나다.
/// </summary>
public static class RepositoryGuard
{
    /// <summary>
    /// 이 경로(파일 또는 폴더)를 품은 저장소 폴더 — 위로 올라가며 <c>.git</c>(폴더, 또는 작업 트리의 파일)을 찾는다. 없으면 <c>null</c>.
    /// 파일은 아직 없어도 된다(저장하기 전에 묻는다).
    /// </summary>
    public static string? RepositoryOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        DirectoryInfo? folder = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full).Directory;

        for (; folder is not null; folder = folder.Parent)
        {
            string git = Path.Combine(folder.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    public static bool IsInsideRepository(string path) => RepositoryOf(path) is not null;
}