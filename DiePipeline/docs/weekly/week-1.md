# 1주차 — 학습 자료를 정리했다

`2026-09-01 ~ 09-04` · 학습 정리 레포

---

## 한 줄

**Lesson 번호로만 쌓여 있던 학습 파일을 주제별로 다시 묶고, 코드마다 설명 문서를 붙였다.**

---

## 1. 왜 정리부터 했나

Lesson01~16을 순서대로 하면 폴더에 남는 것은 **번호뿐**이다.
"Lesson12에서 뭘 했더라"를 알려면 열어 봐야 한다.

> **비유** — 공책을 날짜순으로만 쓰면 나중에 *"삼각함수 어디 있지"* 를 못 찾는다.
> 날짜는 **배운 순서**지 **찾는 순서**가 아니다.

더 큰 문제가 하나 더 있었다. Lesson은 *"이 개념을 이해했다"* 에서 끝난다.
다음 Lesson으로 넘어가면 앞의 것은 그 폴더에 남는다.

```
조각은 다 만들어 봤는데, 하나로 이어 붙인 적이 없었다.
```

**이걸 알아챈 것이 1주차의 진짜 결과다.** 정리해 놓고 보니 비어 있는 자리가 보였다.

---

## 2. 어떻게 정리했나

번호 대신 **주제 4개**로 묶고, 각 주제 안에 `code/` 와 `document/` 를 **쌍으로** 두었다.

```
study/
 ├ opencv/      이미지 처리와 검사 파이프라인
 │   ├ Code/
 │   └ document/
 ├ unittest/    테스트 작성법
 │   ├ code/
 │   └ document/
 ├ wpf/         화면
 │   ├ code/
 │   └ document/
 └ step/        진행 단계
```

**코드와 설명을 쌍으로 둔 것이 핵심이다.** 코드만 있으면 나중에 못 읽고,
설명만 있으면 돌려 볼 수가 없다.

### opencv — 제일 큰 묶음

기초를 다시 **4단계**로 나눴다. 단계 이름 자체가 검사 프로그램이 하는 일의 순서다.

| 단계 | 내용 | 코드 |
|---|---|---|
| 01 이미지 다루기 기초 | 흑백 변환 · 이미지 로드 · Resize · ROI Crop · 회전/뒤집기 · 도형 그리기 | 6개 |
| 02 이미지 전처리 | 이진화 · 적응형 이진화 · 블러 | 3개 |
| 03 결함 검출 파이프라인 | 블러→Canny→윤곽찾기 · 면적/경계상자 · 합불 규칙 엔진 | 3개 |
| 04 검사 영역 관리 | SubMat ROI · 레시피 파일 읽기 · 제품별 레시피 자동 선택 | 3개 |

그 위에 **완성된 묶음** 넷을 따로 두었다.

| 묶음 | 무엇 |
|---|---|
| `Die_Pipeline/Lesson06` · `Lesson07` | die 검사의 기초 |
| `Die_Pipeline/Lesson15` | **파이프라인 엔진 + JSON 레시피** |
| `Wafer/Lesson16` | **웨이퍼 단위 검사** |

---

## 3. 여기서 이 프로젝트의 재료가 나왔다

정리하면서 *"이건 나중에 쓰겠다"* 가 분명해진 것들이다.

### Lesson15 — 엔진과 레시피

파일 이름만 봐도 지금 만들고 있는 것과 거의 같다.

```
Pipeline/     IPipelineContext · IPipelineNode · PipelineEngine · PipelineResult
Configuration/ Recipe · NodeConfig · ParamDescriptor · ParamValues · ParamCheck · RecipeValidator
Registry/     NodeRegistry · NodeDescriptor
Nodes/        Blur · Binarize · Morphology · Label · Or
```

**여기서 배운 세 가지가 이 프로젝트의 뼈대가 된다.**

| 개념 | 한 줄 |
|---|---|
| **칠판**(`IPipelineContext`) | 노드끼리 직접 말을 걸지 않고, 이름표를 붙여 칠판에 올려 둔다 |
| **레시피**(JSON) | 어떤 순서로 돌릴지를 **코드가 아니라 파일**이 정한다 |
| **검증기**(`RecipeValidator`) | 이름표가 그냥 글씨라 오타를 컴파일러가 못 잡는다 → 실행 전에 검사한다 |

### Lesson16 — 웨이퍼 층

```
DieGrid · Neighbors · WaferMap · WaferRunner · DieToDie · SyntheticWafer · WaferInspection
```

**die 한 장만으로는 검사가 성립하지 않는다**는 것을 여기서 배웠다.
"누가 이웃인가"는 웨이퍼 격자만 아는 정보라서, 격자를 아는 층이 따로 있어야 한다.

### Lesson09 — 테스트 방법

```
GoldenMasterTests · DefectComparer · Fakes · TestImage
```

**`DefectComparer` 에서 배운 것 하나를 꼭 적어 둔다.**

> **허용오차로 비교한다. 완전일치는 쓰지 않는다.**

소수점 계산은 실행마다 마지막 자리가 흔들릴 수 있어서, 글자 단위로 같은지 보면 안 된다.

### Lesson08 — WPF

```
MainViewModel · RelayCommand · IDialogService · ObservableObject · BitmapConvert
```

화면과 로직을 갈라 두는 방식(MVVM), 그리고 **대화상자를 인터페이스로 빼서 테스트 가능하게** 만드는 법.
5주차에 쓸 재료다.

---

## 4. 참조로 둔 것 둘

정리하면서 **내가 만든 것이 아닌 두 레포**를 참조 자리에 두었다.

| | 무엇 |
|---|---|
| `visionstack_proto` | 교수님 레포. 이 문제의 **완성된 참조 구현** |
| `icecore_proto_md` | 장비 소프트웨어 플랫폼 10단계 |

```
IceCore          장비 플랫폼
  └ Phase 7      IVisionAlgorithm 구현체 하나
       └ DiePipeline   ← 만들 것
```

**내가 만드는 것이 전체 어디에 들어가는지**를 여기서 정해 두었다.

---

## 5. 숫자

| | |
|---|---|
| 파일 | **139개** |
| 주제 폴더 | 4개 (opencv · unittest · wpf · step) |
| 기초 코드 | 15개 (4단계) |
| 완성 묶음 | 6개 (Lesson06 · 07 · 08 · 09 · 15 · 16) |
| 설명 문서 | 20편 |

---

## 6. 3개월간 학습하면서

1. **조각은 다 있는데 이어 붙인 적이 없다**는 것을 알았다 → 이 프로젝트의 계기
2. 쓸 재료가 어디 있는지 정해졌다 — 엔진/레시피는 Lesson15, 웨이퍼는 Lesson16, 테스트는 Lesson09, 화면은 Lesson08
3. 내가 만들 것이 **전체 플랫폼의 어느 칸**인지 자리를 잡았다

---

## 7. 다음 주로 넘긴 것

**이어 붙이기.** 동기와 같은 과제를 **각자** 구현해 보고 서로 돌려보기로 했다.

- 이쪽 출발점 — Lesson15·16 (구조부터)
- 동기 출발점 — 브리핑 문서 (알고리즘부터)
