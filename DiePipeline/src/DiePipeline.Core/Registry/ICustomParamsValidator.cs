using DiePipeline.Core.Configuration;

namespace DiePipeline.Core.Registry;

/// <summary>
/// 서술자(<see cref="ParamDescriptor"/>)로 표현할 수 없는 파라미터를 <b>팩토리가 직접 검증</b>한다.
///
/// ★ 왜 필요한가: ParamDescriptor는 "이름 하나 = 값 하나"만 표현한다.
///   Filter의 <c>filters[]</c>는 <b>순서 있는 목록</b>이고 항목마다 파라미터가 다르다 —
///   서술자 한 줄로 못 그린다. 그래서 그런 노드만 이 통로로 빠져나간다.
///
/// ★ 대신 규칙이 하나 있다 — <b>검증을 새로 짜지 않는다.</b>
///   항목별 규칙도 ParamDescriptor로 적고 <see cref="ParamCheck"/>로 검사한다.
///   그래야 "서술자가 단일 진실원"이 목록 안쪽에서도 유지된다.
/// </summary>
public interface ICustomParamsValidator
{
    /// <summary>오류 메시지를 낸다. 문제가 없으면 아무것도 안 낸다.</summary>
    IEnumerable<string> ValidateParams(NodeConfig config);
}