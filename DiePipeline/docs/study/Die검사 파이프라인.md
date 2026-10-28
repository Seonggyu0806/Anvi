# VisionStack — die 검출 파이프라인 (C#)

`브리핑/9월 2째주 브리핑.md`에서 공부한 **die 검출 표준 선형 흐름**을 그대로 C#으로 구현한 것.
레시피(JSON) + 노드 레지스트리 구조를 따르고, 실제 검사 이미지
`D:\Local_ATI\ATI_Image\ROI_Mono_585MB.bmp`(10000×20000, 24bpp)로 돌려볼 수 있다.

외부 의존성 없음. OpenCV도 쓰지 않고 FFT·모폴로지·연결요소·CLAHE·ECC까지 전부 직접 구현했다.
알고리즘이 전부 열려 있으니 스텝별로 뜯어보면서 공부하기 좋다.

```
contextInputs: roi, chipRegions
   │
[Combine]  ── chipRegions ─────────► golden          정상 die들 → Golden
[Normalize]── golden ──────────────► goldenNorm      Golden 정규화 (golden만!)
[Align]    ── roi + goldenNorm ────► roiAligned      위상상관 서브픽셀 정합
[Difference]─ roiAligned+goldenNorm► diff            차영상 — 검출의 심장부
[Filter]   ── diff ────────────────► filtered        필터 체인
[Binarize] ── filtered ────────────► mask            0/255 마스크
[Label]    ── mask (+roiAligned) ──► defectList      연결요소 → DefectList
   │
[DefectCluster → DefectClassify → DefectLimit]       (선택, 꼬리)
```

---

## 빠르게 돌려보기

가장 쉬운 방법은 저장소 루트의 **`run-demo.bat`을 더블클릭**하는 것이다.
빌드가 안 돼 있으면 알아서 빌드하고, 메뉴에서 번호만 고르면 된다.

```
  1) 전체 검사 실행       32개 die, 약 9초
  2) die 하나만 + 중간단계 저장   c1,r1 - golden/diff/mask PNG
  3) 노드 카탈로그 보기    포트 - 파라미터 - 기본값
  4) 레시피 검증만
  5) 이미지 정보 + 미리보기 PNG
  6) 테스트 실행          xunit 210개
  7) 결과 폴더 열기
```

> **`visionstack.exe`를 직접 더블클릭하면 창이 바로 닫힌다.** 고장이 아니라, 명령을 인자로
> 받는 콘솔 도구라 인자 없이 실행하면 사용법만 찍고 끝나기 때문이다. 사용법을 읽을 수 있도록
> 이 경우에는 키 입력을 기다리게 해뒀다(콘솔에 이 프로세스만 붙어 있을 때만. 터미널이나
> 스크립트에서 부를 때는 그냥 끝난다).

직접 빌드하려면:

```bash
cd D:\Local_ATI\VisionStack
dotnet build -c Release
```

전체 32 die 검사 (약 9초):

```bash
src\VisionStack.Cli\bin\Release\net8.0\visionstack.exe run --recipe samples\recipes\ati.die.v1.json --case samples\cases\ati_roi.case.json
```

테스트:

```bash
dotnet test
```

노드 카탈로그(포트·파라미터·기본값)를 디스크립터에서 뽑아 출력:

```bash
src\VisionStack.Cli\bin\Release\net8.0\visionstack.exe list
```

레시피 검증만:

```bash
src\VisionStack.Cli\bin\Release\net8.0\visionstack.exe validate --recipe samples\recipes\ati.die.v1.json
```

이미지 정보 확인 + 축소 미리보기 PNG 저장:

```bash
src\VisionStack.Cli\bin\Release\net8.0\visionstack.exe probe --image D:\Local_ATI\ATI_Image\ROI_Mono_585MB.bmp --preview preview.png --step 20
```

---

## 실행 결과 (실제 이미지)

`ROI_Mono_585MB.bmp`는 ATI의 **결함 검사용 테스트 차트**가 4×8로 반복 인쇄된 이미지다.
열/행 프로파일 자기상관으로 확인한 die 피치는 정확히 **2500 × 2500 px**.

```
 dies    : 32 inspected, 31 FAIL
 defects : 263
 elapsed : 9.1 s

--- wafer map (. = pass, digit = defect count, # = 9+) ---
      c0 c1 c2 c3
 r0   5  #  8  .
 r1   #  #  #  1
 r2   7  #  7  1
 r3   #  #  8  3
 r4   7  #  #  1
 r5   #  #  #  1
 r6   7  #  7  1
 r7   #  #  8  3
```

- **c0·c1 열의 16개 die**에는 체커보드 밴드 한가운데에 지름 100px(원본 기준)짜리 검은 원이
  심어져 있고, 파이프라인이 매번 면적 ~2000px(비닝 2 기준)의 `Blob`으로 잡아낸다.
  ```
  #1  Blob  die(437,520)  50x50  area=1976  circ=0.62  wafer(3424,8590)
  ```
- **c2·c3 열**에는 그 원이 없고, 최대 결함 면적이 100px 미만으로 떨어진다.
- 나머지 잔여 후보(면적 9~99px)는 눈금자 문자 · 격자 경계에서 나오는 전형적인 **뉴슨스**다.
  `ati.die.v1.json`은 브리핑의 원형 그대로라 이것들을 전부 올려보내고, 그래서 31개 die가 FAIL로
  찍힌다. **판정에 쓰려면 `ati.die.v3.tuned.json`을 쓴다** — 면적 게이트 하나로 16 FAIL / 16 PASS,
  즉 정답과 정확히 일치하는 맵이 나온다(아래 레시피 항목 참고).
- 이미지 아래 절반(r4~r7)이 위 절반(r0~r3)과 결과가 같다. 20000px 높이가 10000px 패턴의
  두 번 반복이라는 뜻이고, 파이프라인이 결정론적으로 도는지 확인하는 덤이기도 하다.

산출물은 `out/`에 떨어진다.

| 파일 | 내용 |
|---|---|
| `result.json` | die별 결함 목록 (die 상대 좌표 + 원본 절대 좌표) |
| `report.txt` | 사람이 읽는 리포트 + 웨이퍼 맵 |
| `overlay/die_c{c}_r{r}.png` | 결함 bbox를 빨간 사각형으로 표시한 오버레이 |
| `stages_c{c}_r{r}/*.png` | `dumpStages` 지정 시 golden·goldenNorm·diff·filtered·mask 전부 |

---

## 프로젝트 구조

```
src/VisionStack.Core/         타입 · 알고리즘 · 레지스트리 · 레시피 · 실행기
  Imaging/Images.cs             GrayF32 · Gray8 · ImageList
  Imaging/Kernels.cs            가우시안 · 이항 · 미분(Sobel/Laplacian) 커널
  Imaging/Ops.cs                컨볼루션 · 모폴로지 · CLAHE · Otsu · 워핑 · 비닝
  Imaging/Fft.cs                radix-2 FFT · 위상상관(Foroosh 서브픽셀)
  Imaging/ConnectedComponents.cs 2-pass union-find 라벨링 + 통계
  Imaging/BmpCodec.cs           BMP 리더(부분 사각형 seek 읽기) · 라이터
  Imaging/PngWriter.cs          최소 PNG 인코더(오버레이 저장용)
  Model/Defects.cs              Rect · Defect · DefectList · DistanceList
  Registry/                     PortKind · ParamDescriptor · ParamBag · NodeRegistry
  Recipe/                       RecipeModel · Loader · Validator
  Runtime/                      Blackboard · PipelineRunner

src/VisionStack.Stages/       노드 15종 구현 + 레지스트리 등록
src/VisionStack.Cli/          validate / run / list / probe + die 격자 공급자 + 리포트
tests/VisionStack.Tests/      xunit 210개
samples/recipes/              레시피 4종
samples/cases/                검사 케이스 1종
run-demo.bat                  더블클릭용 메뉴 실행기
```

---

## 노드 15종

| 카테고리 | 노드 | 요약 |
|---|---|---|
| Golden | `Combine` | 정상 die들 → Golden. Mean·Median·TrimmedMean·SigmaClipped·Percentile·MinMaxReject |
| Golden | `Normalize` | GaussianBlur·MedianBlur·BackgroundSubtract·None·CLAHE·ZScore |
| Reference | `CellToCell` | pitch만큼 민 자기 이미지를 참조로 (어레이 영역) |
| Alignment | `Align` | Translation(위상상관+Hanning, 서브픽셀) · ECC(이동/유클리드/아핀) · None |
| Comparison | `Difference` | 참조 밴드 대비 초과분. operator × diffPolicy × neighborMode × windowSize |
| Preprocess | `Filter` | Median·Gaussian·Morphology·Sobel·Laplacian·Bilateral 체인 + weight/min/max/rangeMode |
| Decision | `Binarize` | Threshold·Otsu·Range·Sigma·Adaptive·Hysteresis |
| Output | `Label` | 연결요소 → DefectList (+intensity 시 밝기, computeShape 시 원형도) |
| Output | `DefectMerge` | 여러 목록 병합 + 중복 제거(None·OverlapIoU·Centroid) |
| Output | `DefectFilter` | 종횡비 · 채움비 · 원형도 게이트 |
| Output | `DefectClassify` | 룰 기반 Scratch / Blob / Particle |
| Output | `DefectCluster` | bbox 팽창 → union-find 그룹핑 |
| Output | `DefectLimit` | 면적 필터 + 개수 상한 (area 내림차순, 동률 Id 오름차순) |
| Geometry | `MaskApply` | care area 한정 |
| Geometry | `Distance` | NearestBoundary · Centroid |

`visionstack list`로 각 노드의 포트·파라미터·기본값·범위를 그대로 볼 수 있다
(디스크립터가 단일 진실원이라 문서·검증·UI가 전부 거기서 파생된다).

---

## 레시피

### `ati.die.v1.json` — die-to-die 표준 흐름
브리핑의 선형 흐름 그대로. `Combine:Median → Normalize:GaussianBlur → Align:Translation →
Difference(MinMax 밴드) → Filter(Median+Open) → Binarize:Threshold 0.12 → Label →
Cluster → Classify → Limit`.

### `ati.die.v2.multipass.json` — 멀티패스
같은 `filtered`를 민감(0.08, minArea 30)·둔감(0.25, minArea 6) 두 `Binarize`가 구독하고
`DefectMerge:OverlapIoU`로 합친다. 루프가 아니라 **분기 후 합류**다.

### `ati.die.v3.tuned.json` — 판정용 (뉴슨스 컷)
v1과 배선이 완전히 같고 **`DefectLimit.minArea`만 6 → 100**으로 올린 것. 이 한 줄 차이로
웨이퍼 맵이 쓸 수 있는 물건이 된다.

```
v1 (원형)                        v3 (튜닝)
 dies : 32 inspected, 31 FAIL     dies : 32 inspected, 16 FAIL
 defects : 263                    defects : 16
      c0 c1 c2 c3                      c0 c1 c2 c3
 r0   5  #  8  .                  r0   1  1  .  .
 r1   #  #  #  1                  r1   1  1  .  .
 r2   7  #  7  1        ─────►    r2   1  1  .  .
 r3   #  #  8  3                  r3   1  1  .  .
 ...                              ...
```

오른쪽이 정답이다 — c0·c1 열 16개 die에만 검은 원이 심어져 있다.
100이라는 값은 임의가 아니라 **후보 면적 분포의 공백**에서 나왔다:

| 면적 | 개수 | 정체 |
|---|---|---|
| 1 ~ 19 | 234 | 눈금자 문자·격자 경계 잔여물 |
| 20 ~ 49 | 8 | 〃 |
| 50 ~ 99 | 5 | 〃 |
| *100 ~ 1958* | **0** | ← 여기가 비어 있다 |
| 1959 ~ 2012 | 16 | 심어진 검은 원, die당 정확히 1개 |

그 공백 안이면 아무 값이나 되지만, 감도를 최대한 남기려고 아래쪽 끝을 골랐다.
비닝을 1로 바꾸면 면적이 4배가 되므로 400으로 올려야 한다.

### `case2.single.threshold.json` — 참조 없는 흐름
golden도 Align도 없이 `roi → Filter → Binarize:Range → Label → Cluster → Limit`.
"결함은 이 밝기대에 있다"는 사전 지식만으로 검은 원을 정확히 하나 잡는다.

---

## 검사 케이스 (`samples/cases/ati_roi.case.json`)

```jsonc
{
  "image": "D:/Local_ATI/ATI_Image/ROI_Mono_585MB.bmp",
  "grid": { "originX": 0, "originY": 0, "pitchX": 2500, "pitchY": 2500,
            "cols": 4, "rows": 8, "dieWidth": 2500, "dieHeight": 2500 },
  "binning": 2,                                  // 2x2 평균 비닝 -> 타일 1250x1250
  "goldenDies": [[0,0],[1,0],[2,0],[3,0],[0,4],[1,4],[2,4],[3,4]],
  "inspectDies": [],                             // 비우면 전체
  "failDefectCount": 1,
  "output": { "dir": "../../out", "saveOverlay": true, "maxOverlays": 8, "dumpStages": "1,1" }
}
```

- **검사 대상 die는 Golden 계산에서 자동으로 빠진다.** 자기 자신이 기준에 섞이면 결함이 지워진다.
- `dumpStages: "1,1"` — 그 die의 중간 산출물이 전부 PNG로 떨어진다. 파이프라인이 무슨 일을
  했는지 눈으로 확인할 때 제일 유용하다. `diff.png`를 열어보면 심어놓은 검은 원만 하얗게
  떠 있고 나머지는 거의 까맣다 — 차영상이 왜 "검출의 심장부"인지가 한눈에 보인다.
  (`diff`처럼 값이 작은 영상은 저장할 때 [0,1]로 늘려 보여준다.)
- CLI 덮어쓰기: `--binning 1`, `--dies "1,1;3,2"`, `--out <dir>`.

---

## 585MB 이미지를 다루는 방법

`BmpReader.ReadRegion(x, y, w, h)`가 **필요한 행만 seek해서 읽는다.** 전체를 메모리에
올리지 않으므로 die 타일 하나(2500×2500)를 뜯는 데 수십 MB면 된다.
`DieSource`는 반복 사용되는 Golden 참조 die만 캐시에 고정(pin)하고, 검사 타일은 흘려보낸다.

`Align`의 `estimateSize`(기본 1024)는 위상상관에 쓸 중앙 크롭 변 길이다.
시프트는 전역량이라 결과는 같고 FFT 메모리만 상수로 묶인다. `0`이면 전체 이미지를 쓴다.

---

## 테스트 (xunit, 210개)

```bash
dotnet test
```

| 파일 | 다루는 것 |
|---|---|
| `ImagingTests.cs` | 이미지 타입 · 커널 · 컨볼루션 · 모폴로지 · CLAHE · Otsu · 워핑 · 비닝 |
| `FftTests.cs` | FFT 왕복 · 정수/서브픽셀 시프트 복원 · 시프트 방향 규약 |
| `CombineTests.cs` | 6가지 전략의 성격 (Mean은 오염에 끌려가고 Median은 버틴다) |
| `NormalizeAndAlignTests.cs` | 6가지 정규화 · CellToCell · Translation/ECC 정합 · ECC 폴백 |
| `DifferenceAndFilterTests.cs` | 밴드가 1px 정합오차를 흡수하는지 · 극성 · Gradient/Normalized · 필터 체인 |
| `BinarizeAndLabelTests.cs` | 6가지 이진화 · 4/8 연결 · 면적 게이트 · 원형도 |
| `DefectTailTests.cs` | Cluster · Limit · Filter · Classify · Merge |
| `RecipeValidatorTests.cs` | 미등록 type · PortKind 불일치 · 미배선 · 중복 producer · 선언 전 사용 · 파라미터 범위 |
| `PipelineTests.cs` | 합성 die로 end-to-end · 멀티패스 · 별칭 통과 · 결정론 |
| `CodecTests.cs` | BMP 왕복 · 부분 읽기 · 8bpp 팔레트 · PNG 구조/CRC |
| `RealImageTests.cs` | **실제 585MB 이미지**로 돌리는 통합 테스트 (이미지 없으면 건너뜀) |

테스트가 잡아주는 성질 중 공부에 직접 도움 되는 것들:

- `Median_shrugs_off_the_same_contamination` — 오염된 die 한 장이 Golden을 흔들지 못한다
- `The_MinMax_band_absorbs_a_one_pixel_registration_error` — 밴드의 존재 이유
- `Dropping_Align_makes_the_same_misregistration_swamp_the_image` — Align을 빼면 결함 면적이 10배 이상 부푼다
- `Hysteresis_grows_a_faint_tail_out_of_a_strong_seed` — 끊긴 스크래치 복원
- `Binning_too_aggressively_aliases_the_fine_pitch_bands_into_nuisance` — 4배 비닝하면 Nyquist 근처
  라인쌍 밴드가 앨리어싱을 일으켜 뉴슨스가 폭증한다 (실제 이미지로 확인)

---

## 브리핑과 다르게 구현한 부분

| 항목 | 내용 | 이유 |
|---|---|---|
| `Align.estimateSize` | 브리핑에 없는 파라미터 (기본 1024) | 2500×2500 die를 전체 FFT하면 4096² 복소 버퍼가 필요하다. 시프트는 전역량이라 중앙 크롭으로 추정해도 결과가 같다. `0`으로 두면 전체 사용 |
| `Difference`의 `Signed` | `0.5 + 0.5·d`로 재중심 | `workingFormat: GrayF32 → 임계값 [0,1]` 계약을 지키면서 부호를 살리기 위해 |
| 원형도 | `4π·Area / Perimeter²`, Perimeter는 4-이웃 배경 접촉 변의 수 | OpenCV `arcLength`와 값이 다르지만 순서 관계는 같다 |
| `PortKind` | `Image` 하나가 GrayF32와 Gray8을 함께 담는다(카탈로그와 동일) | 구체 포맷 불일치는 `StageInputs`가 런타임에 배선을 짚어주는 메시지로 잡는다 |
| 웨이퍼 노드 | 미구현 | 브리핑이 die 파이프라인만 다뤘다. `InspectDies`·`TransformAbs`·`Sampling`·`TemplateMatchAll`·`SolveAlignment`는 다음 단계 |
