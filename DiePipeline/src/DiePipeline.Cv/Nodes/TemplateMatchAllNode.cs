using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>
/// 표식 찾기 — 그림에서 표식 조각과 닮은 자리를 <b>여러 개</b> 찾는다(교수님 Phase 8 · <c>TemplateMatchAll</c>).
///
/// ★ 웨이퍼 레시피의 첫 블록이다. 받는 그림(<c>alignSrc</c>)은 실행기가 표식마다 잘라 준 <b>창</b>이고,
///   찾은 자리도 그 창 기준이다. 사진 좌표로 옮겨 모으는 것은 실행기의 일이다(교수님 Phase5 #5.10).
/// ★ 계산은 <see cref="TemplateMatchers.FindAll"/> 그대로다 — 이 블록은 입력·출력 칸을 붙인 껍데기다.
/// </summary>
public sealed class TemplateMatchAllNode : IPipelineNode
{
    private readonly string _srcKey;
    private readonly string _templateKey;
    private readonly string _outKey;
    private readonly MatchOptions _options;

    public TemplateMatchAllNode(string id, string srcKey, string templateKey, string outKey, MatchOptions options)
    {
        Id = id;
        _srcKey = srcKey;
        _templateKey = templateKey;
        _outKey = outKey;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "TemplateMatchAll";

    public void Execute(IPipelineContext context)
    {
        Mat src = context.Get<Mat>(_srcKey);
        Mat template = context.Get<Mat>(_templateKey);

        MatchList matches = TemplateMatchers.FindAll(src, template, _options);

        context.Set(_outKey, matches);
    }
}