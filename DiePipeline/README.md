# DiePipeline

반도체 웨이퍼 검사에서 **die 하나의 결함을 찾아내는 파이프라인**.
알고리즘은 부품으로 쪼개져 있고, 조립 순서와 값은 **JSON 레시피 파일**이 정한다.

기준 레포: `visionstack_proto` (교수님 제공)

```bash
dotnet build DiePipeline.slnx
dotnet test  DiePipeline.slnx          # 96개
dotnet run --project src/DiePipeline.App -- demo
```

---

## 무엇을 하는가

```
ROI + 정상 die들 → Combine(골든) → Normalize → Align → Difference → Filter → Binarize → Label
                                                                                          ↓
                                                                                     결함 목록
```

핵심 아이디어 한 줄: **결함이 뭔지 배우지 않고, 정상이 뭔지 만들어 두고 뺀다.**

반도체 라인은 정상 수십만 장에 불량 수십 장이고, 새로운 종류의 결함은 정의상 학습 데이터에 없다.
"불량을 배우는" 방식이 성립하지 않는 도메인이라 이 구조를 쓴다.

## 30초 체험

```bash
dotnet run --project src/DiePipeline.App -- demo --timings
```

파일 없이 합성 die를 만들어 전체 흐름을 돌린다.

```
레시피 die.diff.v1 — 노드 8개
기대 결과: 결함 1개, 중심 (100,100) 근처, 면적 약 113

결함 1개  (26.0 ms)
  #1   (94,95) 13×12          area=    119  중심=(99.9,100.2)  평균밝기=0.025
```

---

## 구조

```
src/DiePipeline.Core      엔진 · 도메인 · 레시피 · 명부        ← OpenCV를 모른다
src/DiePipeline.OpenCv    알고리즘 · 노드 · 팩토리 · 등록
src/DiePipeline.App       콘솔 (diepipe)
tests/DiePipeline.Tests   xUnit 96개
samples/recipes           레시피 예시
```

### `Core`가 OpenCV를 모르는 이유

나중에 WPF와 DB를 붙일 때 **Core를 안 건드리기 위해서**다.
UI는 `Core`의 타입(`Defect`·`Recipe`·`INodeDescriptor`)만 알면 되고, 픽셀은 몰라도 된다.

그 경계 덕분에 Core 테스트는 **이미지 파일 없이 밀리초 단위로** 돈다.

### 엔진은 이게 전부다

```csharp
foreach (IPipelineNode node in nodes) node.Execute(context);
```

> 유연성은 엔진의 크기가 아니라 **경계의 위치**에서 나온다.

"조건 분기를 넣자", "반복을 넣자"는 요구가 오면 엔진이 아니라 **노드 구성**으로 푼다
(멀티 임계 → 노드를 펼치고 병합, die N개 반복 → 상위 실행기가 돈다).

---

## 사용법

```bash
# 등록된 노드와 파라미터 — 설명서가 코드에서 파생된다
diepipe nodes

# 레시피만 검증(이미지 없이). 오류를 한 번에 전부 보고한다
diepipe validate --recipe samples/recipes/die.diff.json

# 실제 검사
diepipe run --recipe samples/recipes/die.diff.json --image die.png \
            --golden ok1.png --golden ok2.png --golden ok3.png --timings
```

### 실데이터 준비 도구

레시피의 값을 정하려면 먼저 이미지를 **재야** 한다. 나중에 WPF가 마우스로 하게 될 일이다.

```bash
diepipe stats   --image wafer.png                       # 밝기 분포 → 임계값 후보 구간
diepipe crop    --image wafer.png --rect 200,200,100,100 \
                --grid 5x1 --pitch 100,100 --out cells/  # 셀 어레이 잘라내기
diepipe measure --image cell_2.png --golden cell_1.png   # 어긋남을 재기만(보정 안 함)
diepipe inject  --image cell_2.png --spot 50,50,3 --out hurt.png   # 답을 아는 결함 심기
```

전체 옵션은 `diepipe --help`.

**실측 기록** → [docs/real-data-check.md](docs/real-data-check.md)
실제 장비 이미지에서 오검 0, 지름 2µm까지 검출. 정합이 조용히 오검 11개를 만들던 문제도 여기서 찾았다.

**WPF 전 점검** → [docs/pre-wpf-check.md](docs/pre-wpf-check.md)
1024×1024 처리 90ms → 44ms, 예외 경로 Mat 누수 9곳 제거. 결과는 바뀌지 않았다.

### 레시피

```json
{
  "recipeId": "die.diff.v1",
  "workingFormat": "GrayF32",
  "contextInputs": [ "roi", "chipRegions" ],
  "pipeline": { "nodes": [
    { "id": "golden", "type": "Combine",
      "inputs": { "regions": "chipRegions" }, "outputs": { "result": "golden" },
      "params": { "strategy": "Median" } }
  ] }
}
```

| 규약 | |
|---|---|
| `workingFormat` | 임계값의 스케일 기준. `GrayF32`면 픽셀이 `[0,1]` |
| `contextInputs` | 호출자가 올려줘야 하는 이름표 |
| `defectList` | **고를 수 없는 출력 이름**. 종단은 반드시 이 이름이어야 한다 |
| 주석·후행 쉼표 | 허용한다. 값을 고른 이유를 파일에 남겨야 하므로 |

---

## 값을 고르는 규칙

감으로 고르지 않는다.

```
임계값     노이즈 출렁임 × 4  ≤  임계  ≤  결함 대비 ÷ 2
minArea    가장 작은 진짜 결함 면적의 1/10 안팎
diffPolicy 결함이 어두우면 DarkOnly — 미러 오검이 구조적으로 사라진다
```

`Difference`의 `neighborMode: MinMax`는 골든의 주변 밴드와 비교해
die 간 ±1px 정합 오차를 흡수한다. 실측으로 잔차가 10배 이상 줄어든다.

---

## 설계 원칙

| 원칙 | 실체 |
|---|---|
| **의존은 위→아래만** | `Core`는 백엔드를 모른다 |
| **새 알고리즘 = 클래스 + 노드 + 팩토리 + 등록 1줄** | 고치는 파일은 `OpenCvBackend.cs` 하나. 엔진·Core는 안 바뀐다 |
| **디스크립터가 단일 진실원** | 검증·문서·(나중에) UI가 `ParamDescriptor`에서 파생된다. `diepipe nodes`가 그 증거 |
| **실행 전 검증** | 오류를 **전부 모아서** 보고한다. 장비에서는 웨이퍼를 집기 전에 알아야 한다 |
| **얇은 추상화** | `IImage`는 핸들일 뿐. 픽셀 연산은 알고리즘이 `Mat`으로 직접 |
| **메모리 소유권** | 중간 버퍼는 풀이 소유. 실행이 끝나면 대여 중 0이어야 한다 |

### 디스크립터로 표현되지 않는 것

`ParamDescriptor`가 파생시키는 것은 **필드 하나의 범위**까지다.
`minArea < maxArea` 같은 **필드 사이의 관계**는 파생되지 않아 손으로 쓴다.

---

## 테스트

```bash
dotnet test DiePipeline.slnx
```

| 종류 | 내용 |
|---|---|
| **합성 오라클** | 답을 아는 이미지를 만들어 쓴다. 실제 사진은 정답을 아무도 모른다 |
| **골든마스터** | 심은 결함 하나를 정확히 하나로 찾는가 / 깨끗한 die에서 0개인가 |
| **한계 문서화** | "밝은 결함은 단일 임계로 못 잡는다"를 테스트로 남긴다 |
| **누수 검출** | 실행 후 대여 중인 버퍼가 0인가 |
| **결정론** | 같은 입력 두 번 → 같은 결과 |

---

## 실데이터

교수님이 주신 실제 이미지는 **레포에 넣지 않는다.**
원본은 레포 밖에 두고 경로만 명령행 인자로 넘긴다. `.gitignore`는 2차 방어선이다.

---

## 앞으로

| | |
|---|---|
| 알고리즘 확장 | `DefectMerge` · `DefectFilter` · `DefectCluster` · `CellToCell` 등 |
| **WPF** | 이미지 위 결함 표시 + **측정 도구**(pitch·밝기·결함 크기를 클릭으로) + 파라미터 슬라이더 |
| **DB · 네트워크** | 검사 이력 저장·조회, 호스트 전송 |
| 웨이퍼 층 | die N개를 도는 상위 실행기 — 엔진은 그대로 두고 위에 얹는다 |
