using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 시험 그림이 <b>시험할 자격이 있는지</b>. 그림이 틀리면 그걸로 잰 모든 결과가 틀린다.
/// </summary>
public sealed class SyntheticWaferTests
{
    private static SyntheticWafer Flat() => SyntheticWafer.Draw(new SyntheticWaferSpec());

    /// <summary>die (col,row) 를 그림에서 잘라 낸다. 반듯할 때만 맞는 계산이다.</summary>
    private static Mat Die(SyntheticWafer wafer, int col, int row)
        => new(wafer.Image, new Rect(
            (int)wafer.OriginX + (col * wafer.Spec.Pitch),
            (int)wafer.OriginY + (row * wafer.Spec.Pitch),
            wafer.Spec.Pitch,
            wafer.Spec.Pitch));

    [Fact]
    public void 같은_씨면_같은_그림이_나온다()
    {
        using SyntheticWafer a = Flat();
        using SyntheticWafer b = Flat();

        Assert.Equal(0.0, Cv2.Norm(a.Image, b.Image, NormTypes.L1));
        Assert.Equal(a.Defects, b.Defects);
    }

    [Fact]
    public void 결함은_요청한_만큼_die_하나에_하나씩()
    {
        using SyntheticWafer wafer = Flat();

        Assert.Equal(3, wafer.Defects.Count);
        Assert.Equal(3, wafer.Defects.Select(d => (d.Col, d.Row)).Distinct().Count());
    }

    /// <summary>★ 무늬 위에 심으면 골든에도 무늬가 있어 차이가 안 난다 — 안 잡힌다.</summary>
    [Fact]
    public void 결함은_die_무늬와_안_겹친다()
    {
        using SyntheticWafer wafer = Flat();
        int pitch = wafer.Spec.Pitch;

        Assert.All(wafer.Defects, d =>
        {
            Assert.True(d.X >= (pitch / 10) + 3, $"{d} 가 테두리에 걸쳤다");
            Assert.True(d.X + d.Size <= pitch * 68 / 100, $"{d} 가 오른쪽 무늬에 걸쳤다");
            Assert.True(d.Y + d.Size <= pitch * 88 / 100, $"{d} 가 아래 무늬에 걸쳤다");
        });
    }

    /// <summary>반듯하면 die(0,0) 은 여백 + 캔버스 여유 자리에 있다 — 정렬이 되찾아야 할 정답.</summary>
    [Fact]
    public void 반듯하면_die_0_0_은_여백_자리에_있다()
    {
        using SyntheticWafer wafer = Flat();

        Assert.Equal(wafer.Spec.Margin + wafer.Slack, wafer.OriginX, tolerance: 1e-9);
        Assert.Equal(wafer.Spec.Margin + wafer.Slack, wafer.OriginY, tolerance: 1e-9);
    }

    /// <summary>★★ die-to-die 의 전제 — 결함 없는 die 끼리는 픽셀이 <b>완전히</b> 같아야 한다.</summary>
    [Fact]
    public void 결함_없는_die_끼리는_픽셀이_똑같다()
    {
        using SyntheticWafer wafer = Flat();

        HashSet<(int, int)> dirty = [.. wafer.Defects.Select(d => (d.Col, d.Row))];

        (int Col, int Row)[] clean =
        [
            .. Enumerable.Range(0, wafer.Spec.Rows)
                .SelectMany(r => Enumerable.Range(0, wafer.Spec.Cols).Select(c => (c, r)))
                .Where(p => !dirty.Contains(p))
                .Take(2),
        ];

        using Mat a = Die(wafer, clean[0].Col, clean[0].Row);
        using Mat b = Die(wafer, clean[1].Col, clean[1].Row);

        Assert.Equal(0.0, Cv2.Norm(a, b, NormTypes.L1));
    }

    [Fact]
    public void 심은_자리에_결함_밝기가_있다()
    {
        using SyntheticWafer wafer = Flat();

        foreach (PlantedDefect d in wafer.Defects)
        {
            int x = (int)wafer.OriginX + (d.Col * wafer.Spec.Pitch) + d.X + (d.Size / 2);
            int y = (int)wafer.OriginY + (d.Row * wafer.Spec.Pitch) + d.Y + (d.Size / 2);

            Assert.Equal(SyntheticWafer.DefectGray, wafer.Image.At<byte>(y, x));
        }
    }

    /// <summary>
    /// ★ 경계는 45 다(결함 24px 를 테두리와 무늬 사이에 넣을 자리가 없어진다).
    ///   메시지의 "100 이상" 은 넉넉히 권하는 값이지 경계가 아니다 — 50 은 들어간다.
    /// </summary>
    [Fact]
    public void 피치가_좁으면_막는다()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => SyntheticWafer.Draw(new SyntheticWaferSpec { Pitch = 40 }));

        Assert.Contains("좁습니다", error.Message);
    }

    [Fact]
    public void 각도가_5도를_넘으면_막는다()
        => Assert.Throws<ArgumentException>(() => SyntheticWafer.Draw(new SyntheticWaferSpec { AngleDeg = 6 }));

    /// <summary>표식 틀은 기울이기 전 모양 — 밝은 네모 + 왼쪽 위 어두운 네모.</summary>
    [Fact]
    public void 표식_틀은_기울이기_전_모양이다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { AngleDeg = 0.7 });
        using Mat template = wafer.DrawMarkTemplate();

        int size = wafer.Marks[0].Size;

        Assert.Equal(size, template.Rows);
        Assert.Equal(size, template.Cols);
        Assert.Equal(0, template.At<byte>((size / 6) + 1, (size / 6) + 1));     // 왼쪽 위 어두운 칸
        Assert.Equal(255, template.At<byte>(size - 2, size - 2));               // 나머지는 밝다
    }

    // ─────────────────────────────── 원판

    private static SyntheticWafer Round(int defects = 5)
        => SyntheticWafer.Draw(new SyntheticWaferSpec { Cols = 6, Rows = 6, Round = true, Defects = defects });

    [Fact]
    public void 원판으로_그리면_원판_밖이_어둡다()
    {
        using SyntheticWafer wafer = Round();
        SyntheticCircle circle = wafer.Circle!.Value;

        Assert.Equal(SyntheticWafer.OffWafer, wafer.Image.At<byte>(0, 0));     // 귀퉁이 — 원판 밖
        Assert.NotEqual(
            SyntheticWafer.OffWafer,
            wafer.Image.At<byte>((int)circle.CenterY, (int)circle.CenterX));       // 가운데 — 원판 안 무늬
    }

    /// <summary>★ 결함은 원판 안에 온전한 die 에만 — 걸친 die 나 원판 밖에 심으면 정답표가 검사 범위와 어긋난다.</summary>
    [Fact]
    public void 원판이면_결함은_원판_안에_온전한_die_에만_심는다()
    {
        using SyntheticWafer wafer = Round();
        SyntheticCircle c = wafer.Circle!.Value;
        int p = wafer.Spec.Pitch;

        Assert.Equal(5, wafer.Defects.Count);

        foreach (PlantedDefect spot in wafer.Defects)
        {
            double x0 = wafer.OriginX + (spot.Col * p);
            double y0 = wafer.OriginY + (spot.Row * p);

            foreach ((double x, double y) in new[] { (x0, y0), (x0 + p, y0), (x0, y0 + p), (x0 + p, y0 + p) })
            {
                double distance = Math.Sqrt(((x - c.CenterX) * (x - c.CenterX)) + ((y - c.CenterY) * (y - c.CenterY)));
                Assert.True(distance <= c.Radius, $"die(c{spot.Col},r{spot.Row}) 의 귀퉁이가 원판 밖이다");
            }
        }
    }

    /// <summary>원판을 안 켜면 원 정답이 없다. 그림이 전과 같은지는 지문(sha256)으로 따로 확인했다.</summary>
    [Fact]
    public void 원판을_안_켜면_원_정보가_없다()
    {
        using SyntheticWafer wafer = Flat();

        Assert.Null(wafer.Circle);
    }

    [Fact]
    public void 원판_안_온전한_die_보다_결함이_많으면_막는다()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Round(defects: 36));

        Assert.Contains("온전히", error.Message);
    }
}