using System.Windows.Media.Imaging;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wpf.Imaging;
using OpenCvSharp;

namespace DiePipeline.Wpf.Preview;

/// <summary>미리보기 한 장 — 칠판의 이름표 하나에 해당한다.</summary>
public sealed record PreviewItem(string Key, BitmapSource Image);

/// <summary>
/// 미리보기 한 번의 결과.
///
/// ★ <paramref name="Inspected"/>와 <paramref name="Defects"/>를 같이 돌려준다 —
///   화면이 네모를 그리려면 <b>그림과 좌표가 짝</b>이어야 한다. 따로 가져가면 언젠가 어긋난다.
///
/// ★ <paramref name="Width"/>·<paramref name="Height"/>는 <b>원본 픽셀</b> 크기다.
///   결함 좌표도 원본 픽셀이라, 화면은 이 둘을 같은 자 위에 놓기만 하면 된다 —
///   <b>좌표를 다시 계산하지 않는다.</b>
/// </summary>
public sealed record PreviewResult(
    IReadOnlyList<PreviewItem> Items,
    IReadOnlyList<Defect> Defects,
    BitmapSource Inspected,
    int Width,
    int Height);

/// <summary>
/// 원본에서 그린 미리보기 입력 — 원본 소스 + 검사 ROI + 정상 die 들(원본 픽셀). 원본 뷰어의 '다이 에디터에 적용' 이 만든다.
/// ★ 소스는 뷰어 것이다 — 미리보기는 읽기만 하고 놓지 않는다.
/// </summary>
public sealed record PreviewInput(IImageSource Source, string Title, Roi Roi, IReadOnlyList<Roi> Chips);

/// <summary>
/// 레시피를 <b>합성 이미지</b>로 한 번 돌리고, 각 단계의 중간 이미지를 거둬 온다.
/// 원본 뷰어에서 영역을 넘겨받았으면(<see cref="PreviewInput"/>) 합성 대신 <b>원본의 ROI · 정상 die</b> 로 돈다.
///
/// ★ 왜 합성인가: 실데이터는 밖으로 못 나가고, 없어도 레시피를 만져 볼 수 있어야 한다.
///   "어느 단계에서 결함이 사라지는지" 를 보는 게 목적이라 그림이 진짜일 필요가 없다.
///
/// ★★ 여기 값들은 <b>이 합성 이미지 전용</b>이다. 결함이 5×5=25 픽셀이라
///   실데이터용 <c>minArea 500</c> 을 쓰면 <b>0개</b>가 잡힌다.
/// </summary>
public static class PreviewService
{
    private static readonly Roi InspectRegion = new(150, 0, 40, 40);

    /// <summary>
    /// 미리보기용 원판 — 검사 조각(150,0,40,40)의 오른쪽 아래 모서리를 중심으로 반지름 35.
    /// 원판 가장자리가 조각을 비스듬히 지나가 <b>왼쪽 위가 원판 밖</b>이 된다(E0 die 모양).
    /// </summary>
    private static readonly WaferCircle PreviewCircle = new(190, 40, 35);

    /// <summary>원판 밖 밝기 — 합성 원판 웨이퍼와 같은 어두운 배경. E0 임계(0.15)보다 어둡다.</summary>
    private const double OffWafer = 0.05;

    private static readonly Roi[] NeighborRegions =
    [
        new(0, 0, 40, 40),
        new(50, 0, 40, 40),
        new(100, 0, 40, 40),
    ];

    public static PreviewResult Run(NodeRegistry registry, Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(recipe);

        using Mat scene = SyntheticScene();
        using MatImageSource source = new(scene, owns: false);

        using Mat inspect = source.ReadRegion(InspectRegion);

        // ★ 레시피가 원판 마스크를 받으면 — 검사 조각을 원판에 걸친 die 처럼 만든다.
        //   원판 밖을 어둡게 칠하고(배경), 웨이퍼 검사와 같은 함수로 만든 마스크를 같이 넘긴다.
        //   안 받는 레시피(die-to-die)에는 아무것도 안 바뀐다.
        using Mat? waferMask = recipe.ContextInputs.Contains(ReservedContext.WaferMask)
            ? WaferInspection.WaferMaskOf(InspectRegion, PreviewCircle)
            : null;

        if (waferMask is not null)
        {
            using Mat outside = new();
            Cv2.Compare(waferMask, Scalar.All(0), outside, CmpTypes.EQ);
            inspect.SetTo(Scalar.All(OffWafer), outside);
        }

        List<Mat> neighbors = [];

        try
        {
            foreach (Roi region in NeighborRegions)
            {
                neighbors.Add(source.ReadRegion(region));
            }

            PipelineContext context = new();

            // ★ SetInput — 소유권이 우리에게 남는다. 칠판이 이걸 놓아 버리면 안 된다.
            context.SetInput(ReservedContext.Roi, inspect);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)neighbors);

            if (waferMask is not null)
            {
                context.SetInput(ReservedContext.WaferMask, waferMask);
            }

            return RunOn(registry, recipe, context, inspect);
        }
        finally
        {
            foreach (Mat neighbor in neighbors)
            {
                neighbor.Dispose();
            }
        }
    }

    /// <summary>
    /// 원본의 ROI · 정상 die 로 돌린다 — 실데이터 die 로 레시피 값을 맞춰 보는 길. ★ 아무것도 저장하지 않는다.
    /// ★ 원판 마스크를 받는 레시피(E0)는 못 돌린다 — 원본 한 장만으로는 원판이 어디 있는지 모른다.
    /// </summary>
    public static PreviewResult Run(NodeRegistry registry, Recipe recipe, PreviewInput input)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(input);

        if (recipe.ContextInputs.Contains(ReservedContext.WaferMask))
        {
            throw new InvalidOperationException(
                "원판 마스크(waferMask)를 받는 레시피는 원본 미리보기로 못 돌립니다 — 원판 위치를 모릅니다. '합성으로 되돌리기' 로 보세요");
        }

        using Mat inspect = input.Source.ReadRegion(input.Roi);
        List<Mat> neighbors = [];

        try
        {
            foreach (Roi region in input.Chips)
            {
                neighbors.Add(input.Source.ReadRegion(region));
            }

            PipelineContext context = new();
            context.SetInput(ReservedContext.Roi, inspect);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)neighbors);

            return RunOn(registry, recipe, context, inspect);
        }
        finally
        {
            foreach (Mat neighbor in neighbors)
            {
                neighbor.Dispose();
            }
        }
    }

    /// <summary>입력을 다 올린 칠판으로 한 번 돌리고 단계 그림을 거둔다 — 합성 · 원본이 같은 길.</summary>
    private static PreviewResult RunOn(NodeRegistry registry, Recipe recipe, PipelineContext context, Mat inspect)
    {
        List<PreviewItem> items = [];

        // ★ 엔진의 틈으로 들어가 반납되기 <b>전에</b> 거둔다.
        PipelineResult result = new PipelineEngine().Run(
            registry.CreateAll(recipe),
            context,
            inspect: board => Capture(recipe, board, items));

        // ★ 검사 조각은 반납 뒤에도 필요하다(네모를 얹을 바탕). 그래서 따로 한 장 더 뜬다.
        //   BitmapSource 는 Freeze 돼 있어 Mat 이 죽어도 멀쩡하다.
        return new PreviewResult(
            items,
            result.Defects.Items,
            BitmapConvert.ToBitmap(inspect),
            inspect.Width,
            inspect.Height);
    }

    /// <summary>
    /// ★ 이름표 목록을 <b>레시피에서</b> 얻는다 — 칠판에는 "무엇이 올라와 있나" 를 묻는 길이 없다.
    ///   덤으로, 꺼 둔 노드의 이름표는 칠판에 없으니 자연스럽게 빠진다.
    ///
    /// ★ 교수님은 이름순으로 정렬했는데, 우리는 <b>실행 순서</b> 그대로 둔다.
    ///   "어느 단계에서 사라졌나" 를 보려면 단계 차례대로 늘어서는 게 맞다.
    /// </summary>
    private static void Capture(Recipe recipe, IPipelineContext board, List<PreviewItem> items)
    {
        foreach (string key in Names(recipe))
        {
            // 이미지가 아닌 것(chipRegions 목록·결함 목록)은 TryGet 이 알아서 거른다.
            if (board.TryGet(key, out Mat? mat) && mat is not null)
            {
                items.Add(new PreviewItem(key, BitmapConvert.ToBitmap(mat)));
            }
        }
    }

    private static IEnumerable<string> Names(Recipe recipe)
    {
        foreach (string input in recipe.ContextInputs)
        {
            yield return input;
        }

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            foreach (string key in node.Outputs.Values)
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// 밋밋한 바탕에 밝은 네모 하나. ★ GrayF32 여야 한다 — 작업 포맷이 그것이다.
    /// 바탕 0.4, 결함 0.86 → 차이 0.46 이라 임계값 0.2 를 넉넉히 넘는다.
    /// </summary>
    private static Mat SyntheticScene()
    {
        Mat scene = new(100, 200, MatType.CV_32FC1, Scalar.All(0.4));

        // 검사 영역(150,0,40,40) 안쪽에 찍는다. 밖에 찍으면 아무것도 안 잡힌다.
        Cv2.Rectangle(scene, new Rect(168, 18, 5, 5), Scalar.All(0.86), thickness: -1);

        return scene;
    }
}