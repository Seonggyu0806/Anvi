using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 검사 영역 한정 — <c>MaskApply</c>.
///
/// ★ 오늘 확인할 것 — <b>지운 곳이 결함으로 둔갑하지 않는가.</b>
///   E0 레시피는 0~0.15 를 결함으로 본다. 0 으로 지우면 지운 곳이 통째로 걸린다.
/// </summary>
public sealed class MaskApplyTests : IDisposable
{
    private readonly List<Mat> _made = [];

    public void Dispose()
    {
        foreach (Mat mat in _made)
        {
            mat.Dispose();
        }
    }

    private Mat Keep(Mat mat)
    {
        _made.Add(mat);
        return mat;
    }

    /// <summary>4×4 · 밝기 0.4 — 원판 안 무늬쯤.</summary>
    private Mat Image() => Keep(new Mat(4, 4, MatType.CV_32FC1, Scalar.All(0.4)));

    /// <summary>왼쪽 두 열만 켜진 마스크 — 왼쪽이 원판 안, 오른쪽이 원판 밖이라고 치자.</summary>
    private Mat LeftHalf(byte on = 255)
    {
        Mat mask = Keep(new Mat(4, 4, MatType.CV_8UC1, Scalar.All(0)));

        // ColRange 는 복사가 아니라 그 자리를 가리키는 창이다 — 창에 칠하면 원본에 칠해진다.
        using (Mat left = mask.ColRange(0, 2))
        {
            left.SetTo(Scalar.All(on));
        }

        return mask;
    }

    // ─────────────────────────────── 지우기

    /// <summary>기본값 — 교수님 동작 그대로. 켜진 곳은 원본, 꺼진 곳은 0.</summary>
    [Fact]
    public void 켜진_곳은_그대로_꺼진_곳은_0이다()
    {
        Mat result = Keep(MaskAppliers.Apply(Image(), LeftHalf(), new MaskApplyOptions()));

        Assert.Equal(0.4f, result.At<float>(0, 0), 1e-6f);
        Assert.Equal(0.0f, result.At<float>(0, 3), 1e-6f);
    }

    [Fact]
    public void fill_로_지운_곳을_채운다()
    {
        Mat result = Keep(MaskAppliers.Apply(Image(), LeftHalf(), new MaskApplyOptions { Fill = 1.0 }));

        Assert.Equal(0.4f, result.At<float>(0, 0), 1e-6f);
        Assert.Equal(1.0f, result.At<float>(0, 3), 1e-6f);
    }

    [Fact]
    public void invert_면_꺼진_곳만_남긴다()
    {
        Mat result = Keep(MaskAppliers.Apply(Image(), LeftHalf(), new MaskApplyOptions { Invert = true }));

        Assert.Equal(0.0f, result.At<float>(0, 0), 1e-6f);
        Assert.Equal(0.4f, result.At<float>(0, 3), 1e-6f);
    }

    /// <summary>
    /// ★ 마스크 값이 255 가 아니어도 '0 이 아니면 켜짐' 이다.
    ///   교수님 방식(BitwiseNot)이면 1 은 254 가 되어 반전해도 여전히 켜져 있다.
    /// </summary>
    [Fact]
    public void 마스크가_0_255_가_아니어도_반전이_맞다()
    {
        Mat result = Keep(MaskAppliers.Apply(Image(), LeftHalf(on: 1), new MaskApplyOptions { Invert = true }));

        Assert.Equal(0.0f, result.At<float>(0, 0), 1e-6f);
        Assert.Equal(0.4f, result.At<float>(0, 3), 1e-6f);
    }

    // ─────────────────────────────── ★ 이 블록을 가져온 까닭

    /// <summary>
    /// ★★ 어두운 임계(0~0.15 = 결함) 앞에서 fill 이 결과를 가른다.
    ///   0 으로 지우면 원판 밖 8칸이 결함 · 1.0 으로 채우면 원판 안의 진짜 결함 1칸만 남는다.
    /// </summary>
    [Theory]
    [InlineData(0.0, (2 * 4) + 1)]
    [InlineData(1.0, 1)]
    public void 어두운_임계_앞에서는_fill_이_결과를_가른다(double fill, int expectedDark)
    {
        Mat image = Image();
        image.Set(1, 1, 0.05f);    // 원판 안의 진짜 어두운 결함

        Mat masked = Keep(MaskAppliers.Apply(image, LeftHalf(), new MaskApplyOptions { Fill = fill }));
        Mat dark = Keep(new RangeBinarizer(0.0, 0.15).Binarize(masked));

        Assert.Equal(expectedDark, Cv2.CountNonZero(dark));
    }

    // ─────────────────────────────── 거부

    [Fact]
    public void 형식이_틀리면_거부한다()
    {
        Mat image8 = Keep(new Mat(4, 4, MatType.CV_8UC1, Scalar.All(100)));
        Mat maskF32 = Keep(new Mat(4, 4, MatType.CV_32FC1, Scalar.All(1)));
        Mat small = Keep(new Mat(2, 2, MatType.CV_8UC1, Scalar.All(255)));

        Assert.Throws<ArgumentException>(() => MaskAppliers.Apply(image8, LeftHalf(), new MaskApplyOptions()));
        Assert.Throws<ArgumentException>(() => MaskAppliers.Apply(Image(), maskF32, new MaskApplyOptions()));
        Assert.Throws<ArgumentException>(() => MaskAppliers.Apply(Image(), small, new MaskApplyOptions()));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void fill_은_0에서_1_사이다(double fill)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => MaskAppliers.Apply(Image(), LeftHalf(), new MaskApplyOptions { Fill = fill }));

    // ─────────────────────────────── 레시피로

    /// <summary>명부에 있고, 기본값이 교수님과 같다(invert false · fill 0).</summary>
    [Fact]
    public void 기본값은_교수님과_같다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("MaskApply");

        Assert.Equal(false, descriptor.Params.Single(p => p.Name == "invert").Default);
        Assert.Equal(0.0, descriptor.Params.Single(p => p.Name == "fill").Default);
    }

    /// <summary>
    /// ★ 레시피 한 장으로 돈다 — 마스크는 바깥에서 받은 입력(<c>waferMask</c>)이다.
    ///   웨이퍼 층이 die 마다 이 이름으로 넘겨줄 자리다(로드맵 3번).
    /// </summary>
    [Fact]
    public void 레시피로_돈다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            { "recipeId": "t", "contextInputs": [ "roi", "waferMask" ], "pipeline": { "nodes": [
              { "id": "mask", "type": "MaskApply", "inputs": { "src": "roi", "mask": "waferMask" },
                "outputs": { "result": "masked" }, "params": { "fill": 1.0 } }
            ] } }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("roi", Image());
        context.SetInput("waferMask", LeftHalf());

        float inside = float.NaN;
        float outside = float.NaN;

        new PipelineEngine().Run(
            registry.CreateAll(recipe),
            context,
            inspect: c =>
            {
                Mat masked = c.Get<Mat>("masked");
                inside = masked.At<float>(0, 0);
                outside = masked.At<float>(0, 3);
            });

        Assert.Equal(0.4f, inside, 1e-6f);
        Assert.Equal(1.0f, outside, 1e-6f);
    }
}