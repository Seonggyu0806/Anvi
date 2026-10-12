using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Tests;

/// <summary>
/// 사람이 적은 글자 → 웨이퍼 입력값. ★ 명령행과 화면이 이 규칙 하나를 같이 쓴다.
/// </summary>
public sealed class WaferInputTextTests
{
    [Fact]
    public void 점_하나를_읽는다()
    {
        Assert.Equal(new PointF(410, -40.5), WaferInputText.Point(" 410 , -40.5 "));
    }

    /// <summary>여럿은 ; 나 줄바꿈으로 나눈다. 빈 칸은 건너뛴다.</summary>
    [Fact]
    public void 점_여럿을_세미콜론과_줄바꿈으로_나눈다()
    {
        IReadOnlyList<PointF> points = WaferInputText.Points("-40,-40; 410,310;\r\n12.5,7 ;");

        Assert.Equal([new PointF(-40, -40), new PointF(410, 310), new PointF(12.5, 7)], points);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ;  ")]
    public void 빈_글자는_빈_목록이다(string? text)
    {
        Assert.Empty(WaferInputText.Points(text));
        Assert.Empty(WaferInputText.Zones(text));
    }

    [Theory]
    [InlineData("410")]
    [InlineData("1,2,3")]
    [InlineData("a,2")]
    public void 틀린_점은_무엇이_틀렸는지_말한다(string text)
    {
        FormatException error = Assert.Throws<FormatException>(() => WaferInputText.Point(text));

        Assert.Contains(text, error.Message);
    }

    [Theory]
    [InlineData("2,3=off", 2, 3, WaferZone.Outside)]
    [InlineData("0,3=E0", 0, 3, WaferZone.E0)]
    [InlineData(" 2 , 2 = e1 ", 2, 2, WaferZone.E1)]
    public void 수동_지정_하나를_읽는다(string text, int col, int row, WaferZone zone)
    {
        Assert.Equal(new ZoneOverride(col, row, zone), WaferInputText.Zone(text));
    }

    [Fact]
    public void 수동_지정_여럿을_읽는다()
    {
        Assert.Equal(
            [new ZoneOverride(2, 3, WaferZone.Outside), new ZoneOverride(3, 3, WaferZone.E0)],
            WaferInputText.Zones("2,3=off; 3,3=E0"));
    }

    [Theory]
    [InlineData("2,3")]           // 존이 없다
    [InlineData("2=off")]         // 행이 없다
    [InlineData("2,3=skip")]      // 모르는 존
    [InlineData("2.5,3=off")]     // 열·행은 정수
    public void 틀린_수동_지정은_거부한다(string text)
    {
        Assert.Throws<FormatException>(() => WaferInputText.Zone(text));
    }

    /// <summary>결과에 다시 보일 때는 사람이 적은 모양 그대로 — off 는 off 로.</summary>
    [Fact]
    public void 수동_지정을_다시_글자로_보인다()
    {
        Assert.Equal("c7r15→off", WaferInputText.Describe(new ZoneOverride(7, 15, WaferZone.Outside)));
        Assert.Equal("c8r15→E0", WaferInputText.Describe(new ZoneOverride(8, 15, WaferZone.E0)));
    }
}