# 웨이퍼 층 (`DiePipeline.Wafer`)

> die 하나를 검사하는 것은 아래층이 한다.
> 이 층은 **"누가 이웃인가" 와 "웨이퍼가 어떻게 놓였는가" 를 아는 층**이다.

---

## 1. 왜 따로 있나

우리 검사는 *die-to-die* — 옆 die 와 비교해 다른 곳을 결함이라 부른다.
그런데 **"누가 옆인가" 는 웨이퍼 격자만 아는 정보**다. die 사진 한 장 안에는 없다.

그리고 **die 반복은 엔진 밖**이다.

```
엔진 안에 넣을 수 있는 것    ①→②→③→…    순서가 미리 정해진 것
엔진 안에 못 넣는 것          die N개 반복   N이 실행할 때 정해진다
```

JSON 레시피는 노드를 어떤 순서로 놓을지 적는 파일인데, die 개수는 **웨이퍼를 열어 봐야 안다.**
교수님 구현도 Lesson16 도 같은 결론이었다 — 교수님 쪽에는 *"엔진 무루프"* 규칙으로 못 박혀 있다.

---

## 2. 경계 — **"화면을 모른다"**

| | 4주차 | 지금 |
|---|---|---|
| 참조 | `Core` | `Core` + `Cv` |
| 경계 문장 | "OpenCV 를 모른다" | **"화면을 모른다"** |

쓰는 쪽이 명령행 하나일 때는 흐름을 명령행이 들고 있어도 됐다.
**화면이 두 번째로 쓰려 하자** 베끼거나 꺼내거나가 됐고, 베끼면 *"정렬을 못 믿으면 멈춘다"* 가드가
한쪽에만 남는 날이 온다. 그래서 흐름을 `WaferInspection` 으로 꺼냈다.
교수님도 같은 자리에서 같은 선택을 했다 — ***"UI 무의존이지 백엔드 무의존이 아니다."***

**`WaferRunner` 는 여전히 검사 함수를 받는다.** 격자·이웃·실패 기록은 지금도 사진 없이 시험된다.

---

## 3. 파일 지도 — 19개

```
DiePipeline.Wafer/
 ├ Config/
 │   ├ WaferSpec.cs              웨이퍼 형상 — die 배치 + 원판(중심·반지름) · 가장자리 여유 · 수동 지정
 │   └ WaferInputText.cs         글자 입력 규칙 — 점 · 수동 지정(2,3=off). 명령행과 화면이 같이 쓴다
 ├ Domain/Wafer.cs               WaferZone · DieOrigin · DieGrid · WaferAlignment
 │                               SimilarityTransform · WaferDefect · DieState · DieResult
 ├ Execution/
 │   ├ DieGridBuilder.cs         형상 + 정렬 → die 원점 격자 (수동 지정은 자동 판정 뒤에 덮어쓴다)
 │   ├ AlignmentSolver.cs        표식 자리 → 놓인 자세 (가드 4종)
 │   ├ GoldenSelector.cs         이 die 의 골든을 누구로 만들까
 │   ├ GoldenContract.cs         레시피가 골든 몇 장을 전제하는가
 │   ├ WaferRunner.cs            die 목록을 돌린다 — 검사 함수를 받는다
 │   ├ DefectCollector.cs        die 별 결과 → 한 목록 (꼬리표만)
 │   ├ AbsoluteTransform.cs      die 안 좌표 → 웨이퍼 절대 좌표
 │   ├ WaferSampler.cs           결과가 많을 때 추리기
 │   ├ DefectWindow.cs           결함 하나를 원본에서 볼 창 (화면의 '결함 원본 보기')
 │   ├ WaferInspection.cs        ★ 위 전부를 꿰는 흐름 + 격자 경고 · 원판 마스크 · 정렬 창
 │   ├ WaferRecipeRunner.cs      ★ 웨이퍼 레시피를 토막 내서 돌린다 — 속은 WaferInspection
 │   ├ WaferRecipePaths.cs       레시피를 다른 폴더에 저장할 때 안의 경로를 새 폴더 기준으로 고쳐 적기
 │   └ WaferMap.cs               검사 결과 → 웨이퍼 맵 칸(상태 · 결함 수). 화면이 그리기만 한다
 └ Processing/                   웨이퍼 레시피의 블록 (교수님과 같은 이름 · 자리)
     ├ WaferNodes.cs             자세 계산 · die 전부 검사(경계) · 웨이퍼 좌표 · 결과 추리기
     ├ WaferFactories.cs         위 넷의 설명서 — 파라미터 · 실행 전 검사
     └ WaferBackend.cs           웨이퍼 레시피용 명부 = Cv 블록 전부 + 위 넷
```

**사진을 만지는 것은 `WaferInspection` · `WaferRecipeRunner` 둘뿐.** 나머지는 이미지 없이 밀리초로 시험된다.
(표식 찾기 블록 `TemplateMatchAll` 은 그림을 다뤄서 `Cv` 층에 있다.)

---

## 4. 흐름

```
   [사진]  +  [E1 레시피]  (+ [E0 레시피])  (+ [표식 조각])
      │
 0 ───┼─  레시피 검증 · 노드 한 번만 만들기 · 골든 계약 읽기
      ▼
 1 ─  표식 찾기               대략 원점 + 설계 자리 ± 여유 창만 읽는다 (여유를 안 주면 사진 전체)
                              ※ 표식을 안 주면 건너뜀 → 항등 정렬
      ▼
 2 ─  정렬 풀기 ──✗ 못 믿겠다 ──────▶ 멈춤  AlignmentRejected
      │        └─✗ 기울기가 크다 ──▶ 멈춤  RotationNeedsWarp
      ▼
 3 ─  격자 만들기              설계 격자 × 정렬 · 존 판정(원판 있으면 원으로, 없으면 겹 수로) · 격자 경고
      ▼
 4 ─  die 루프 (row-major)
      │     원판 밖 → 검사 안 함 (die 아님)
      │   ROI 자르기 + 경계로 밀어 넣기
      │     E1 → 이웃 고르기 → 골든 ROI → E1 레시피   (골든 부족 → 이 die 만 실패)
      │     E0 → E0 레시피                          (E0 레시피 없음 → 통째로 중단)
      │     레시피가 waferMask 를 받으면 → 원판 안 마스크를 같이 넘긴다
      │   터지면 → 이 die 만 실패로 적고 계속
      ▼
 5 ─  모으기        꼬리표만 단다
 6 ─  절대 좌표     abs = dieOrigin + s·R(θ)·(중심 + ReadOffset)    ReadOffset = 잘라 읽은 자리 − die 원점
 7 ─  샘플링        ★ 검사를 덜 하는 게 아니라 결과만 추린다
      ▼
   [결과]  — 출력은 안 한다. 명령행은 글자로, 화면은 맵으로.
```

---

## 5. 웨이퍼 레시피 — 입력을 파일 하나로

격자 · 원판 · 수동 지정 · 표식을 명령줄에 매번 적는 대신 **파일 하나**(`*.wafer.json`)에 적는다.
교수님 Phase 8 의 통합 블록 레시피와 같은 모양이다.

```
[TemplateMatchAll] → [SolveAlignment] → [InspectDies] → [TransformAbs] → [Sampling]
 ① 표식 찾기          ② 자세 계산        ③ die 전부 검사     ④ 웨이퍼 좌표 · 결과 추리기
 창마다 반복           1번               die 마다 반복        1번
```

**토막 내서 돈다.** 엔진은 블록을 한 번씩만 돈다. 그런데 ①은 창마다, ③은 die 마다 돌아야 한다.
그래서 `WaferRecipeRunner` 가 레시피를 `InspectDies`(경계 블록 — 엔진이 돌리면 멈춘다)에서 자르고,
앞은 '표식 목록' 칸에서 한 번 더 자른다. 교수님은 정렬 토막을 사진 전체에서 한 번 돌린다 — 창은 우리만 있다.

| 규칙 | 내용 |
|---|---|
| 입구는 둘, 속은 하나 | 명령행 옵션 · 화면 입력 · 레시피 파일이 모두 `WaferInspection` 을 지난다. 정렬 가드 · 격자 · die 반복이 한 곳이다. 두 입구가 같은 답인지 시험이 본다 |
| 값은 한 곳에 | 표식 설계 자리 · 대략 원점(`firstDieX/Y`) · 판정 기준은 `SolveAlignment` 에만. 격자 · 원판 · 수동 지정 · 조각 경로는 `InspectDies` 에만. 교수님 견본은 표식 자리를 두 블록에 적는다 |
| 경로 | 안에 적힌 die 레시피 · 조각 경로는 **레시피 파일이 있는 폴더 기준**(교수님 CLI 와 같다). 절대 경로도 된다. 푸는 규칙은 `PathIn` 한 곳 |
| 화면 입구 | 웨이퍼 에디터는 아직 파일로 안 쓴 그래프를 돌린다 → `RunIn(레시피, 폴더)`. `RunFile` 도 이걸 거친다. 다른 폴더에 저장하면 `WaferRecipePaths.Rebase` 가 `e1Recipe` · `e0Recipe` · `features[].template` 을 새 폴더 기준으로 고쳐 적는다 |
| 실행 전에 막는다 | `InspectDies` 가 0개·2개 · 어디에도 안 이어진 블록 · 끝 블록이 2개 · 조각이 2개 → die 를 하나도 돌기 전에 거부(끝값 2) |
| 섞지 않는다 | `--recipe` 는 `-i` 말고 다른 옵션과 같이 못 쓴다 — 어느 값이 쓰였는지 결과에서 안 보인다 |
| 이름 | `edgeExclusionPx` = 원판 가장자리 여유(px, 교수님 문서의 뜻) · `edgeRings` = 테두리 겹 수. 우리에게만 있는 것: `circle` · `zones` · `searchPaddingPx` · `neighbors` · `dieWidth/Height` · `firstDieX/Y` |

```json
{ "id": "inspect", "type": "InspectDies", "inputs": { "alignment": "alignment" },
  "params": { "e1Recipe": "../recipes/die.diff.json", "cols": 4, "rows": 3, "pitchX": 100, "pitchY": 100,
              "features": [ { "template": "features/demo_mark.bmp" } ] } }
```

견본은 `samples/wafer/demo.wafer.json` — `diepipe demo` 기본 그림에 맞춘 것. 조각 그림은 코드가 그린 것과 픽셀까지 같은지 시험이 본다.

> ⚠ 실데이터 웨이퍼 레시피는 **레포 밖**(사진 옆)에 둔다 — 격자 · 피치 · 원판 위치가 들어간다.

---

## 6. 꼭 알아야 할 것 다섯

**① 설계와 놓인 자세는 다른 것** — `WaferSpec`(제품마다 고정) × `WaferAlignment`(한 장마다 다름).

**② `Unknown = 0`** — `WaferZone`·`NeighborStrategy`·`SamplingMode`·`DieState` 전부.
C# enum 기본값이 0이라, 초기화를 빠뜨린 값이 조용히 `E1` 이 되면 **이웃 없는 die 에 die-to-die 를 시도**한다.
존에는 "무해한 기본값" 이 없다.

> ⚠ **E0 는 "이미지 테두리" 가 아니라 "원판 가장자리"** 다. 안쪽을 부분 촬영한 사진이면 전부 E1 일 수 있다.
> 두 방법으로 받는다 (같이 못 쓴다)
>   원판 `Circle` : 네 귀퉁이가 다 원 안 = E1 · 일부만 = E0 · 하나도 안 닿음 = Outside(die 아님)
>   겹 수 `EdgeRings` : 원판 값이 없을 때의 근사
> 가장자리 여유 `EdgeExclusion` 은 잰 원은 그대로 두고 검사에 쓰는 원만 줄인다(존 판정 · 마스크 둘 다).

**③ ★ 파라미터는 짝이다** — 이 층에서 제일 중요한 규칙.

```
이웃 고르는 법  ×  합치는 법  ×  차이의 부호     ← 셋이 한 덩어리다
```

정하는 자리가 떨어져 있다(합치기는 레시피, 이웃 고르기는 명령행/화면).
**틀려도 안 터진다** — 이웃의 결함이 되비쳐(**미러**) 결함 수가 조용히 부풀 뿐이다.
그래서 `GoldenContract` 가 **레시피에서 필요한 장수를 유도**한다.

| 합치는 법 | 최소 | 까닭 |
|---|---|---|
| `Median` | 3 | 2장이면 평균과 같아져 미러 |
| `Percentile`·`Min`·`Max` | 2 | 한 장이면 고를 것이 없다 |
| `Mean` | 1 | 장수와 무관하게 한 장은 있어야 한다 |

차이 부호가 `Absolute`(또는 안 적음)면 합치는 법과 **무관하게** 미러 경고.
`Median` 도 3장 중 2장이 결함으로 겹치면 넘어간다(5주차 — 합성에서 심은 3개가 4개로).
막는 것은 장수가 아니라 **부호**다.

**④ `DieState` 셋은 색이 달라야 한다** — `Clean`(봤고 깨끗) / `Defective` / `Failed`(**못 봤다**).
`Failed` 도 결함 수는 0이다. 요약을 만들 때 **실패를 먼저** 본다.
`OffWafer`(원판 밖 — die 가 아니다. 결함 수에도 die 수에도 안 들어간다)

**⑤ 못 믿으면 시작조차 안 한다** — `AlignmentRejected`(점수·각도·배율·어긋남) ·
`RotationNeedsWarp`(기울기 0.05° 초과). 결과를 내고 "믿지 마세요" 라고 하면 늦다.

---

## 7. 이 층이 막는 함정 — 전부 **안 터지고 틀리는** 것들

| 증상 | 진짜 원인 | 막는 곳 |
|---|---|---|
| "결함이 왜 이렇게 많지?" | 격자/피치가 틀려 엉뚱한 자리를 비교 | `DieGridBuilder` 입구 |
| 같은 결함이 두 번 잡힘 | die 크기 > 피치 → ROI 겹침 | `DieGridBuilder` |
| 좌표가 전부 (0,0) | 정렬 `Scale` 이 0 | `DieGridBuilder` |
| die 가 조용히 건너뛰어짐 | 골든 ROI 가 **1px** 달라 `Combine` 거부 | `GoldenSelector` — 잘라내지 말고 **밀어 넣는다** |
| 결함 수가 조용히 부풂 | 짝이 어긋남(미러) | `GoldenContract` |
| 깨끗해 보이는데 사실 안 봄 | 터진 die 와 깨끗한 die 가 같은 모양 | `DieState.Failed` |
| 맵의 점이 살짝 밀림 | 좌표 변환이 여러 곳에 흩어짐 | `AbsoluteTransform` 한 곳 |
| 오검 천 개 | 정렬이 틀렸는데 그대로 검사 | `WaferStop` |
| 두 번 돌리면 결과가 다름 | 순서·무작위가 안 고정됨 | row-major · 씨 고정 |
| 사진 귀퉁이만 보고 결과가 나옴 | 격자가 사진 일부만 덮음 | `WaferInspection` 격자 경고 |
| 원판에 걸친 die 의 배경이 결함 | E0 레시피가 원판 밖을 안 가림 | `MaskApply` + 원판 짝 경고 |
| 잘린 die 의 결함이 엉뚱한 자리 | 절대 좌표에 잘리기 전 자리를 더함 | `ReadOffset` |

> **이 층의 성격** — 고장이 나도 **프로그램이 죽지 않는다.** *그럴듯하게 틀린 숫자*만 나온다.
> 그래서 **입구에서 막고 · 못 믿으면 멈추고 · 상태를 타입에 적는다.**

```
우리 계산이 틀렸다(설정 오류)  → 통째로 멈춘다   fail-loud
받은 함수가 터졌다             → 그 die 만 실패   fail-soft
```

---

## 8. 쓰는 법

```
mkdir out                                   ← demo 는 폴더를 만들지 않는다
diepipe demo  -o out/wafer.bmp --grid 4,3 --pitch 100 --defects 3 --seed 1
diepipe wafer -i out/wafer.bmp --e1 samples/recipes/die.diff.json --grid 4,3 --pitch 100 \
              --mark out/wafer.mark.bmp --expect -40,-40 --expect 410,310
```

원판(E0)과 정렬 창:

```
diepipe demo -o out/round.bmp --round      ← 원판 값과 다음에 칠 명령을 같이 출력한다
diepipe wafer … --circle <x,y,r> --e0 samples/recipes/die.dark.masked.json --edge-exclusion <px>
diepipe wafer … --mark <조각> --expect … --origin <대략 x,y> --mark-padding <px>
```

`demo` 가 **심은 결함 자리와 놓인 자세를 같이 출력한다** — 정답표가 같이 나오는 셈이다.

```
  r0    .  1  .  .          숫자=결함 수 · `.`=깨끗 · `-`=E0 · `×`=실패
  r1    .  .  .  .
  r2    1  .  1  .          die 12개 · 결함 3개 · 37 ms
```

같은 것을 웨이퍼 레시피 파일로 — 옵션 대신 파일 하나:

```
diepipe wafer -i out/wafer.bmp --recipe samples/wafer/demo.wafer.json
```

**심은 3개 ↔ 찾은 3개.** 그리고 **망가뜨려 보는 것이 더 중요하다.**

| 해 보는 것 | 나와야 하는 결과 |
|---|---|
| `--angle 0.7` | `RotationNeedsWarp` — 검사를 시작도 안 함 |
| `--angle 2.5` | `AlignmentRejected` |
| `Median` 레시피 + `--neighbors same-row1` | 골든 부족 → 전 die 실패 |
| `Mean`+`Absolute` 레시피 | 미러 경고 · 결함 수 부풂 |
| `--circle` + `--e0 die.dark.json`(안 가리는 레시피) | 원판 짝 경고 · E0 결함 부풂 |

> ⚠ `--grid` · `--pitch` · `--origin` 과 원본 파일 이름은 **제품 배치 정보**다.
> 명령줄에만 쓰고 문서·커밋에 남기지 않는다. 문제는 숫자가 아니라 *원본과 묶이는 문장*이다.

---

## 9. 지금 **없는** 것

| 없는 것 | 무슨 일이 안 되나 |
|---|---|
| **`warp read`** ★ | die 를 펴서 못 읽는다 → 기울기 **0.05°** 까지만. 붙이는 날 그 가드를 **같이** 지운다 |
| **이벤트** | die 하나 끝날 때마다 알릴 길이 없다 → 진행률이 안 찬다 |
| **`result.json`+provenance** | 결과를 파일로 못 넘기고, "어떤 레시피로 나온 값인가" 를 못 적는다 |
| **`Cluster`** | 흩어진 점 20개를 "긁힌 자국 1개" 로 못 묶는다 |
| **노치 · 플랫 자동** | 원으로만 본다 — 노치 die 는 사람이 수동 지정(`--zone 2,3=off`)으로 뺀다 (교수님도 "장비 통합 시") |

웨이퍼 레시피 블록 파라미터와 명령행 옵션은 1:1 로 맞는다
(`e1Recipe`→`--e1`, `cols/rows`→`--grid`, `pitchX/Y`→`--pitch`, `circle`→`--circle`,
`edgeExclusionPx`→`--edge-exclusion`, `edgeRings`→`--edge-rings`, `zones`→`--zone`,
`features[].template`→`--mark`, `expected`→`--expect`, `firstDieX/Y`→`--origin`(정렬할 때) · `originX/Y`→`--origin`(정렬 안 할 때), `searchPaddingPx`→`--mark-padding`,
`minScore/maxAngleDeg`→`--align-*`).

> 이벤트를 붙일 때 미리 못 박을 것 — 시각·소요 ms 는 **비결정 값**이라
> 결과 목록에 섞으면 회귀 비교가 무너진다. 이벤트 스트림과 provenance 봉투에만 둔다.

---

## 10. 숫자

| | |
|---|---|
| 웨이퍼 층 파일 | **19개** (사진을 만지는 건 2개) |
| 전체 테스트 | **618개** |
| 합성 4×3 · 12 die | **35 ms** (die 하나 2.9 ms) |
| 웨이퍼 레시피 블록 | **5종** (표식 찾기는 Cv 층) |
| warp 없이 믿는 기울기 | **0.05°** |
| 정렬 가드 | 4종 (점수 0.8 · 각도 2° · 배율 2% · 어긋남 3px) |

---

> **이 층의 고장은 프로그램을 죽이지 않는다. 그럴듯하게 틀린 숫자만 낸다.**
> **이웃 고르는 법 · 합치는 법 · 차이의 부호는 한 덩어리다.**
