using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;

namespace DiePipeline.Editor;

/// <summary>내보내기가 검증에서 막혔을 때. 오류를 <b>전부</b> 담는다.</summary>
public sealed class RecipeExportException : Exception
{
    public RecipeExportException(IReadOnlyList<string> errors)
        : base($"레시피를 내보낼 수 없습니다. 오류 {errors.Count}개:{Environment.NewLine}  - " +
               string.Join($"{Environment.NewLine}  - ", errors))
        => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// 그래프 ↔ 레시피 양방향 변환.
///
/// ★ 둘은 이어짐을 표현하는 방식이 다르다:
///   <code>
///   그래프   선으로 잇는다.    순서가 없다.
///   레시피   이름표로 잇는다.  적힌 순서가 실행 순서다.
///   </code>
///   그래서 내보낼 때 <b>이름표와 순서를 둘 다 만들어 내야</b> 한다.
///
/// ★ 이름표 규약: 노드 출력 = <c>"{노드id}.{포트이름}"</c>, 컨텍스트 입력 = 이름표 그 자체.
///   손으로 쓴 짧은 이름(mask, golden)을 불러오면 이 형식으로 <b>다시 지어진다.</b>
///   파일은 달라지지만 <b>결과는 같다</b> — 이름이 바뀐 것뿐이다.
/// </summary>
public static class RecipeMapper
{
    public static string OutputKey(string nodeId, string portName) => $"{nodeId}.{portName}";

    /// <summary>그래프 → 레시피. <b>검증을 안 한다</b> — 일반 내보내기는 <see cref="Export"/>를 쓴다.</summary>
    public static Recipe ToRecipe(EditorGraph graph, NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);

        IReadOnlyList<EditorNode> ordered = TopologicalSort(graph);

        // 어느 노드든 입력으로 '읽어 가는' 출력 이름표. 파이프라인의 끝을 가려내는 데 쓴다.
        HashSet<string> consumed = new(
            graph.Connections
                .Where(c => c.SourceNodeId is not null)
                .Select(c => OutputKey(c.SourceNodeId!, c.SourcePort)),
            StringComparer.Ordinal);

        List<NodeConfig> nodes = new(ordered.Count);
        HashSet<string> contextKeys = new(StringComparer.Ordinal);

        foreach (EditorNode node in ordered)
        {
            Dictionary<string, string> outputs = new(StringComparer.Ordinal);

            if (registry.Contains(node.Type))
            {
                foreach (PortDescriptor port in registry.Describe(node.Type).Outputs)
                {
                    string key = OutputKey(node.Id, port.Name);

                    // ★ 아무도 안 읽는 DefectList 출력 = 파이프라인의 끝.
                    //   엔진이 결과를 꺼낼 때 쓰는 예약 이름표를 붙여 준다.
                    //   이게 없으면 그려 놓고 돌렸는데 결함이 0개로 나온다 —
                    //   계산은 다 해 놓고 <b>꺼내는 서랍 이름이 달라서</b> 못 찾는 것이다.
                    if (port.Kind == PortKind.DefectList && !consumed.Contains(key))
                    {
                        key = PipelineResult.DefectListKey;
                    }

                    outputs[port.Name] = key;
                }
            }

            Dictionary<string, string> inputs = new(StringComparer.Ordinal);

            foreach (EditorConnection connection in graph.Connections.Where(c => c.TargetNodeId == node.Id))
            {
                if (connection.SourceNodeId is null)
                {
                    // 컨텍스트 입력 — 이름표가 곧 출처다(예: "roi").
                    inputs[connection.TargetPort] = connection.SourcePort;
                    contextKeys.Add(connection.SourcePort);
                }
                else
                {
                    inputs[connection.TargetPort] = OutputKey(connection.SourceNodeId, connection.SourcePort);
                }
            }

            nodes.Add(new NodeConfig
            {
                Id = node.Id,
                Type = node.Type,
                Enabled = node.Enabled,
                Inputs = inputs,
                Outputs = outputs,
                Params = ParamsToElement(node.Params),
            });
        }

        return new Recipe
        {
            RecipeId = graph.RecipeId,
            Description = graph.Description,

            // 빈 캔버스에서 시작하면 컨텍스트 선이 하나도 없다. 그때는 예약 이름표를 기본으로 깐다.
            ContextInputs = contextKeys.Count > 0
                ? [.. contextKeys.Order(StringComparer.Ordinal)]
                : [ReservedContext.Roi, ReservedContext.ChipRegions],

            Pipeline = new PipelineSection { Nodes = nodes },
        };
    }

    /// <summary>
    /// 내보내기 = 변환 + <b>검증 강제</b>.
    ///
    /// ★ 검증을 건너뛸 수 있게 두지 않는다. 화면에서 만든 레시피가 파일로 나가는 순간부터는
    ///   <b>누가 언제 돌릴지 모른다</b> — 망가진 파일이 나가면 그걸 만든 사람은 이미 자리에 없다.
    /// </summary>
    public static Recipe Export(EditorGraph graph, NodeRegistry registry)
    {
        Recipe recipe = ToRecipe(graph, registry);
        IReadOnlyList<string> errors = RecipeValidator.Validate(recipe, registry);

        if (errors.Count > 0)
        {
            throw new RecipeExportException(errors);
        }

        return recipe;
    }

    /// <summary>레시피 → 그래프(불러오기). 이름표로 <b>만든 사람을 되짚어</b> 선을 되살린다.</summary>
    public static EditorGraph FromRecipe(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        // 이름표 → 그걸 만든 (노드, 출력 포트). 선을 되살리는 열쇠다.
        Dictionary<string, (string NodeId, string Port)> producedBy = new(StringComparer.Ordinal);

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            foreach ((string port, string key) in node.Outputs)
            {
                producedBy[key] = (node.Id, port);
            }
        }

        EditorGraph graph = new()
        {
            RecipeId = recipe.RecipeId,
            Description = recipe.Description,
        };

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            EditorNode editorNode = new() { Id = node.Id, Type = node.Type, Enabled = node.Enabled };

            if (node.Params.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in node.Params.EnumerateObject())
                {
                    // ★ Clone()이 꼭 필요하다. JsonElement는 원본 문서를 들여다보는 '창'일 뿐이라,
                    //   문서가 사라지면 창 너머가 텅 빈다. 복제해서 떼어 놓는다.
                    editorNode.Params[property.Name] = property.Value.Clone();
                }
            }

            graph.Nodes.Add(editorNode);

            foreach ((string port, string key) in node.Inputs)
            {
                graph.Connections.Add(
                    producedBy.TryGetValue(key, out (string NodeId, string Port) source)
                        ? new EditorConnection(source.NodeId, source.Port, node.Id, port)
                        : new EditorConnection(null, key, node.Id, port));   // 만든 노드가 없다 = 컨텍스트 입력
            }
        }

        return graph;
    }

    // 낱개로 들고 있던 파라미터를 JSON 객체 하나로 뭉친다. 없으면 Undefined(= 미지정).
    private static JsonElement ParamsToElement(IReadOnlyDictionary<string, JsonElement> values)
        => values.Count == 0 ? default : JsonSerializer.SerializeToElement(values);

    /// <summary>
    /// 위상정렬 — 그림에는 없던 <b>순서를 만들어 낸다</b>.
    /// "나보다 먼저 와야 할 노드가 전부 나왔으면 나도 나간다" 를 더 나갈 게 없을 때까지 되풀이한다.
    /// </summary>
    private static IReadOnlyList<EditorNode> TopologicalSort(EditorGraph graph)
    {
        HashSet<string> ids = new(graph.Nodes.Select(n => n.Id), StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> dependsOn = new(StringComparer.Ordinal);

        foreach (EditorNode node in graph.Nodes)
        {
            dependsOn[node.Id] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (EditorConnection connection in graph.Connections)
        {
            // 컨텍스트 입력(SourceNodeId == null)은 의존이 아니다 — 시작부터 칠판에 올라가 있다.
            // 자기 자신으로 가는 선도 의존으로 세지 않는다(그건 검증기가 사이클로 잡는다).
            if (connection.SourceNodeId is { } source
                && ids.Contains(source)
                && dependsOn.ContainsKey(connection.TargetNodeId)
                && source != connection.TargetNodeId)
            {
                dependsOn[connection.TargetNodeId].Add(source);
            }
        }

        List<EditorNode> ordered = new(graph.Nodes.Count);
        HashSet<string> emitted = new(StringComparer.Ordinal);

        // ★ 안정 정렬 — 풀린 노드를 <b>원래 순서대로</b> 내보낸다.
        //   그냥 아무거나 먼저 내보내면 같은 그래프인데도 낼 때마다 순서가 달라진다.
        //   그러면 아무것도 안 고쳤는데 git diff 가 매번 뒤집힌다.
        bool progress = true;

        while (progress)
        {
            progress = false;

            foreach (EditorNode node in graph.Nodes)
            {
                if (emitted.Contains(node.Id) || !dependsOn[node.Id].All(emitted.Contains))
                {
                    continue;
                }

                ordered.Add(node);
                emitted.Add(node.Id);
                progress = true;
            }
        }

        // ★ 고리(사이클)에 걸려 남은 노드는 원래 순서로 덧붙인다 — <b>버리지 않는다.</b>
        //   여기서 예외를 던져 버리면 "무엇이 문제인지" 를 검증기가 말해 줄 기회가 사라진다.
        //   잘못된 그래프도 일단 통째로 넘기고, 진단은 진단하는 쪽에 맡긴다.
        foreach (EditorNode node in graph.Nodes)
        {
            if (!emitted.Contains(node.Id))
            {
                ordered.Add(node);
            }
        }

        return ordered;
    }
}