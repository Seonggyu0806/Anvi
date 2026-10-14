# DiePipeline

반도체 웨이퍼 사진 한 장을 받아, 위에 있는 die 를 하나씩 검사해 **어느 die 의 어디에 결함이 있는지** 목록을 내는 프로그램.

- 결함의 모양을 배우지 않는다. 이웃 die 를 합쳐 정상 기준(골든)을 만들고, 그것과 다른 곳을 결함으로 본다(die-to-die)
- 처리 순서와 값은 JSON 레시피가 정한다. die 하나는 die 레시피, 웨이퍼 한 장은 웨이퍼 레시피
- 기준 구현: 교수님 레포 `visionstack_proto` — 이름 · 구조 · 기본값이 갈라지면 이쪽을 따른다

## 빠른 시작

```bash
dotnet build DiePipeline.slnx
dotnet test  tests/DiePipeline.Tests
```

합성 웨이퍼를 그려 웨이퍼 레시피 견본으로 검사한다. 데이터가 필요 없다.

```bash
mkdir out
dotnet run --project src/DiePipeline.App -- demo -o out/wafer.bmp
dotnet run --project src/DiePipeline.App -- wafer -i out/wafer.bmp --recipe samples/wafer/demo.wafer.json
```

`demo` 가 출력한 심은 결함 자리와 `wafer` 가 찾은 자리가 같으면 정상이다.

화면:

```bash
dotnet run --project src/DiePipeline.Wpf
```

## 흐름

```
웨이퍼  표식 찾기 → 자세 계산 → die 전부 검사 → 웨이퍼 좌표 → 결과 추리기
die     이웃 합치기(골든) → 빼기 → 이진화 → 덩어리 세기 → 결함 목록
```

## 구조

| 프로젝트 | 하는 일 |
|---|---|
| `Core` | 파이프라인 엔진 · 레시피와 검증 · 노드 명부 · 원본 부분 읽기. 이미지 라이브러리를 모른다 |
| `Cv` | 알고리즘과 노드(OpenCvSharp) · 이미지 소스 · 합성 웨이퍼 |
| `Wafer` | 격자 · 존 · 정렬 · 이웃 선택 · die 반복 · 결과 처리 · 웨이퍼 레시피 |
| `Editor` | 블록 그래프 편집 규칙 · 그래프 ↔ 레시피 변환 · 자동 배치 · 편집 이력 |
| `Viewer` | 원본 이미지 뷰어의 계산 — 화면 ↔ 원본 좌표 · 축소본 · 보이는 영역 · 영역 끌기 · 도장 |
| `Wpf` | 화면 — 작업공간 6개: 다이(블록 에디터 · 오버레이) · 웨이퍼(블록 에디터 · 오버레이 · 옵션 검사) · 원본(이미지 뷰어) |
| `App` | 명령행 `diepipe` — `demo` · `wafer` · `run` · `probe` |

| 폴더 | |
|---|---|
| `samples/recipes` | die 레시피 4개 |
| `samples/wafer` | 웨이퍼 레시피 견본 · 표식 조각(코드가 그린 그림) |
| `tests/DiePipeline.Tests` | 테스트 666개 |

## 문서

| 문서 | 내용 |
|---|---|
| `docs/guide.md` | **처음 읽는 문서** — 무엇을 왜 어떻게 만들었나 |
| `docs/wiki/` | 주차별 요약 |
| `docs/doc_code/` | 주차별 상세 · 웨이퍼 층 상세 |
| `docs/deferred.md` | 미룬 기능과 넣을 조건 |
| `docs/todo.md` | 남은 일 |

## 회사 자료

실데이터와 그 정보(원본 파일 이름 · 해상도 · 크기 · 제품명 · 격자 · 피치 · 원판 위치 · 사양)는 이 레포에 넣지 않는다.
원본과 실데이터 웨이퍼 레시피는 레포 밖에 두고 경로만 명령행으로 넘긴다. 화면에서 원본으로 만든 파일(케이스 · 골든 · 표식 조각 · 보고서)은 레포 안이면 저장을 거부한다. `.gitignore` 는 두 번째 방어선이다.
