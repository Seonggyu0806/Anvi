using DiePipeline.Core.Domain;
using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>얼마나 닮았는지 재는 방법.</summary>
public enum MatchMethod
{
    /// <summary>정해지지 않음. 받으면 거부한다.</summary>
    Unknown = 0,

    /// <summary>평균을 뺀 정규화 상관. <b>밝기 차이에 제일 둔하다</b> — 기본값.</summary>
    CCoeffNormed = 1,

    /// <summary>정규화 상관. 평균을 안 빼서 밝기 차이에 조금 더 민감하다.</summary>
    CCorrNormed = 2,

    /// <summary>차이 제곱합. <b>원래는 낮을수록 좋지만</b> 여기서 뒤집어 준다.</summary>
    SqDiffNormed = 3,
}

/// <summary>찾기 설정.</summary>
public sealed record MatchOptions
{
    public MatchMethod Method { get; init; } = MatchMethod.CCoeffNormed;

    /// <summary>
    /// 이보다 안 닮았으면 <b>못 찾은 것으로 친다.</b>
    ///
    /// ★ 2주차에 배운 것 — 재는 도구는 <b>엉뚱한 답을 자신 있게</b> 낸다.
    ///   "제일 닮은 자리" 는 아무 사진에나 반드시 하나 있다. 그게 표식이라는 뜻은 아니다.
    ///
    /// ★ 실측으로 정했다 — <b>진짜 표식 0.93~1.00 / 반복 무늬 오답 0.70 언저리.</b>
    ///   사이가 넓어서 0.8 이면 깨끗이 갈린다. 0.6 으로 두면 무늬 오답이 줄줄이 딸려 온다.
    ///
    /// ★★ 다만 <b>이것만으로는 못 막는 경우가 있다.</b> 조각이 작으면(수십 px) 닮은 자리가
    ///   여러 곳 생기고, 그중 <b>틀린 자리가 1등</b>으로 나오기도 한다(실측: 오답 0.98 / 정답 0.93).
    ///   그건 점수로 못 막는다 — <b>조각을 크게(수백 px) 하고 · 찾는 창을 좁히고 ·
    ///   표식 둘의 기하가 맞는지 확인해서</b> 막는다. 마지막 것이 SolveAlignment 의 일이다.
    /// </summary>
    public double MinScore { get; init; } = 0.8;

    /// <summary>몇 개까지 찾을까.</summary>
    public int MaxMatches { get; init; } = 8;

    /// <summary>
    /// 한 자리를 고른 뒤 눌러 놓을 반경(px). <b>0이면 템플릿 짧은 변의 절반</b>으로 알아서 잡는다.
    ///
    /// ★ 좁으면 봉우리 하나가 여러 개로 갈라지고, 넓으면 <b>가까이 있는 진짜 표식 둘이 하나로 뭉친다.</b>
    /// </summary>
    public int SuppressRadius { get; init; }

    /// <summary>
    /// 소수점 자리까지 잡을까. <b>기본은 켠다.</b>
    ///
    /// ★ <c>matchTemplate</c> 은 <b>한 칸 단위</b>로만 답한다. 자리를 쓰는 데는 충분하지만,
    ///   <b>두 점으로 각도를 풀 때는 그 반 칸이 그대로 각도 오차가 된다.</b>
    ///   실측 — 표식 간격 570px 에서 각도가 0.700° 대신 0.785° 로 나왔다(0.085° 오차).
    ///   같은 오차가 1만 px 급 웨이퍼에서는 끝에서 15px 밀림이 된다.
    ///
    /// ★ 끄는 경우는 "칸 단위가 맞는지" 를 시험할 때뿐이다.
    /// </summary>
    public bool SubPixel { get; init; } = true;
}

/// <summary>
/// 작은 조각(템플릿)이 큰 사진 <b>어디에 있는지</b> 찾는다.
///
/// <para>★ 방법 — 조각을 사진 위에서 한 칸씩 밀어 가며 "얼마나 닮았나" 점수판을 만들고,
/// 높은 봉우리부터 차례로 집는다. 투명 필름에 그린 그림을 사진 위에 올려놓고 밀어 보는 것과 같다.</para>
///
/// <para>★ <b>정규화</b> 상관을 쓴다. 그냥 빼서 비교하면 사진이 전체적으로 밝을 때
/// 아무 데나 점수가 높아진다. 정규화하면 밝기가 아니라 <b>모양</b>만 본다.</para>
///
/// <para>★ 회전에는 <b>약하다.</b> 조각을 돌려 보지는 않기 때문이다.
/// 웨이퍼는 몇 도 안 돌아가니 작은 표식에서는 문제가 안 되지만,
/// 크게 돌아간 웨이퍼는 이 방법으로 못 찾는다 — 그래서 <c>demo</c> 도 ±5도까지만 받는다.</para>
/// </summary>
public static class TemplateMatchers
{
    /// <summary>한 번 고른 자리를 눌러 두는 값. 어떤 점수보다도 낮아서 다시 안 뽑힌다.</summary>
    private const float Suppressed = -1.0e9f;

    public static MatchList FindAll(Mat source, Mat template, MatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Method == MatchMethod.Unknown)
        {
            throw new ArgumentException("재는 방법을 안 정했습니다", nameof(options));
        }

        if (options.MaxMatches < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), $"찾을 개수는 1 이상이어야 합니다 (현재 {options.MaxMatches})");
        }

        if (options.SuppressRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "누를 반경은 0 이상이어야 합니다");
        }

        if (source.Type() != MatType.CV_32FC1 || template.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException(
                "사진과 조각 둘 다 32비트 실수 회색(CV_32FC1)이어야 합니다 — "
                + "ImageSource.ReadRegion 이나 ImageFile.LoadGrayF32 로 읽으세요");
        }

        // ★ 조각이 사진보다 크면 애초에 못 찾는다. 빈 목록을 주면 "표식이 없네" 로 읽히지만
        //   사실은 <b>파일을 잘못 준 것</b>이다. 설정 오류는 조용히 넘기지 않는다.
        if (template.Cols > source.Cols || template.Rows > source.Rows)
        {
            throw new ArgumentException(
                $"조각({template.Cols}×{template.Rows})이 사진({source.Cols}×{source.Rows})보다 큽니다 "
                + "— 조각과 사진을 바꿔 넣지 않았는지 확인하세요",
                nameof(template));
        }

        using Mat scores = ScoreMap(source, template, options.Method);

        int radius = options.SuppressRadius > 0
            ? options.SuppressRadius
            : Math.Max(1, Math.Min(template.Cols, template.Rows) / 2);

        List<Match> found = [];

        for (int i = 0; i < options.MaxMatches; i++)
        {
            Cv2.MinMaxLoc(scores, out _, out double best, out _, out Point at);

            // 점수판이 내림차순으로 나오므로, 여기서 끊기면 뒤는 볼 것도 없다.
            if (best < options.MinScore)
            {
                break;
            }

            PointF where = options.SubPixel
                ? Refine(scores, at)
                : new PointF(at.X, at.Y);

            found.Add(new Match { Location = where, Score = best });

            // ★ 누르는 것은 <b>정수 칸</b> 기준이다. 소수점은 보고용이고,
            //   점수판을 지우는 일은 칸 단위로만 할 수 있다.
            Suppress(scores, at, radius);
        }

        return found.Count == 0 ? MatchList.Empty : new MatchList { Items = found };
    }

    /// <summary>"클수록 좋다" 로 방향을 맞춘 점수판.</summary>
    private static Mat ScoreMap(Mat source, Mat template, MatchMethod method)
    {
        TemplateMatchModes mode = method switch
        {
            MatchMethod.CCorrNormed => TemplateMatchModes.CCorrNormed,
            MatchMethod.SqDiffNormed => TemplateMatchModes.SqDiffNormed,
            _ => TemplateMatchModes.CCoeffNormed,
        };

        Mat raw = new();

        try
        {
            Cv2.MatchTemplate(source, template, raw, mode);

            if (method != MatchMethod.SqDiffNormed)
            {
                return raw;
            }

            // ★ 이 방식만 "낮을수록 좋음" 이다. 1에서 빼서 나머지와 방향을 맞춘다.
            //   여기서 안 맞추면 MinScore 가드가 정반대로 걸린다.
            using Mat ones = new(raw.Rows, raw.Cols, raw.Type(), Scalar.All(1.0));

            Mat flipped = new();

            try
            {
                Cv2.Subtract(ones, raw, flipped);
                return flipped;
            }
            catch
            {
                flipped.Dispose();
                throw;
            }
        }
        finally
        {
            if (method == MatchMethod.SqDiffNormed)
            {
                raw.Dispose();
            }
        }
    }

    /// <summary>
    /// 봉우리 <b>꼭대기의 소수점 자리</b>를 찾는다.
    ///
    /// <para>★ 최고점 칸과 <b>양옆 칸</b> 세 개를 지나는 포물선을 그려 그 꼭대기를 구한다.
    /// 세 점이면 포물선이 하나로 정해지므로 식이 딱 떨어진다.</para>
    ///
    /// <code>
    ///   δ = ½ · (왼쪽 − 오른쪽) / (왼쪽 − 2·가운데 + 오른쪽)
    /// </code>
    ///
    /// <para><b>비유</b> — 언덕에 1m 간격으로 말뚝을 박고 높이를 쟀다. 제일 높은 말뚝이 꼭대기는 아니다.
    /// <b>양옆 말뚝을 비교하면</b> 진짜 꼭대기가 어느 쪽으로 얼마나 치우쳤는지 나온다.</para>
    ///
    /// <para>★ 못 믿을 때는 <b>그냥 정수 칸을 쓴다</b> — 가장자리라 양옆이 없거나,
    /// 분모가 0에 가깝거나(평평해서 꼭대기가 어딘지 알 수 없다),
    /// 반 칸을 넘는 답이 나오면(봉우리 모양이 아니다) 보정하지 않는다.
    /// <b>못 믿을 때 안 움직이는 것</b>은 2주차 정합 가드와 같은 태도다.</para>
    /// </summary>
    private static PointF Refine(Mat scores, Point at)
        => new(at.X + Offset(Sample(scores, at.X - 1, at.Y), Sample(scores, at.X, at.Y), Sample(scores, at.X + 1, at.Y)),
               at.Y + Offset(Sample(scores, at.X, at.Y - 1), Sample(scores, at.X, at.Y), Sample(scores, at.X, at.Y + 1)));

    /// <summary>점수판 한 칸. 판 밖이면 <c>null</c> — 보정하지 않는다는 뜻이다.</summary>
    private static double? Sample(Mat scores, int x, int y)
        => x < 0 || y < 0 || x >= scores.Cols || y >= scores.Rows
            ? null
            : scores.At<float>(y, x);

    private static double Offset(double? low, double? middle, double? high)
    {
        if (low is not { } a || middle is not { } b || high is not { } c)
        {
            return 0;   // 가장자리 — 양옆이 없다
        }

        double curve = a - (2 * b) + c;

        // 0에 가까우면 평평하다는 뜻이고, 양수면 봉우리가 아니라 골짜기다.
        if (curve > -1e-12)
        {
            return 0;
        }

        double shift = 0.5 * (a - c) / curve;

        // 반 칸을 넘으면 옆 칸이 더 높다는 말이라 애초에 최고점이 아니었다.
        return Math.Abs(shift) <= 0.5 ? shift : 0;
    }

    /// <summary>고른 자리 둘레를 눌러 둔다 — 같은 봉우리를 또 집지 않게.</summary>
    private static void Suppress(Mat scores, Point center, int radius)
    {
        int left = Math.Max(center.X - radius, 0);
        int top = Math.Max(center.Y - radius, 0);
        int right = Math.Min(center.X + radius, scores.Cols - 1);
        int bottom = Math.Min(center.Y + radius, scores.Rows - 1);

        using Mat window = scores[new Rect(left, top, right - left + 1, bottom - top + 1)];

        window.SetTo(Scalar.All(Suppressed));
    }
}