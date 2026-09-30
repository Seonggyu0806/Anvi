using System.Text.Json;

namespace DiePipeline.Core.Configuration;

/// <summary>
/// 레시피 안의 노드 하나 — JSON 객체 하나를 그대로 담은 그릇.
///
/// ★ Params가 왜 JsonElement(약타입)인가:
///   노드 타입마다 파라미터가 완전히 다르다(Blur는 kernel/sigma, Label은 minArea).
///   여기서 강타입으로 못 박으면 노드를 추가할 때마다 Core를 고쳐야 한다 — 코어 불변이 깨진다.
///   그래서 Core는 '파싱하지 않은 채로' 들고만 있고, 뜻을 아는 팩토리가 자기 기준으로 읽는다.
/// </summary>
public sealed class NodeConfig
{
    /// <summary>레시피 안의 고유 식별자.</summary>
    public required string Id { get; init; }

    /// <summary>명부의 키. JSON의 "type"과 같은 문자열.</summary>
    public required string Type { get; init; }

    /// <summary>
    /// 꺼진 노드는 <b>실행에서 통째로 빠진다</b>. 화면의 '노드 끄기' 체크박스가 여기에 붙는다.
    ///
    /// ★ 끄면 출력이 안 생기는데 뒷 노드는 그 이름표를 계속 읽는다.
    ///   그래서 끄는 대신 <b>별칭</b>을 건다 — "이 노드의 출력 = 이 노드의 입력".
    ///   뒷 노드는 이름표를 그대로 두고도 윗 것을 읽게 된다(NodeRegistry.CreateAll).
    ///
    /// ★ 그래서 <b>입력 1개·출력 1개인 노드만 끌 수 있다.</b>
    ///   입력이 둘이면 "어느 쪽을 물려줄지"가 정해지지 않는다. 검증기가 미리 막는다.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>포트 이름 → 칠판의 이름표. 예: { "src": "roi" }</summary>
    public Dictionary<string, string> Inputs { get; init; } = [];

    /// <summary>예: { "result": "blurred" }</summary>
    public Dictionary<string, string> Outputs { get; init; } = [];

    /// <summary>미지정이면 ValueKind == JsonValueKind.Undefined.</summary>
    public JsonElement Params { get; init; }
}