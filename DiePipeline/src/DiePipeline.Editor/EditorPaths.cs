namespace DiePipeline.Editor;

/// <summary>
/// 찾아보기로 고른 파일 → 레시피에 적을 경로.
///
/// ★ 레시피 안의 경로는 <b>레시피 파일이 있는 폴더 기준</b>으로 풀린다(웨이퍼 실행기와 같은 규칙).
///   그래서 고른 파일도 그 폴더 기준 상대 경로로 적는다 — 레시피와 파일을 같이 옮겨도 그대로 풀린다.
/// </summary>
public static class EditorPaths
{
    /// <summary>
    /// <paramref name="file"/> 을 <paramref name="folder"/> 기준 상대 경로로. 슬래시(<c>/</c>)로 적는다(견본과 같은 모양).
    /// ★ 기준 폴더가 없으면(아직 저장 안 한 레시피) 절대 경로. 드라이브가 달라 상대 경로가 없을 때도 절대 경로.
    /// </summary>
    public static string Relative(string? folder, string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        string full = Path.GetFullPath(file);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return full;
        }

        string relative = Path.GetRelativePath(Path.GetFullPath(folder), full);

        return Path.IsPathRooted(relative) ? relative : relative.Replace('\\', '/');
    }
}