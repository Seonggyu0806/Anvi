using DiePipeline.Core.Domain;

namespace DiePipeline.Core.Pipeline;

/// <summary>
/// 파이프라인 1회 실행의 결과.
///
/// ★ 왜 칠판을 그대로 돌려주지 않나:
///   실행이 끝나면 중간 이미지는 전부 반납된다(ReleaseAll).
///   그러니 호출자에게 넘길 수 있는 건 '반납되지 않는 것' — 즉 값 타입 결과뿐이다.
///   경계에서 값만 뽑아내면, 호출자가 죽은 이미지를 만질 방법이 아예 없어진다.
/// </summary>
public sealed record PipelineResult
{
    /// <summary>관례상 최종 결함 목록의 이름표. 레시피가 이 키로 써야 한다.</summary>
    public const string DefectListKey = "defectList";

    public required DefectList Defects { get; init; }

    public static PipelineResult From(IPipelineContext context)
    {
        // ★ 삼항 연산자(? :)로 쓰면 CS8601이 난다.
        //   TryGet<T>의 T에 제약이 없어서, 컴파일러가 "true면 값이 있다"를 확신하지 못한다.
        //   'defects is not null'로 직접 확인해 주면 납득한다.
        if (context.TryGet(DefectListKey, out DefectList? defects) && defects is not null)
        {
            return new PipelineResult { Defects = defects };
        }

        // 결함 노드가 없는 레시피(전처리만)도 유효하다.
        return new PipelineResult { Defects = DefectList.Empty };
    }
}