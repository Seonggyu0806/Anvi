using DiePipeline.Core.Registry;

namespace DiePipeline.Core.Configuration;

/// <summary>레시피가 실행 가능한 상태가 아닐 때. 오류를 <b>전부</b> 담는다.</summary>
public sealed class RecipeValidationException : Exception
{
    public RecipeValidationException(string recipeId, IReadOnlyList<string> errors)
        : base($"레시피 '{recipeId}'에 오류 {errors.Count}개:{Environment.NewLine}  - " +
               string.Join($"{Environment.NewLine}  - ", errors))
        => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// 레시피를 <b>실행하기 전에</b> 검사한다.
///
/// ★ 왜 실행 전인가: 검사 장비에서 파이프라인이 절반쯤 돌다 터지면
///   웨이퍼는 이미 스테이지 위에 있고 로봇은 대기 중이다.
///   레시피 오류는 <b>웨이퍼를 집기 전에</b> 전부 알아야 한다.
///
/// ★ 첫 오류에서 멈추지 않고 <b>전부 모아서</b> 돌려준다.
///   하나 고치고 다시 돌리기를 열 번 반복하게 만들지 않는다.
/// </summary>
public static class RecipeValidator
{
    public static IReadOnlyList<string> Validate(Recipe recipe, NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(registry);

        List<string> errors = [];

        // 지금까지 '누군가 만든' 이름표 → 그 종류. 호출자가 넣어줄 것에서 출발한다.
        Dictionary<string, PortKind> available = new(StringComparer.Ordinal);

        foreach (string input in recipe.ContextInputs)
        {
            available[input] = ReservedContext.Kinds.TryGetValue(input, out PortKind kind) ? kind : PortKind.Image;
        }

        HashSet<string> seenIds = new(StringComparer.Ordinal);

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            if (!seenIds.Add(node.Id))
            {
                errors.Add($"node '{node.Id}': id가 중복됩니다");
            }

            if (!registry.Contains(node.Type))
            {
                errors.Add($"node '{node.Id}': 알 수 없는 type '{node.Type}' " +
                           $"(등록된 것: {string.Join(", ", registry.TypeNames.Order())})");
                continue;   // 디스크립터가 없으니 더 검사할 수 없다
            }

            INodeDescriptor descriptor = registry.Describe(node.Type);

            CheckInputs(errors, node, descriptor, available);
            CheckOutputs(errors, node, descriptor, available);

            foreach (ParamDescriptor param in descriptor.Params)
            {
                foreach (string error in ParamCheck.Validate(param, node.Params))
                {
                    errors.Add($"node '{node.Id}'({node.Type}): {error}");
                }
            }

            // ★ 서술자로 못 적는 파라미터(Filter의 filters[] 같은 중첩 목록)는
            //   팩토리가 스스로 검증한다. 안 하는 팩토리는 그냥 지나간다.
            if (registry.Get(node.Type) is ICustomParamsValidator custom)
            {
                foreach (string error in custom.ValidateParams(node))
                {
                    errors.Add($"node '{node.Id}'({node.Type}): {error}");
                }
            }
        }

        return errors;
    }

    /// <summary>오류가 있으면 던진다. 실행기가 부르는 쪽.</summary>
    public static void EnsureValid(Recipe recipe, NodeRegistry registry)
    {
        IReadOnlyList<string> errors = Validate(recipe, registry);

        if (errors.Count > 0)
        {
            throw new RecipeValidationException(recipe.RecipeId, errors);
        }
    }

    private static void CheckInputs(
        List<string> errors, NodeConfig node, INodeDescriptor descriptor, Dictionary<string, PortKind> available)
    {
        foreach (PortDescriptor port in descriptor.Inputs)
        {
            if (!node.Inputs.TryGetValue(port.Name, out string? key))
            {
                if (!port.Optional)
                {
                    errors.Add($"node '{node.Id}'({node.Type}): 입력 포트 '{port.Name}'이 연결되지 않았습니다");
                }

                continue;
            }

            // ★★ 이 검사가 이 파일의 존재 이유다.
            //   앞에서 만들어진 것만 available에 있으므로, 오타도 순서가 뒤집힌 배선도 여기 걸린다.
            if (!available.TryGetValue(key, out PortKind produced))
            {
                errors.Add($"node '{node.Id}'({node.Type}): 입력 '{port.Name}'이 가리키는 '{key}'를 " +
                           $"앞의 어떤 노드도 만들지 않습니다. 지금까지: [{string.Join(", ", available.Keys)}]");
                continue;
            }

            if (produced != port.Kind)
            {
                errors.Add($"node '{node.Id}'({node.Type}): 입력 '{port.Name}'은 {port.Kind}를 받는데 " +
                           $"'{key}'는 {produced}입니다");
            }
        }
    }

    private static void CheckOutputs(
        List<string> errors, NodeConfig node, INodeDescriptor descriptor, Dictionary<string, PortKind> available)
    {
        foreach (PortDescriptor port in descriptor.Outputs)
        {
            if (!node.Outputs.TryGetValue(port.Name, out string? key))
            {
                errors.Add($"node '{node.Id}'({node.Type}): 출력 포트 '{port.Name}'이 연결되지 않았습니다");
                continue;
            }

            // PipelineContext.Set이 던지는 예외와 같은 규칙을 실행 전에 미리 검사하는 것이다.
            if (available.ContainsKey(key))
            {
                errors.Add($"node '{node.Id}'({node.Type}): 출력 '{key}'를 이미 다른 노드가 쓰고 있습니다");
                continue;
            }

            available[key] = port.Kind;
        }
    }
}